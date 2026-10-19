using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

public record KommentarDto(int Id, uint Version, PositionKurzDto Position, string Text, string Verfasser, string Status,
    DateTimeOffset ErstelltAm);

public record KommentarAnlegen(string? Text, string? Verfasser);

/// <summary>Änderung: Text und/oder Status («Offen», «Erledigt»).</summary>
public record KommentarAendern(uint Version, string? Text, string? Status);

public static class KommentarEndpunkte
{
    public const int MaxText = 2000;

    public static void MapKommentarEndpunkte(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/positionen/{id:int}/kommentare", async (int id, NetzDbContext db, CancellationToken abbruch) =>
        {
            if (!await db.Positionen.AnyAsync(p => p.Id == id, abbruch)) return Fehler.NichtGefunden("Position nicht gefunden.");
            return Results.Ok(await Abfrage(db.Kommentare.Where(k => k.PositionId == id)).ToListAsync(abbruch));
        });

        // Übersicht im Büro: Kommentare eines Standorts, standardmässig nur offene
        app.MapGet("/api/standorte/{id:int}/kommentare", async (int id, string? status, NetzDbContext db, CancellationToken abbruch) =>
        {
            if (!await db.Standorte.AnyAsync(s => s.Id == id, abbruch)) return Fehler.NichtGefunden("Standort nicht gefunden.");
            var abfrage = db.Kommentare.Where(k => k.Position.StandortId == id);
            if (status != "alle")
            {
                if (!Enum.TryParse<Kommentarstatus>(status ?? "Offen", out var s) || !Enum.IsDefined(s))
                    return Fehler.Ungueltig("Unbekannter Status.");
                abfrage = abfrage.Where(k => k.Status == s);
            }
            return Results.Ok(await Abfrage(abfrage).ToListAsync(abbruch));
        });

        app.MapPost("/api/positionen/{id:int}/kommentare", Anlegen);
        app.MapPut("/api/kommentare/{id:int}", Aendern);
        app.MapDelete("/api/kommentare/{id:int}", Loeschen);
    }

    /// <summary>Use Case «Kommentar erfassen»: an einer Position, mit Verfasser (freies Feld), Status «offen».</summary>
    private static async Task<IResult> Anlegen(int id, KommentarAnlegen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        if (!await db.Positionen.AnyAsync(p => p.Id == id, abbruch)) return Fehler.NichtGefunden("Position nicht gefunden.");
        var text = eingabe.Text?.Trim() ?? "";
        var verfasser = eingabe.Verfasser?.Trim() ?? "";
        if (text.Length is 0 or > MaxText) return Fehler.Ungueltig($"Bitte einen Text (höchstens {MaxText} Zeichen) eingeben.");
        if (verfasser.Length is 0 or > 100) return Fehler.Ungueltig("Bitte Ihren Namen als Verfasser angeben (höchstens 100 Zeichen).");

        var kommentar = new Kommentar { PositionId = id, Text = text, Verfasser = verfasser, Status = Kommentarstatus.Offen };
        db.Kommentare.Add(kommentar);
        await db.SaveChangesAsync(abbruch);
        var dto = await Abfrage(db.Kommentare.Where(k => k.Id == kommentar.Id)).SingleAsync(abbruch);
        return Results.Created($"/api/kommentare/{kommentar.Id}", dto);
    }

    /// <summary>Use Case «Kommentare bearbeiten»: Text korrigieren, Status auf «erledigt» oder zurück auf «offen».</summary>
    private static async Task<IResult> Aendern(int id, KommentarAendern eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var kommentar = await db.Kommentare.FirstOrDefaultAsync(k => k.Id == id, abbruch);
        if (kommentar is null) return Fehler.NichtGefunden("Kommentar nicht gefunden.");
        if (eingabe.Text is not null)
        {
            var text = eingabe.Text.Trim();
            if (text.Length is 0 or > MaxText) return Fehler.Ungueltig($"Bitte einen Text (höchstens {MaxText} Zeichen) eingeben.");
            kommentar.Text = text;
        }
        if (eingabe.Status is not null)
        {
            if (!Enum.TryParse<Kommentarstatus>(eingabe.Status, out var s) || !Enum.IsDefined(s))
                return Fehler.Ungueltig("Unbekannter Status.");
            kommentar.Status = s;
        }
        db.Entry(kommentar).Property(k => k.Version).OriginalValue = eingabe.Version;
        db.Entry(kommentar).Property(k => k.Text).IsModified = true; // Versionsprüfung erzwingen
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return Geaendert(); }
        return Results.Ok(await Abfrage(db.Kommentare.Where(k => k.Id == id)).SingleAsync(abbruch));
    }

    private static async Task<IResult> Loeschen(int id, uint version, NetzDbContext db, CancellationToken abbruch)
    {
        var kommentar = await db.Kommentare.FirstOrDefaultAsync(k => k.Id == id, abbruch);
        if (kommentar is null) return Fehler.NichtGefunden("Kommentar nicht gefunden.");
        db.Entry(kommentar).Property(k => k.Version).OriginalValue = version;
        db.Kommentare.Remove(kommentar);
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return Geaendert(); }
        return Results.NoContent();
    }

    private static IResult Geaendert() =>
        Fehler.Konflikt("Der Kommentar wurde inzwischen von jemand anderem geändert oder gelöscht. Bitte neu laden.");

    /// <summary>Offene zuerst, dann neueste zuerst.</summary>
    private static IQueryable<KommentarDto> Abfrage(IQueryable<Kommentar> kommentare) =>
        kommentare.AsNoTracking()
            .OrderBy(k => k.Status == Kommentarstatus.Offen ? 0 : 1).ThenByDescending(k => k.ErstelltAm).ThenByDescending(k => k.Id)
            .Select(k => new KommentarDto(k.Id, k.Version,
                new PositionKurzDto(k.Position.Id, k.Position.Bezeichnung, k.Position.Positionstyp.Name, k.Position.Stockwerk),
                k.Text, k.Verfasser, k.Status.ToString(), k.ErstelltAm));
}
