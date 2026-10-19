using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

/// <summary>Eine Position, kurz – als Ende einer Verbindung.</summary>
public record PositionKurzDto(int Id, string Bezeichnung, string Positionstyp, int Stockwerk);

/// <summary>
/// Verbindung zwischen zwei Positionen. <see cref="LaengeMeter"/> ist die Luftlinie in der Ebene
/// (ohne Stockwerke und ohne Leitungsverlauf) – nur als Anhaltspunkt.
/// </summary>
public record VerbindungDto(int Id, uint Version, PositionKurzDto A, PositionKurzDto B, double LaengeMeter, int AnzahlMedien);

/// <summary>Neue Verbindung: A = zuerst gewählte Position (Ende A der Medien).</summary>
public record VerbindungAnlegen(int PositionAId, int PositionBId);

public static class VerbindungEndpunkte
{
    public static void MapVerbindungEndpunkte(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/standorte/{standortId:int}/verbindungen", async (int standortId, NetzDbContext db, CancellationToken abbruch) =>
        {
            if (!await db.Standorte.AnyAsync(s => s.Id == standortId, abbruch)) return Fehler.NichtGefunden("Standort nicht gefunden.");
            var liste = await Abfrage(db.Verbindungen.AsNoTracking().Where(v => v.StandortId == standortId)).ToListAsync(abbruch);
            return Results.Ok(liste.Select(AlsDto).OrderBy(v => v.A.Bezeichnung).ThenBy(v => v.B.Bezeichnung));
        });

        app.MapPost("/api/standorte/{standortId:int}/verbindungen", Anlegen);
        app.MapDelete("/api/verbindungen/{id:int}", Loeschen);
    }

    /// <summary>
    /// Use Case «Verbindung anlegen»: zwei verschiedene Positionen am selben Standort. Pro Paar höchstens
    /// eine Verbindung (A–B = B–A); mehrere Kabel laufen als Medien über dieselbe Verbindung.
    /// </summary>
    private static async Task<IResult> Anlegen(int standortId, VerbindungAnlegen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        if (!await db.Standorte.AnyAsync(s => s.Id == standortId, abbruch)) return Fehler.NichtGefunden("Standort nicht gefunden.");
        if (eingabe.PositionAId == eingabe.PositionBId)
            return Fehler.Ungueltig("Eine Verbindung braucht zwei verschiedene Positionen.");

        var positionen = await db.Positionen.AsNoTracking()
            .Where(p => p.Id == eingabe.PositionAId || p.Id == eingabe.PositionBId)
            .Select(p => new { p.Id, p.StandortId, p.Bezeichnung })
            .ToListAsync(abbruch);
        if (positionen.Count != 2) return Fehler.NichtGefunden("Position nicht gefunden.");
        if (positionen.Any(p => p.StandortId != standortId))
            return Fehler.Ungueltig("Beide Positionen müssen zu diesem Standort gehören.");

        var a = positionen.Single(p => p.Id == eingabe.PositionAId).Bezeichnung;
        var b = positionen.Single(p => p.Id == eingabe.PositionBId).Bezeichnung;
        var meldungDoppelt = $"Zwischen «{a}» und «{b}» besteht bereits eine Verbindung.";
        if (await db.Verbindungen.AnyAsync(v =>
                (v.PositionAId == eingabe.PositionAId && v.PositionBId == eingabe.PositionBId)
                || (v.PositionAId == eingabe.PositionBId && v.PositionBId == eingabe.PositionAId), abbruch))
            return Fehler.Konflikt(meldungDoppelt);

        var verbindung = new Verbindung
        {
            StandortId = standortId,
            PositionAId = eingabe.PositionAId,
            PositionBId = eingabe.PositionBId
        };
        db.Verbindungen.Add(verbindung);
        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            // Gleichzeitig von jemand anderem angelegt (Index ux_verbindungen_positionspaar)
            return Fehler.Konflikt(meldungDoppelt);
        }

        var dto = AlsDto(await Abfrage(db.Verbindungen.AsNoTracking().Where(v => v.Id == verbindung.Id)).SingleAsync(abbruch));
        return Results.Created($"/api/verbindungen/{verbindung.Id}", dto);
    }

    /// <summary>Use Case «Verbindung löschen»: gesperrt, solange Medien darüber laufen.</summary>
    private static async Task<IResult> Loeschen(int id, uint version, NetzDbContext db, CancellationToken abbruch)
    {
        var verbindung = await db.Verbindungen.FirstOrDefaultAsync(v => v.Id == id, abbruch);
        if (verbindung is null) return Fehler.NichtGefunden("Verbindung nicht gefunden.");

        var medien = await db.Medien.CountAsync(m => m.VerbindungId == id, abbruch);
        if (medien > 0)
            return Fehler.Konflikt(medien == 1
                ? "Über diese Verbindung läuft noch ein Medium. Bitte dieses zuerst entfernen."
                : $"Über diese Verbindung laufen noch {medien} Medien. Bitte diese zuerst entfernen.");

        db.Entry(verbindung).Property(v => v.Version).OriginalValue = version;
        db.Verbindungen.Remove(verbindung);
        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Konflikt("Die Verbindung wurde inzwischen von jemand anderem geändert oder gelöscht. Bitte die Seite neu laden.");
        }
        return Results.NoContent();
    }

    private record Zeile(Verbindung V, Position A, string TypA, Position B, string TypB, int Medien);

    private static IQueryable<Zeile> Abfrage(IQueryable<Verbindung> verbindungen) =>
        verbindungen.Select(v => new Zeile(v, v.PositionA, v.PositionA.Positionstyp.Name, v.PositionB,
            v.PositionB.Positionstyp.Name, v.Medien.Count));

    private static VerbindungDto AlsDto(Zeile z) => new(
        z.V.Id, z.V.Version,
        new PositionKurzDto(z.A.Id, z.A.Bezeichnung, z.TypA, z.A.Stockwerk),
        new PositionKurzDto(z.B.Id, z.B.Bezeichnung, z.TypB, z.B.Stockwerk),
        Math.Sqrt(Math.Pow(z.B.X - z.A.X, 2) + Math.Pow(z.B.Y - z.A.Y, 2)),
        z.Medien);
}
