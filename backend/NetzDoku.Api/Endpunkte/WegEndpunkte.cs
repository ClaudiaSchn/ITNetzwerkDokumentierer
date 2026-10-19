using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Wegsuche;

namespace NetzDoku.Api.Endpunkte;

/// <summary>Medium auf einem Wegabschnitt; <see cref="Frei"/> = Fasern/Adern, die für eine neue Strecke verfügbar sind.</summary>
public record WegMediumDto(int Id, string Kabelnummer, string Medientyp, int Anzahl, int Frei);

/// <summary>Abschnitt eines Weges: eine Verbindung, in Wegrichtung von → nach.</summary>
public record WegAbschnittDto(int VerbindungId, PositionKurzDto Von, PositionKurzDto Nach, double LaengeMeter, int Frei,
    IReadOnlyList<WegMediumDto> Medien);

public record WegDto(double LaengeMeter, int Frei, IReadOnlyList<WegAbschnittDto> Abschnitte);

public record WegeDto(IReadOnlyList<WegDto> Wege, bool Begrenzt);

/// <summary>Port in einem Leitungsverlauf.</summary>
public record VerlaufPortDto(int Id, int GeraetId, string Geraet, string Kategorie, int Nummer, string Seite, string? Bezeichnung, string Portart);

/// <summary>Punkt im Verlauf: ein Port oder (Port = null) ein offenes, nicht aufgelegtes Faser-Ende an einer Position.</summary>
public record VerlaufPunktDto(VerlaufPortDto? Port, PositionKurzDto Position);

/// <summary>Glied zwischen zwei Punkten: «kabel», «durchgang» (vorne ↔ hinten) oder «patch».</summary>
public record VerlaufGliedDto(string Art, int? MediumId, string? Kabelnummer, string? Faser, int? VerbindungId, int? PatchungId, string? Bezeichnung);

public record VerlaufDto(IReadOnlyList<VerlaufPunktDto> Punkte, IReadOnlyList<VerlaufGliedDto> Glieder, double LaengeMeter,
    IReadOnlyList<int> VerbindungIds);

public static class WegEndpunkte
{
    /// <summary>Höchstzahl gelieferter Wege zwischen zwei Positionen.</summary>
    private const int MaxWege = 5;
    /// <summary>Höchstzahl Abschnitte eines Weges.</summary>
    private const int MaxTiefe = 12;
    /// <summary>Abbruch der Suche nach so vielen Schritten (schützt vor sehr vermaschten Netzen).</summary>
    private const int MaxSchritte = 100_000;

    public static void MapWegEndpunkte(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/standorte/{id:int}/wege", Wege);
        app.MapGet("/api/ports/{id:int}/verlauf", Verlauf);
        app.MapGet("/api/geraete/{id:int}/wege", GeraetZuGeraet);
    }

