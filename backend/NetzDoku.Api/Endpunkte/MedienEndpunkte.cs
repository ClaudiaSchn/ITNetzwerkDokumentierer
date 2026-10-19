using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

public record PortartDto(int Id, string Name);

public record MedientypDto(int Id, string Name, IReadOnlyList<PortartDto> Portarten);

public record PortartAnlegen(string? Name);

public record MedientypAnlegen(string? Name, List<int>? PortartIds);

/// <summary>
/// Medium (Kabel, Funkstrecke) auf einer Verbindung. <see cref="BelegtA"/>/<see cref="BelegtB"/>:
/// wie viele Anschlüsse am Ende A bzw. B an einen Port angeschlossen sind.
/// </summary>
public record MediumDto(
    int Id, uint Version, int VerbindungId, string Kabelnummer, int MedientypId, string Medientyp,
    int AnzahlAnschluesse, int BelegtA, int BelegtB);

public record MediumDaten(string? Kabelnummer, int MedientypId, int AnzahlAnschluesse);

public record MediumAendern(uint Version, string? Kabelnummer, int MedientypId, int AnzahlAnschluesse);

public static class MedienEndpunkte
{
    /// <summary>Höchstzahl Anschlüsse pro Ende (z.B. 288 Fasern).</summary>
    public const int MaxAnschluesse = 288;

    public static void MapMedienEndpunkte(this IEndpointRouteBuilder app)
    {
        // --- Stammdaten: Portarten und Medientypen ---
        app.MapGet("/api/portarten", async (NetzDbContext db, CancellationToken abbruch) =>
            await db.Portarten.OrderBy(p => p.Name).Select(p => new PortartDto(p.Id, p.Name)).ToListAsync(abbruch));

        app.MapPost("/api/portarten", async (PortartAnlegen eingabe, NetzDbContext db, CancellationToken abbruch) =>
        {
            var name = eingabe.Name?.Trim() ?? "";
            if (name.Length is 0 or > 100) return Fehler.Ungueltig("Bitte einen Namen (höchstens 100 Zeichen) angeben.");
            var portart = new Portart { Name = name };
            db.Portarten.Add(portart);
            try { await db.SaveChangesAsync(abbruch); }
            catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
            {
                return Fehler.Konflikt($"Die Portart «{name}» gibt es bereits.");
            }
            return Results.Created($"/api/portarten/{portart.Id}", new PortartDto(portart.Id, portart.Name));
        });

        app.MapGet("/api/medientypen", async (NetzDbContext db, CancellationToken abbruch) =>
        {
            var typen = await db.Medientypen.AsNoTracking().Include(t => t.ErlaubtePortarten)
                .OrderBy(t => t.Name).ToListAsync(abbruch);
            return typen.Select(MedientypAlsDto);
        });

        app.MapPost("/api/medientypen", async (MedientypAnlegen eingabe, NetzDbContext db, CancellationToken abbruch) =>
        {
            var name = eingabe.Name?.Trim() ?? "";
            if (name.Length is 0 or > 100) return Fehler.Ungueltig("Bitte einen Namen (höchstens 100 Zeichen) angeben.");
            var ids = eingabe.PortartIds?.Distinct().ToList() ?? [];
            if (ids.Count == 0) return Fehler.Ungueltig("Bitte mindestens eine erlaubte Portart wählen.");
            var portarten = await db.Portarten.Where(p => ids.Contains(p.Id)).ToListAsync(abbruch);
            if (portarten.Count != ids.Count) return Fehler.Ungueltig("Unbekannte Portart.");

            var typ = new Medientyp { Name = name, ErlaubtePortarten = portarten };
            db.Medientypen.Add(typ);
            try { await db.SaveChangesAsync(abbruch); }
            catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
            {
                return Fehler.Konflikt($"Den Medientyp «{name}» gibt es bereits.");
            }
            return Results.Created($"/api/medientypen/{typ.Id}", MedientypAlsDto(typ));
        });

        // --- Medien ---
        app.MapGet("/api/verbindungen/{verbindungId:int}/medien", async (int verbindungId, NetzDbContext db, CancellationToken abbruch) =>
        {
            if (!await db.Verbindungen.AnyAsync(v => v.Id == verbindungId, abbruch))
                return Fehler.NichtGefunden("Verbindung nicht gefunden.");
            var medien = await Abfrage(db.Medien.AsNoTracking().Where(m => m.VerbindungId == verbindungId)
                .OrderBy(m => m.Kabelnummer)).ToListAsync(abbruch);
            return Results.Ok(medien);
        });

        app.MapPost("/api/verbindungen/{verbindungId:int}/medien", Zuweisen);
        app.MapPut("/api/medien/{id:int}", Aendern);
        app.MapDelete("/api/medien/{id:int}", Entfernen);
    }

