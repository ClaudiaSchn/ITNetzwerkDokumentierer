using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

/// <summary>Port, an dem ein Anschluss hängt (kurz, für Listen).</summary>
public record AngeschlossenAnDto(int PortId, int GeraetId, string Geraet, int Nummer, string Seite, string? Bezeichnung, string Portart);

public record AnschlussDto(int Id, uint Version, string Ende, int Nummer, AngeschlossenAnDto? Port);

/// <summary>Medium mit beiden Enden (Positionen) und allen Anschlüssen.</summary>
public record MediumAnschluesseDto(MediumDto Medium, PositionKurzDto PositionA, PositionKurzDto PositionB, IReadOnlyList<AnschlussDto> Anschluesse);

/// <summary>Port zur Auswahl: <see cref="Erlaubt"/> = Portart passt zum Medientyp; <see cref="BelegtDurch"/> z.B. «K-12 A3».</summary>
public record PortWahlDto(int Id, int Nummer, string Seite, string? Bezeichnung, string Portart, bool Erlaubt, int? AnschlussId, string? BelegtDurch);

public record GeraetWahlDto(int Id, string Bezeichnung, string Kategorie, IReadOnlyList<PortWahlDto> Ports);

/// <summary>Geräte und Ports an beiden Enden eines Mediums.</summary>
public record PortauswahlDto(IReadOnlyList<GeraetWahlDto> A, IReadOnlyList<GeraetWahlDto> B);

public record AnschlussSetzen(uint Version, int? PortId);

/// <summary>
/// Fortlaufend anschliessen: Anschlüsse <see cref="AbNummer"/> … (+<see cref="Anzahl"/>−1) am Ende
/// <see cref="Ende"/> an die Ports <see cref="AbPortnummer"/> … desselben Geräts und derselben Seite.
/// </summary>
public record FortlaufendAnschliessen(string? Ende, int AbNummer, int Anzahl, int GeraetId, string? Seite, int AbPortnummer);

public static class AnschlussEndpunkte
{
    public static void MapAnschlussEndpunkte(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/medien/{id:int}/anschluesse", Anschluesse);
        app.MapGet("/api/medien/{id:int}/portauswahl", Portauswahl);
        app.MapPut("/api/anschluesse/{id:int}", Setzen);
        app.MapPost("/api/medien/{id:int}/fortlaufend", Fortlaufend);
    }

    private static async Task<IResult> Anschluesse(int id, NetzDbContext db, CancellationToken abbruch)
    {
        var medium = await db.Medien.AsNoTracking().Where(m => m.Id == id)
            .Select(m => new
            {
                Dto = new MediumDto(m.Id, m.Version, m.VerbindungId, m.Kabelnummer, m.MedientypId, m.Medientyp.Name, m.AnzahlAnschluesse,
                    m.Anschluesse.Count(a => a.Ende == Mediumende.A && a.PortId != null),
                    m.Anschluesse.Count(a => a.Ende == Mediumende.B && a.PortId != null)),
                A = new PositionKurzDto(m.Verbindung.PositionA.Id, m.Verbindung.PositionA.Bezeichnung,
                    m.Verbindung.PositionA.Positionstyp.Name, m.Verbindung.PositionA.Stockwerk),
                B = new PositionKurzDto(m.Verbindung.PositionB.Id, m.Verbindung.PositionB.Bezeichnung,
                    m.Verbindung.PositionB.Positionstyp.Name, m.Verbindung.PositionB.Stockwerk)
            })
            .SingleOrDefaultAsync(abbruch);
        if (medium is null) return Fehler.NichtGefunden("Medium nicht gefunden.");

        var anschluesse = await db.Anschluesse.AsNoTracking().Where(a => a.MediumId == id)
            .OrderBy(a => a.Ende).ThenBy(a => a.Nummer)
            .Select(a => new
            {
                a.Id, a.Version, a.Ende, a.Nummer,
                Port = a.Port == null ? null : new
                {
                    a.Port.Id, a.Port.GeraetId, Geraet = a.Port.Geraet.Bezeichnung, a.Port.Nummer, a.Port.Seite,
                    a.Port.Bezeichnung, Portart = a.Port.Portart.Name
                }
            })
            .ToListAsync(abbruch);

        return Results.Ok(new MediumAnschluesseDto(medium.Dto, medium.A, medium.B, anschluesse.Select(a => new AnschlussDto(
            a.Id, a.Version, a.Ende.ToString(), a.Nummer,
            a.Port is null ? null : new AngeschlossenAnDto(a.Port.Id, a.Port.GeraetId, a.Port.Geraet, a.Port.Nummer,
                a.Port.Seite.ToString(), a.Port.Bezeichnung, a.Port.Portart))).ToList()));
    }

