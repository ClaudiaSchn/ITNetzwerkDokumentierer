using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

/// <summary>Port kurz, z.B. als Ende einer Patchung.</summary>
public record PortKurzDto(int PortId, int GeraetId, string Geraet, int Nummer, string Seite, string? Bezeichnung, string Portart);

public record PatchungDto(int Id, uint Version, int PositionId, string? Bezeichnung, PortKurzDto Port1, PortKurzDto Port2);

public record PatchungAnlegen(int Port1Id, int Port2Id, string? Bezeichnung);

public record PatchungAendern(uint Version, string? Bezeichnung);

/// <summary>Port an einer Position zur Auswahl; <see cref="Belegt"/> = angeschlossen oder gepatcht (mit Text).</summary>
public record PositionsportDto(int Id, int Nummer, string Seite, string? Bezeichnung, string Portart, string? Belegt);

public record PositionsgeraetDto(int Id, string Bezeichnung, string Kategorie, IReadOnlyList<PositionsportDto> Ports);

public static class PatchungEndpunkte
{
    public static void MapPatchungEndpunkte(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/positionen/{positionId:int}/patchungen", async (int positionId, NetzDbContext db, CancellationToken abbruch) =>
        {
            if (!await db.Positionen.AnyAsync(p => p.Id == positionId, abbruch)) return Fehler.NichtGefunden("Position nicht gefunden.");
            return Results.Ok(await Liste(db, db.Patchungen.Where(p => p.PositionId == positionId), abbruch));
        });
        app.MapGet("/api/positionen/{positionId:int}/ports", Ports);
        app.MapPost("/api/positionen/{positionId:int}/patchungen", Anlegen);
        app.MapPut("/api/patchungen/{id:int}", Aendern);
        app.MapDelete("/api/patchungen/{id:int}", Loeschen);
    }

    /// <summary>Alle Geräte und Ports einer Position mit Belegung (Kabel oder Patchung) – zur Auswahl.</summary>
    private static async Task<IResult> Ports(int positionId, NetzDbContext db, CancellationToken abbruch)
    {
        if (!await db.Positionen.AnyAsync(p => p.Id == positionId, abbruch)) return Fehler.NichtGefunden("Position nicht gefunden.");
        var geraete = await db.Geraete.AsNoTracking().Where(g => g.PositionId == positionId).OrderBy(g => g.Bezeichnung)
            .Select(g => new
            {
                g.Id, g.Bezeichnung, g.Geraetetyp.Kategorie,
                Ports = g.Ports.OrderBy(p => p.Nummer).ThenBy(p => p.Seite).Select(p => new
                {
                    p.Id, p.Nummer, p.Seite, p.Bezeichnung, Portart = p.Portart.Name,
                    Kabel = p.Anschluss == null ? null : p.Anschluss.Medium.Kabelnummer,
                    Ende = p.Anschluss == null ? (Mediumende?)null : p.Anschluss.Ende,
                    AnschlussNummer = p.Anschluss == null ? (int?)null : p.Anschluss.Nummer,
                    Partner = p.Patchende == null ? null : p.Patchende.Patchung.Enden
                        .Where(x => x.PortId != p.Id)
                        .Select(x => new { Geraet = x.Port.Geraet.Bezeichnung, x.Port.Nummer, x.Port.Seite })
                        .FirstOrDefault()
                }).ToList()
            })
            .ToListAsync(abbruch);
        return Results.Ok(geraete.Select(g => new PositionsgeraetDto(g.Id, g.Bezeichnung, g.Kategorie.ToString(),
            g.Ports.Select(p => new PositionsportDto(p.Id, p.Nummer, p.Seite.ToString(), p.Bezeichnung, p.Portart,
                p.Kabel is not null ? $"{p.Kabel} {p.Ende}{p.AnschlussNummer}"
                : p.Partner is not null ? $"Patch ⇄ {p.Partner.Geraet} · {p.Partner.Nummer}{SeiteText(p.Partner.Seite)}"
                : null)).ToList())).ToList());
    }

    /// <summary>
    /// Use Case «Patchen»: Patchkabel zwischen zwei Ports derselben Position. Beide Ports müssen frei sein
    /// (weder an ein Medium angeschlossen noch schon gepatcht).
    /// </summary>
    private static async Task<IResult> Anlegen(int positionId, PatchungAnlegen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        if (!await db.Positionen.AnyAsync(p => p.Id == positionId, abbruch)) return Fehler.NichtGefunden("Position nicht gefunden.");
        if (eingabe.Port1Id == eingabe.Port2Id) return Fehler.Ungueltig("Bitte zwei verschiedene Ports wählen.");
        var bezeichnung = eingabe.Bezeichnung?.Trim();
        if (bezeichnung?.Length > 100) return Fehler.Ungueltig("Die Bezeichnung darf höchstens 100 Zeichen lang sein.");

        var ports = await db.Ports.Include(p => p.Geraet).Include(p => p.Anschluss).ThenInclude(a => a!.Medium)
            .Include(p => p.Patchende)
            .Where(p => p.Id == eingabe.Port1Id || p.Id == eingabe.Port2Id).ToListAsync(abbruch);
        if (ports.Count != 2) return Fehler.NichtGefunden("Port nicht gefunden.");
        foreach (var port in ports)
        {
            var name = $"Port {port.Nummer}{SeiteText(port.Seite)} an «{port.Geraet.Bezeichnung}»";
            if (port.Geraet.PositionId != positionId)
                return Fehler.Ungueltig($"{name} steht nicht an dieser Position. Patchungen verbinden nur Ports derselben Position.");
            if (port.Anschluss is { } a)
                return Fehler.Konflikt($"{name} ist bereits an ein Kabel angeschlossen ({a.Medium.Kabelnummer} {a.Ende}{a.Nummer}).");
            if (port.Patchende is not null) return Fehler.Konflikt($"{name} ist bereits gepatcht.");
        }

        var patchung = new Patchung
        {
            PositionId = positionId,
            Bezeichnung = string.IsNullOrEmpty(bezeichnung) ? null : bezeichnung,
            Enden = [new Patchende { Nummer = 1, PortId = eingabe.Port1Id }, new Patchende { Nummer = 2, PortId = eingabe.Port2Id }]
        };
        db.Patchungen.Add(patchung);
        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt("Ein Port wurde inzwischen von jemand anderem gepatcht. Bitte neu laden.");
        }
        var dto = (await Liste(db, db.Patchungen.Where(p => p.Id == patchung.Id), abbruch)).Single();
        return Results.Created($"/api/patchungen/{patchung.Id}", dto);
    }

    private static async Task<IResult> Aendern(int id, PatchungAendern eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var patchung = await db.Patchungen.FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (patchung is null) return Fehler.NichtGefunden("Patchung nicht gefunden.");
        var bezeichnung = eingabe.Bezeichnung?.Trim();
        if (bezeichnung?.Length > 100) return Fehler.Ungueltig("Die Bezeichnung darf höchstens 100 Zeichen lang sein.");
        db.Entry(patchung).Property(p => p.Version).OriginalValue = eingabe.Version;
        patchung.Bezeichnung = string.IsNullOrEmpty(bezeichnung) ? null : bezeichnung;
        db.Entry(patchung).Property(p => p.Bezeichnung).IsModified = true;
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return Geaendert(); }
        return Results.Ok((await Liste(db, db.Patchungen.Where(p => p.Id == id), abbruch)).Single());
    }

    private static async Task<IResult> Loeschen(int id, uint version, NetzDbContext db, CancellationToken abbruch)
    {
        var patchung = await db.Patchungen.FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (patchung is null) return Fehler.NichtGefunden("Patchung nicht gefunden.");
        db.Entry(patchung).Property(p => p.Version).OriginalValue = version;
        db.Patchungen.Remove(patchung); // Enden werden mitgelöscht, die Ports sind wieder frei
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return Geaendert(); }
        return Results.NoContent();
    }

    private static IResult Geaendert() =>
        Fehler.Konflikt("Die Patchung wurde inzwischen von jemand anderem geändert oder gelöscht. Bitte neu laden.");

    private static async Task<List<PatchungDto>> Liste(NetzDbContext db, IQueryable<Patchung> abfrage, CancellationToken abbruch)
    {
        var liste = await abfrage.AsNoTracking()
            .Select(p => new
            {
                p.Id, p.Version, p.PositionId, p.Bezeichnung,
                Enden = p.Enden.OrderBy(x => x.Nummer).Select(x => new PortKurzDto(
                    x.PortId, x.Port.GeraetId, x.Port.Geraet.Bezeichnung, x.Port.Nummer, x.Port.Seite.ToString(),
                    x.Port.Bezeichnung, x.Port.Portart.Name)).ToList()
            })
            .ToListAsync(abbruch);
        return liste
            .Select(p => new PatchungDto(p.Id, p.Version, p.PositionId, p.Bezeichnung, p.Enden[0], p.Enden[1]))
            .OrderBy(p => p.Port1.Geraet).ThenBy(p => p.Port1.Nummer).ToList();
    }

    public static string SeiteText(Portseite seite) => seite switch
    {
        Portseite.Vorne => " vorne",
        Portseite.Hinten => " hinten",
        _ => ""
    };
}