    /// <summary>
    /// Use Case «Weg suchen» von Position zu Position über Verbindungen. Mit <paramref name="nurFrei"/> nur über
    /// Verbindungen mit mindestens einer freien Faser/Ader; mit <paramref name="medientypId"/> nur Medien dieses Typs.
    /// Liefert die kürzesten Wege (Summe der Luftlinien), höchstens fünf.
    /// </summary>
    private static async Task<IResult> Wege(int id, int von, int nach, bool? nurFrei, int? medientypId,
        NetzDbContext db, CancellationToken abbruch)
    {
        if (!await db.Standorte.AnyAsync(s => s.Id == id, abbruch)) return Fehler.NichtGefunden("Standort nicht gefunden.");
        var netz = await Netz.LadenAsync(db, id, abbruch);
        if (!netz.Positionen.ContainsKey(von) || !netz.Positionen.ContainsKey(nach))
            return Fehler.Ungueltig("Bitte Start und Ziel aus diesem Standort wählen.");
        if (von == nach) return Fehler.Ungueltig("Start und Ziel sind dieselbe Position.");

        // Abschnitte mit ihren Medien vorbereiten und filtern
        var abschnitte = new Dictionary<int, (Netz.VerbindungInfo V, List<WegMediumDto> Medien, int Frei)>();
        var medienJeVerbindung = netz.Medien.Values.ToLookup(m => m.VerbindungId);
        foreach (var v in netz.Verbindungen.Values)
        {
            var medien = medienJeVerbindung[v.Id]
                .Where(m => medientypId is null || m.MedientypId == medientypId)
                .OrderBy(m => m.Kabelnummer)
                .Select(m => new WegMediumDto(m.Id, m.Kabelnummer, m.Medientyp, m.Anzahl, netz.FreieFasern(m)))
                .ToList();
            var frei = medien.Sum(m => m.Frei);
            if (medientypId is not null && medien.Count == 0) continue;
            if (nurFrei == true && frei == 0) continue;
            abschnitte[v.Id] = (v, medien, frei);
        }
        var nachbarn = abschnitte.Values
            .SelectMany(a => new[] { (Von: a.V.A, Nach: a.V.B, a.V.Id), (Von: a.V.B, Nach: a.V.A, a.V.Id) })
            .ToLookup(x => x.Von);

        // Alle einfachen Wege per Tiefensuche, kürzeste zuerst ausgeben
        var gefunden = new List<(double Laenge, List<(int VerbindungId, int Von, int Nach)> Weg)>();
        var pfad = new List<(int, int, int)>();
        var besucht = new HashSet<int> { von };
        var schritte = 0;
        var begrenzt = false;

        void Suchen(int position, double laenge)
        {
            if (++schritte > MaxSchritte) { begrenzt = true; return; }
            if (position == nach)
            {
                gefunden.Add((laenge, [.. pfad]));
                return;
            }
            if (pfad.Count >= MaxTiefe) { begrenzt = true; return; }
            foreach (var (_, ziel, vid) in nachbarn[position].OrderBy(x => abschnitte[x.Id].V.LaengeMeter))
            {
                if (!besucht.Add(ziel)) continue;
                pfad.Add((vid, position, ziel));
                Suchen(ziel, laenge + abschnitte[vid].V.LaengeMeter);
                pfad.RemoveAt(pfad.Count - 1);
                besucht.Remove(ziel);
            }
        }
        Suchen(von, 0);

        PositionKurzDto Kurz(int pid)
        {
            var p = netz.Positionen[pid];
            return new PositionKurzDto(p.Id, p.Bezeichnung, p.Typ, p.Stockwerk);
        }

        var wege = gefunden.OrderBy(w => w.Laenge).ThenBy(w => w.Weg.Count).Take(MaxWege)
            .Select(w =>
            {
                var teile = w.Weg.Select(x =>
                {
                    var a = abschnitte[x.VerbindungId];
                    return new WegAbschnittDto(x.VerbindungId, Kurz(x.Von), Kurz(x.Nach), a.V.LaengeMeter, a.Frei, a.Medien);
                }).ToList();
                return new WegDto(w.Laenge, teile.Min(t => t.Frei), teile);
            })
            .ToList();
        return Results.Ok(new WegeDto(wege, begrenzt));
    }

    /// <summary>Leitungsverlauf durch einen Port (Kabel, Patchpanels, Patchungen) bis zu den Endgeräten bzw. offenen Enden.</summary>
    private static async Task<IResult> Verlauf(int id, NetzDbContext db, CancellationToken abbruch)
    {
        var standortId = await db.Ports.Where(p => p.Id == id).Select(p => (int?)p.Geraet.Position.StandortId).SingleOrDefaultAsync(abbruch);
        if (standortId is null) return Fehler.NichtGefunden("Port nicht gefunden.");
        var netz = await Netz.LadenAsync(db, standortId.Value, abbruch);
        return Results.Ok(AlsDto(netz, netz.Verlauf(id)));
    }