    /// <summary>Alle Geräte und Ports an den beiden End-Positionen, mit «erlaubt» und Belegung.</summary>
    private static async Task<IResult> Portauswahl(int id, NetzDbContext db, CancellationToken abbruch)
    {
        var m = await db.Medien.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Verbindung.PositionAId, x.Verbindung.PositionBId, Erlaubt = x.Medientyp.ErlaubtePortarten.Select(p => p.Id).ToList() })
            .SingleOrDefaultAsync(abbruch);
        if (m is null) return Fehler.NichtGefunden("Medium nicht gefunden.");

        async Task<List<GeraetWahlDto>> Geraete(int positionId)
        {
            var geraete = await db.Geraete.AsNoTracking().Where(g => g.PositionId == positionId).OrderBy(g => g.Bezeichnung)
                .Select(g => new
                {
                    g.Id, g.Bezeichnung, Kategorie = g.Geraetetyp.Kategorie,
                    Ports = g.Ports.OrderBy(p => p.Nummer).ThenBy(p => p.Seite).Select(p => new
                    {
                        p.Id, p.Nummer, p.Seite, p.Bezeichnung, p.PortartId, Portart = p.Portart.Name,
                        AnschlussId = p.Anschluss == null ? (int?)null : p.Anschluss.Id,
                        Kabel = p.Anschluss == null ? null : p.Anschluss.Medium.Kabelnummer,
                        Ende = p.Anschluss == null ? (Mediumende?)null : p.Anschluss.Ende,
                        AnschlussNummer = p.Anschluss == null ? (int?)null : p.Anschluss.Nummer,
                        Gepatcht = p.Patchende != null
                    }).ToList()
                })
                .ToListAsync(abbruch);
            return geraete.Select(g => new GeraetWahlDto(g.Id, g.Bezeichnung, g.Kategorie.ToString(), g.Ports.Select(p => new PortWahlDto(
                p.Id, p.Nummer, p.Seite.ToString(), p.Bezeichnung, p.Portart, m.Erlaubt.Contains(p.PortartId), p.AnschlussId,
                p.Kabel is not null ? $"{p.Kabel} {p.Ende}{p.AnschlussNummer}" : p.Gepatcht ? "gepatcht" : null)).ToList())).ToList();
        }

        return Results.Ok(new PortauswahlDto(await Geraete(m.PositionAId), await Geraete(m.PositionBId)));
    }

    /// <summary>
    /// Use Case «Medium anschliessen / Anschluss lösen»: einen Anschluss an einen Port hängen (oder mit
    /// PortId = null lösen). Geprüft: Port an der richtigen End-Position (Regel 7), Portart erlaubt,
    /// Port frei (Regel 8, zusätzlich eindeutiger Index in der Datenbank).
    /// </summary>
    private static async Task<IResult> Setzen(int id, AnschlussSetzen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var anschluss = await db.Anschluesse.Include(a => a.Medium).ThenInclude(m => m.Verbindung)
            .FirstOrDefaultAsync(a => a.Id == id, abbruch);
        if (anschluss is null) return Fehler.NichtGefunden("Anschluss nicht gefunden.");

        if (eingabe.PortId is { } portId)
        {
            var port = await PortLaden(db, portId, abbruch);
            if (port is null) return Fehler.NichtGefunden("Port nicht gefunden.");
            if (await Pruefen(db, anschluss, port, abbruch) is { } fehler) return fehler;
        }

        db.Entry(anschluss).Property(a => a.Version).OriginalValue = eingabe.Version;
        anschluss.PortId = eingabe.PortId;
        db.Entry(anschluss).Property(a => a.PortId).IsModified = true;
        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Konflikt("Der Anschluss wurde inzwischen von jemand anderem geändert. Bitte neu laden.");
        }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt("Der Port wurde inzwischen von jemand anderem belegt. Bitte neu laden.");
        }
        return await Anschluesse(anschluss.MediumId, db, abbruch);
    }

    /// <summary>
    /// Mehrere Anschlüsse eines Endes fortlaufend an die Ports eines Geräts hängen (z.B. Fasern 1–12 an
    /// Ports 1–12 hinten). Alles oder nichts: beim ersten Problem wird nichts gespeichert.
    /// </summary>
    private static async Task<IResult> Fortlaufend(int id, FortlaufendAnschliessen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var medium = await db.Medien.Include(m => m.Verbindung).Include(m => m.Anschluesse)
            .FirstOrDefaultAsync(m => m.Id == id, abbruch);
        if (medium is null) return Fehler.NichtGefunden("Medium nicht gefunden.");
        if (!Enum.TryParse<Mediumende>(eingabe.Ende, out var ende) || !Enum.IsDefined(ende))
            return Fehler.Ungueltig("Bitte das Ende A oder B angeben.");
        if (eingabe.Anzahl < 1 || eingabe.AbNummer < 1 || eingabe.AbNummer + eingabe.Anzahl - 1 > medium.AnzahlAnschluesse)
            return Fehler.Ungueltig($"Die Anschlüsse müssen zwischen 1 und {medium.AnzahlAnschluesse} liegen.");

        var geraet = await db.Geraete.AsNoTracking().FirstOrDefaultAsync(g => g.Id == eingabe.GeraetId, abbruch);
        if (geraet is null) return Fehler.NichtGefunden("Gerät nicht gefunden.");
        Portseite? seite = null;
        if (!string.IsNullOrEmpty(eingabe.Seite))
        {
            if (!Enum.TryParse<Portseite>(eingabe.Seite, out var s) || !Enum.IsDefined(s)) return Fehler.Ungueltig("Unbekannte Portseite.");
            seite = s;
        }

        var portnummern = Enumerable.Range(eingabe.AbPortnummer, eingabe.Anzahl).ToList();
        var ports = await db.Ports.Include(p => p.Geraet).Include(p => p.Anschluss).ThenInclude(a => a!.Medium).Include(p => p.Patchende)
            .Where(p => p.GeraetId == geraet.Id && portnummern.Contains(p.Nummer) && (seite == null || p.Seite == seite))
            .ToListAsync(abbruch);
        if (ports.Select(p => p.Nummer).Distinct().Count() != ports.Count)
            return Fehler.Ungueltig("Bitte die Seite (vorne oder hinten) wählen.");

        for (var i = 0; i < eingabe.Anzahl; i++)
        {
            var nummer = eingabe.AbNummer + i;
            var port = ports.FirstOrDefault(p => p.Nummer == eingabe.AbPortnummer + i);
            if (port is null) return Fehler.Ungueltig($"«{geraet.Bezeichnung}» hat keinen Port {eingabe.AbPortnummer + i}.");
            var anschluss = medium.Anschluesse.Single(a => a.Ende == ende && a.Nummer == nummer);
            if (anschluss.PortId == port.Id) continue;
            if (await Pruefen(db, anschluss, port, abbruch) is { } fehler) return fehler;
            anschluss.PortId = port.Id;
        }

        try
        {
            await db.SaveChangesAsync(abbruch); // alle Anschlüsse in einer Transaktion
        }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt("Ein Port wurde inzwischen von jemand anderem belegt. Es wurde nichts gespeichert. Bitte neu laden.");
        }
        return await Anschluesse(id, db, abbruch);
    }

    private static Task<Port?> PortLaden(NetzDbContext db, int portId, CancellationToken abbruch) =>
        db.Ports.Include(p => p.Geraet).Include(p => p.Anschluss).ThenInclude(a => a!.Medium).Include(p => p.Patchende)
            .FirstOrDefaultAsync(p => p.Id == portId, abbruch);

    /// <summary>Regeln 7 und 8: richtige End-Position, Portart erlaubt, Port frei.</summary>
    private static async Task<IResult?> Pruefen(NetzDbContext db, Anschluss anschluss, Port port, CancellationToken abbruch)
    {
        var verbindung = anschluss.Medium.Verbindung;
        var sollPosition = anschluss.Ende == Mediumende.A ? verbindung.PositionAId : verbindung.PositionBId;
        var portName = $"Port {port.Nummer}{SeiteText(port.Seite)} an «{port.Geraet.Bezeichnung}»";
        if (port.Geraet.PositionId != sollPosition)
        {
            var soll = await db.Positionen.Where(p => p.Id == sollPosition).Select(p => p.Bezeichnung).SingleAsync(abbruch);
            return Fehler.Ungueltig($"Ende {anschluss.Ende} gehört an «{soll}» – {portName} steht an einer anderen Position.");
        }

        var erlaubt = await db.Medientypen.Where(t => t.Id == anschluss.Medium.MedientypId)
            .SelectMany(t => t.ErlaubtePortarten.Select(p => p.Id)).ContainsAsync(port.PortartId, abbruch);
        if (!erlaubt)
        {
            var portart = await db.Portarten.Where(p => p.Id == port.PortartId).Select(p => p.Name).SingleAsync(abbruch);
            return Fehler.Ungueltig($"{portName} ist {portart} – für dieses Medium nicht erlaubt.");
        }

        if (port.Patchende is not null)
            return Fehler.Konflikt($"{portName} ist gepatcht. Ein Port ist entweder an ein Kabel angeschlossen oder gepatcht.");
        if (port.Anschluss is { } belegt && belegt.Id != anschluss.Id)
            return Fehler.Konflikt($"{portName} ist bereits belegt ({belegt.Medium.Kabelnummer} {belegt.Ende}{belegt.Nummer}).");
        return null;
    }

    private static string SeiteText(Portseite seite) => seite switch
    {
        Portseite.Vorne => " vorne",
        Portseite.Hinten => " hinten",
        _ => ""
    };
}
