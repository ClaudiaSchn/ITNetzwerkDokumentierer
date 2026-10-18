using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Dienste;

/// <summary>
/// Referenzieren von Plänen über Passpunkte und Pflege der Bezugskette
/// (z.B. Serverraum → Gebäude A EG → Campusplan).
/// </summary>
public static class Referenzierung
{
    /// <summary>
    /// Berechnet Massstab, Drehung und Versatz eines Plans aus seinen zwei Passpunkten und der
    /// aktuellen Lage des Bezugsplans. Erwartet geladene <see cref="Plan.Passpunkte"/>.
    /// </summary>
    public static void AusPasspunkten(Plan plan, Plan bezug)
    {
        var pp = plan.Passpunkte.OrderBy(p => p.Nummer).ToList();
        if (pp.Count != 2) throw new InvalidOperationException("Für die Referenzierung braucht es genau zwei Passpunkte.");
        if (!bezug.Referenziert) throw new InvalidOperationException("Der Bezugsplan ist nicht referenziert.");

        var a = Geometrie.RichteAus(
            new Planpunkt(pp[0].PlanX, pp[0].PlanY), Geometrie.NachAbsolut(bezug, new Planpunkt(pp[0].BezugX, pp[0].BezugY)),
            new Planpunkt(pp[1].PlanX, pp[1].PlanY), Geometrie.NachAbsolut(bezug, new Planpunkt(pp[1].BezugX, pp[1].BezugY)));

        plan.Massstab = a.Massstab;
        plan.Drehung = a.Drehung;
        plan.VersatzX = a.VersatzX;
        plan.VersatzY = a.VersatzY;
        plan.Referenziert = true;
    }

    /// <summary>Alle aktiven Pläne eines Standorts mit Passpunkten (werden verfolgt, d.h. Änderungen werden gespeichert).</summary>
    public static Task<List<Plan>> PlaeneMitPasspunkten(NetzDbContext db, int standortId, CancellationToken abbruch) =>
        db.Plaene.Include(p => p.Passpunkte).Where(p => p.StandortId == standortId && p.Aktiv).ToListAsync(abbruch);

    /// <summary>Ids aller Pläne, die direkt oder über eine Kette auf <paramref name="planId"/> referenziert sind.</summary>
    public static HashSet<int> Nachfolger(IEnumerable<Plan> plaene, int planId)
    {
        var kinder = plaene.Where(p => p.BezugsplanId is not null).ToLookup(p => p.BezugsplanId!.Value);
        var ergebnis = new HashSet<int>();
        var offen = new Queue<int>([planId]);
        while (offen.Count > 0)
        {
            foreach (var kind in kinder[offen.Dequeue()])
                if (ergebnis.Add(kind.Id)) offen.Enqueue(kind.Id);
        }
        return ergebnis;
    }

    /// <summary>
    /// Nach einer Änderung der Lage von <paramref name="geaendert"/> alle darauf referenzierten Pläne neu
    /// berechnen (über ihre gespeicherten Passpunkte), damit die ganze Kette stimmt.
    /// <paramref name="plaene"/> muss <paramref name="geaendert"/> enthalten (gleiches Objekt).
    /// Gibt die Anzahl neu berechneter Pläne zurück. Gespeichert wird mit dem nächsten SaveChanges.
    /// </summary>
    public static int NachfolgerNeuBerechnen(IReadOnlyCollection<Plan> plaene, Plan geaendert)
    {
        var kinder = plaene.Where(p => p.BezugsplanId is not null).ToLookup(p => p.BezugsplanId!.Value);
        var anzahl = 0;
        var offen = new Queue<Plan>([geaendert]);
        var besucht = new HashSet<int> { geaendert.Id };
        while (offen.Count > 0)
        {
            var eltern = offen.Dequeue();
            foreach (var kind in kinder[eltern.Id])
            {
                if (!besucht.Add(kind.Id)) continue; // Schutz gegen Zyklen
                if (kind.Methode == Referenzierungsmethode.Passpunkte && kind.Referenziert && kind.Passpunkte.Count == 2)
                {
                    AusPasspunkten(kind, eltern);
                    anzahl++;
                }
                offen.Enqueue(kind);
            }
        }
        return anzahl;
    }
}