    /// <summary>
    /// Use Case «Medium zuweisen»: Medientyp, Kabelnummer (eindeutig pro Standort) und Anzahl Anschlüsse
    /// pro Ende. Die 2 × n Anschlüsse werden gleich mit angelegt (Regel 6).
    /// </summary>
    private static async Task<IResult> Zuweisen(int verbindungId, MediumDaten eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var verbindung = await db.Verbindungen.AsNoTracking().FirstOrDefaultAsync(v => v.Id == verbindungId, abbruch);
        if (verbindung is null) return Fehler.NichtGefunden("Verbindung nicht gefunden.");
        if (await Pruefen(db, eingabe.Kabelnummer, eingabe.MedientypId, eingabe.AnzahlAnschluesse, abbruch) is { } fehler)
            return fehler;

        var kabelnummer = eingabe.Kabelnummer!.Trim();
        var medium = new Medium
        {
            VerbindungId = verbindungId,
            StandortId = verbindung.StandortId,
            MedientypId = eingabe.MedientypId,
            Kabelnummer = kabelnummer,
            AnzahlAnschluesse = eingabe.AnzahlAnschluesse
        };
        AnschluesseErgaenzen(medium, 1, eingabe.AnzahlAnschluesse);
        db.Medien.Add(medium);
        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt(KabelnummerVergeben(kabelnummer));
        }
        return Results.Created($"/api/medien/{medium.Id}", await EinzelnLaden(db, medium.Id, abbruch));
    }

    /// <summary>
    /// Medium ändern. Kabelnummer jederzeit; Medientyp nur, solange kein Anschluss belegt ist;
    /// weniger Anschlüsse nur, wenn die wegfallenden frei sind.
    /// </summary>
    private static async Task<IResult> Aendern(int id, MediumAendern eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var medium = await db.Medien.Include(m => m.Anschluesse).FirstOrDefaultAsync(m => m.Id == id, abbruch);
        if (medium is null) return Fehler.NichtGefunden("Medium nicht gefunden.");
        if (await Pruefen(db, eingabe.Kabelnummer, eingabe.MedientypId, eingabe.AnzahlAnschluesse, abbruch) is { } fehler)
            return fehler;

        var belegt = medium.Anschluesse.Where(a => a.PortId != null).ToList();
        if (eingabe.MedientypId != medium.MedientypId && belegt.Count > 0)
            return Fehler.Konflikt(belegt.Count == 1
                ? "Der Medientyp lässt sich nicht ändern, solange ein Anschluss belegt ist."
                : $"Der Medientyp lässt sich nicht ändern, solange {belegt.Count} Anschlüsse belegt sind.");
        if (eingabe.AnzahlAnschluesse < medium.AnzahlAnschluesse && belegt.Any(a => a.Nummer > eingabe.AnzahlAnschluesse))
            return Fehler.Konflikt("Die Anzahl lässt sich nicht verkleinern: Wegfallende Anschlüsse sind noch belegt.");

        db.Entry(medium).Property(m => m.Version).OriginalValue = eingabe.Version;
        var kabelnummer = eingabe.Kabelnummer!.Trim();
        medium.Kabelnummer = kabelnummer;
        medium.MedientypId = eingabe.MedientypId;
        if (eingabe.AnzahlAnschluesse > medium.AnzahlAnschluesse)
            AnschluesseErgaenzen(medium, medium.AnzahlAnschluesse + 1, eingabe.AnzahlAnschluesse);
        else if (eingabe.AnzahlAnschluesse < medium.AnzahlAnschluesse)
            db.Anschluesse.RemoveRange(medium.Anschluesse.Where(a => a.Nummer > eingabe.AnzahlAnschluesse));
        medium.AnzahlAnschluesse = eingabe.AnzahlAnschluesse;
        db.Entry(medium).Property(m => m.Kabelnummer).IsModified = true; // Versionsprüfung erzwingen

        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Konflikt("Das Medium wurde inzwischen von jemand anderem geändert. Bitte neu laden und die Eingabe wiederholen.");
        }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt(KabelnummerVergeben(kabelnummer));
        }
        return Results.Ok(await EinzelnLaden(db, medium.Id, abbruch));
    }

    /// <summary>Use Case «Medium entfernen»: nur, solange kein Anschluss an einem Port hängt (Regel 10).</summary>
    private static async Task<IResult> Entfernen(int id, uint version, NetzDbContext db, CancellationToken abbruch)
    {
        var medium = await db.Medien.FirstOrDefaultAsync(m => m.Id == id, abbruch);
        if (medium is null) return Fehler.NichtGefunden("Medium nicht gefunden.");
        var belegt = await db.Anschluesse.CountAsync(a => a.MediumId == id && a.PortId != null, abbruch);
        if (belegt > 0)
            return Fehler.Konflikt(belegt == 1
                ? $"«{medium.Kabelnummer}» ist noch an einen Port angeschlossen. Bitte zuerst den Anschluss lösen."
                : $"«{medium.Kabelnummer}» ist noch an {belegt} Ports angeschlossen. Bitte zuerst die Anschlüsse lösen.");

        db.Entry(medium).Property(m => m.Version).OriginalValue = version;
        db.Medien.Remove(medium); // Anschlüsse werden mitgelöscht
        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Konflikt("Das Medium wurde inzwischen von jemand anderem geändert. Bitte neu laden.");
        }
        return Results.NoContent();
    }

    private static async Task<IResult?> Pruefen(NetzDbContext db, string? kabelnummer, int medientypId, int anzahl, CancellationToken abbruch)
    {
        var nummer = kabelnummer?.Trim() ?? "";
        if (nummer.Length is 0 or > 50) return Fehler.Ungueltig("Bitte eine Kabelnummer (höchstens 50 Zeichen) angeben.");
        if (anzahl is < 1 or > MaxAnschluesse)
            return Fehler.Ungueltig($"Die Anzahl Anschlüsse pro Ende muss zwischen 1 und {MaxAnschluesse} liegen.");
        if (!await db.Medientypen.AnyAsync(t => t.Id == medientypId, abbruch)) return Fehler.Ungueltig("Bitte einen Medientyp wählen.");
        return null;
    }

    private static string KabelnummerVergeben(string kabelnummer) =>
        $"Die Kabelnummer «{kabelnummer}» ist an diesem Standort bereits vergeben.";

    private static void AnschluesseErgaenzen(Medium medium, int von, int bis)
    {
        for (var nummer = von; nummer <= bis; nummer++)
        {
            medium.Anschluesse.Add(new Anschluss { Ende = Mediumende.A, Nummer = nummer });
            medium.Anschluesse.Add(new Anschluss { Ende = Mediumende.B, Nummer = nummer });
        }
    }

    private static IQueryable<MediumDto> Abfrage(IQueryable<Medium> medien) =>
        medien.Select(m => new MediumDto(
            m.Id, m.Version, m.VerbindungId, m.Kabelnummer, m.MedientypId, m.Medientyp.Name, m.AnzahlAnschluesse,
            m.Anschluesse.Count(a => a.Ende == Mediumende.A && a.PortId != null),
            m.Anschluesse.Count(a => a.Ende == Mediumende.B && a.PortId != null)));

    private static Task<MediumDto> EinzelnLaden(NetzDbContext db, int id, CancellationToken abbruch) =>
        Abfrage(db.Medien.AsNoTracking().Where(m => m.Id == id)).SingleAsync(abbruch);

    private static MedientypDto MedientypAlsDto(Medientyp t) =>
        new(t.Id, t.Name, t.ErlaubtePortarten.OrderBy(p => p.Name).Select(p => new PortartDto(p.Id, p.Name)).ToList());
}
