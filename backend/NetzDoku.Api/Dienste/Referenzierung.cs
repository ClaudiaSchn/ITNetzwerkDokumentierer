using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Dienste;

/// <summary>Linie im Plan von <see cref="Von"/> nach <see cref="Bis"/> (Planeinheiten).</summary>
public record Richtungslinie(Planpunkt Von, Planpunkt Bis);

/// <summary>
/// Angaben einer Referenzierung mit eigenem Massstab (Massstab selbst: über die Messstrecken).
/// Lage: Ankerpunkt im Plan und entweder derselbe Punkt im Bezugsplan oder seine Koordinate in Metern.
/// Drehung: entweder als Winkel oder als Richtung (Linie im Plan und dieselbe Richtung im Bezugsplan,
/// z.B. beide Nordpfeile).
/// </summary>
public record EigenerMassstabDaten(
    Planpunkt Anker,
    Planpunkt? AnkerBezug,
    Weltpunkt? AnkerAbsolut,
    double? Winkel,
    Richtungslinie? RichtungPlan,
    Richtungslinie? RichtungBezug)
{
    private static readonly JsonSerializerOptions Optionen = new(JsonSerializerDefaults.Web);

    public string AlsJson() => JsonSerializer.Serialize(this, Optionen);

    public static EigenerMassstabDaten? AusJson(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<EigenerMassstabDaten>(json, Optionen);
}

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

    /// <summary>
    /// Berechnet Massstab (aus den Messstrecken), Drehung und Versatz eines Plans mit eigenem Massstab.
    /// Erwartet geladene <see cref="Plan.Messstrecken"/> und gesetzte <see cref="Plan.EigenerMassstabDaten"/>.
    /// </summary>
    public static void AusEigenemMassstab(Plan plan, Plan bezug)
    {
        var d = EigenerMassstabDaten.AusJson(plan.EigenerMassstabDaten)
                ?? throw new InvalidOperationException("Angaben zum eigenen Massstab fehlen.");
        var strecken = plan.Messstrecken.OrderBy(m => m.Nummer)
            .Select(m => new BekannteStrecke(new(m.U1, m.V1), new(m.U2, m.V2), m.LaengeMeter)).ToList();
        if (!bezug.Referenziert && (d.AnkerAbsolut is null || d.Winkel is null))
            throw new InvalidOperationException("Der Bezugsplan ist nicht referenziert.");

        var drehung = d.Winkel ?? (
            Geometrie.RichtungAbsolut(Geometrie.NachAbsolut(bezug, d.RichtungBezug!.Von), Geometrie.NachAbsolut(bezug, d.RichtungBezug.Bis))
            - Geometrie.RichtungImPlan(d.RichtungPlan!.Von, d.RichtungPlan.Bis));
        var anker = d.AnkerAbsolut ?? Geometrie.NachAbsolut(bezug, d.AnkerBezug!.Value);

        var a = Geometrie.MitEigenemMassstab(Geometrie.MassstabAus(strecken), drehung, d.Anker, anker);
        plan.Massstab = a.Massstab;
        plan.Drehung = a.Drehung;
        plan.VersatzX = a.VersatzX;
        plan.VersatzY = a.VersatzY;
        plan.Referenziert = true;
    }

    /// <summary>
    /// Alle aktiven Pläne eines Standorts mit Passpunkten und Messstrecken (werden verfolgt, d.h.
    /// Änderungen werden gespeichert).
    /// </summary>
    public static Task<List<Plan>> PlaeneMitLageangaben(NetzDbContext db, int standortId, CancellationToken abbruch) =>
        db.Plaene.Include(p => p.Passpunkte).Include(p => p.Messstrecken)
            .Where(p => p.StandortId == standortId && p.Aktiv).ToListAsync(abbruch);

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
                if (!kind.Referenziert) { offen.Enqueue(kind); continue; }
                if (kind.Methode == Referenzierungsmethode.Passpunkte && kind.Passpunkte.Count == 2)
                {
                    AusPasspunkten(kind, eltern);
                    anzahl++;
                }
                else if (kind.Methode == Referenzierungsmethode.EigenerMassstab && kind.EigenerMassstabDaten is not null
                         && kind.Messstrecken.Count > 0)
                {
                    AusEigenemMassstab(kind, eltern);
                    anzahl++;
                }
                offen.Enqueue(kind);
            }
        }
        return anzahl;
    }
}