    /// <summary>
    /// Wege von Gerät zu Gerät: alle Leitungsverläufe, die an einem Port des einen Geräts beginnen und an einem
    /// Port des anderen enden. Der Verlauf wird auf den Abschnitt zwischen den beiden Geräten gekürzt.
    /// </summary>
    private static async Task<IResult> GeraetZuGeraet(int id, int nach, NetzDbContext db, CancellationToken abbruch)
    {
        var standorte = await db.Geraete.Where(g => g.Id == id || g.Id == nach)
            .Select(g => new { g.Id, g.Position.StandortId }).ToListAsync(abbruch);
        if (standorte.Count != (id == nach ? 1 : 2)) return Fehler.NichtGefunden("Gerät nicht gefunden.");
        if (id == nach) return Fehler.Ungueltig("Bitte zwei verschiedene Geräte wählen.");
        if (standorte[0].StandortId != standorte[1].StandortId) return Fehler.Ungueltig("Die Geräte stehen an verschiedenen Standorten.");

        var netz = await Netz.LadenAsync(db, standorte[0].StandortId, abbruch);
        var ergebnisse = new List<VerlaufDto>();
        var gesehen = new HashSet<string>();
        foreach (var port in netz.Ports.Values.Where(p => p.GeraetId == id).OrderBy(p => p.Nummer).ThenBy(p => p.Seite))
        {
            if (netz.PortFrei(port.Id)) continue;
            var (punkte, glieder) = netz.Verlauf(port.Id);
            // Abschnitt zwischen einem Port von «id» und einem Port von «nach» (die nächstgelegenen)
            var i = punkte.FindIndex(p => p.Port?.Id == port.Id);
            var j = -1;
            for (var d = 1; d < punkte.Count && j < 0; d++)
            {
                if (i + d < punkte.Count && punkte[i + d].Port?.GeraetId == nach) j = i + d;
                else if (i - d >= 0 && punkte[i - d].Port?.GeraetId == nach) j = i - d;
            }
            if (j < 0) continue;
            var (von, bis) = i < j ? (i, j) : (j, i);
            var teilPunkte = punkte.GetRange(von, bis - von + 1);
            var teilGlieder = glieder.GetRange(von, bis - von);
            if (i > j) { teilPunkte.Reverse(); teilGlieder.Reverse(); }
            if (!gesehen.Add(string.Join(",", teilPunkte.Select(p => p.Port?.Id).Order()))) continue;
            ergebnisse.Add(AlsDto(netz, (teilPunkte, teilGlieder)));
        }
        return Results.Ok(ergebnisse);
    }

    private static VerlaufDto AlsDto(Netz netz, (List<Netz.Punkt> Punkte, List<Netz.Glied> Glieder) v)
    {
        PositionKurzDto Kurz(int pid)
        {
            var p = netz.Positionen[pid];
            return new PositionKurzDto(p.Id, p.Bezeichnung, p.Typ, p.Stockwerk);
        }
        var verbindungen = v.Glieder.Where(g => g.VerbindungId != null).Select(g => g.VerbindungId!.Value).Distinct().ToList();
        return new VerlaufDto(
            v.Punkte.Select(p => new VerlaufPunktDto(
                p.Port is { } x ? new VerlaufPortDto(x.Id, x.GeraetId, x.Geraet, x.Kategorie.ToString(), x.Nummer, x.Seite.ToString(), x.Bezeichnung, x.Portart) : null,
                Kurz(p.PositionId))).ToList(),
            v.Glieder.Select(g => new VerlaufGliedDto(g.Art, g.MediumId, g.Kabelnummer, g.Faser, g.VerbindungId, g.PatchungId, g.Bezeichnung)).ToList(),
            v.Glieder.Where(g => g.VerbindungId != null).Sum(g => netz.Verbindungen[g.VerbindungId!.Value].LaengeMeter),
            verbindungen);
    }
}
