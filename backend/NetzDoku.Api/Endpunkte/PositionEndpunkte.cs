using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Dienste;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

public record PositionstypDto(int Id, string Name);

public record PositionstypAnlegen(string? Name);

/// <summary>Position, wie sie in einem bestimmten Plan erscheint (u, v = Lage im Planbild).</summary>
public record PositionImPlanDto(
    int Id,
    uint Version,
    string Bezeichnung,
    int PositionstypId,
    string Positionstyp,
    int Stockwerk,
    double X,
    double Y,
    double U,
    double V,
    int AnzahlGeraete,
    int AnzahlVerbindungen);

/// <summary>Neue Position: Klickpunkt (u, v) in einem kalibrierten Plan.</summary>
public record PositionAnlegen(int PlanId, double U, double V, string? Bezeichnung, int PositionstypId, int? Stockwerk);

/// <summary>Änderung einer Position. Mit U/V wird sie verschoben (im Plan <see cref="PlanId"/>).</summary>
public record PositionAendern(
    uint Version, int PlanId, string? Bezeichnung, int PositionstypId, int Stockwerk, double? U, double? V);

public static class PositionEndpunkte
{
    public const int MinStockwerk = -10;
    public const int MaxStockwerk = 100;

    public static void MapPositionEndpunkte(this IEndpointRouteBuilder app)
    {
        // --- Positionstypen (Stammdaten) ---
        app.MapGet("/api/positionstypen", async (NetzDbContext db, CancellationToken abbruch) =>
            await db.Positionstypen.OrderBy(t => t.Name).Select(t => new PositionstypDto(t.Id, t.Name)).ToListAsync(abbruch));

        app.MapPost("/api/positionstypen", async (PositionstypAnlegen eingabe, NetzDbContext db, CancellationToken abbruch) =>
        {
            var name = eingabe.Name?.Trim() ?? "";
            if (name.Length is 0 or > 100) return Fehler.Ungueltig("Bitte einen Namen (höchstens 100 Zeichen) angeben.");
            var typ = new Positionstyp { Name = name };
            db.Positionstypen.Add(typ);
            try { await db.SaveChangesAsync(abbruch); }
            catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
            {
                return Fehler.Konflikt($"Den Positionstyp «{name}» gibt es bereits.");
            }
            return Results.Created($"/api/positionstypen/{typ.Id}", new PositionstypDto(typ.Id, typ.Name));
        });

        // --- Positionen ---
        app.MapGet("/api/plaene/{planId:int}/positionen", PositionenImPlan);
        app.MapPost("/api/standorte/{standortId:int}/positionen", PositionErfassen);
        app.MapPut("/api/positionen/{id:int}", PositionAendernAsync);
        app.MapDelete("/api/positionen/{id:int}", PositionLoeschen);
    }

    /// <summary>
    /// Use Case «Plan mit Positionen anzeigen»: alle Positionen, deren Koordinate im Planbereich liegt.
    /// Der Masterplan ist immer im Übersichtsmodus (alle Stockwerke); bei anderen Plänen nur das
    /// eigene Stockwerk, ausser <paramref name="uebersicht"/> ist gesetzt.
    /// </summary>
    private static async Task<IResult> PositionenImPlan(int planId, bool? uebersicht, NetzDbContext db, CancellationToken abbruch)
    {
        var plan = await db.Plaene.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan nicht gefunden.");
        if (!plan.Referenziert) return Fehler.Ungueltig("Der Plan ist noch nicht kalibriert.");

        var alleStockwerke = plan.IstMasterplan || uebersicht == true;
        var kandidaten = await AbfrageMitZaehlern(db, db.Positionen.Where(p => p.StandortId == plan.StandortId
                && (alleStockwerke || p.Stockwerk == plan.Stockwerk)))
            .ToListAsync(abbruch);

        var imPlan = kandidaten
            .Select(k => AlsDto(k, plan))
            .Where(d => d.U >= 0 && d.V >= 0 && d.U <= plan.BildBreite && d.V <= plan.BildHoehe)
            .OrderBy(d => d.Bezeichnung)
            .ToList();
        return Results.Ok(imPlan);
    }

    /// <summary>Use Case «Position erfassen»: Klickpunkt im Plan → absolute Koordinate.</summary>
    private static async Task<IResult> PositionErfassen(int standortId, PositionAnlegen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var plan = await db.Plaene.FirstOrDefaultAsync(p => p.Id == eingabe.PlanId && p.StandortId == standortId, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan an diesem Standort nicht gefunden.");
        if (!plan.Referenziert) return Fehler.Ungueltig("Der Plan ist noch nicht kalibriert. Bitte zuerst kalibrieren.");

        var stockwerk = eingabe.Stockwerk ?? (plan.IstMasterplan ? 0 : plan.Stockwerk);
        if (Pruefen(eingabe.Bezeichnung, stockwerk, plan, eingabe.U, eingabe.V) is { } fehler) return fehler;
        if (!await db.Positionstypen.AnyAsync(t => t.Id == eingabe.PositionstypId, abbruch))
            return Fehler.Ungueltig("Bitte einen Positionstyp wählen.");

        var welt = Geometrie.NachAbsolut(plan, new Planpunkt(eingabe.U, eingabe.V));
        var position = new Position
        {
            StandortId = standortId,
            PositionstypId = eingabe.PositionstypId,
            Bezeichnung = eingabe.Bezeichnung!.Trim(),
            X = welt.X,
            Y = welt.Y,
            Stockwerk = stockwerk
        };
        db.Positionen.Add(position);
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt($"Die Bezeichnung «{position.Bezeichnung}» gibt es an diesem Standort bereits.");
        }

        var dto = await EinzelnLaden(db, position.Id, plan, abbruch);
        return Results.Created($"/api/positionen/{position.Id}", dto);
    }

    /// <summary>Use Case «Position bearbeiten»: Bezeichnung, Typ, Stockwerk ändern, optional verschieben.</summary>
    private static async Task<IResult> PositionAendernAsync(int id, PositionAendern eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var position = await db.Positionen.FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (position is null) return Fehler.NichtGefunden("Position nicht gefunden.");
        var plan = await db.Plaene.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == eingabe.PlanId && p.StandortId == position.StandortId, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan an diesem Standort nicht gefunden.");

        var verschieben = eingabe.U is not null && eingabe.V is not null;
        if (verschieben && !plan.Referenziert) return Fehler.Ungueltig("Der Plan ist noch nicht kalibriert.");
        if (Pruefen(eingabe.Bezeichnung, eingabe.Stockwerk, plan, eingabe.U ?? 0, eingabe.V ?? 0, punktPruefen: verschieben) is { } fehler)
            return fehler;
        if (!await db.Positionstypen.AnyAsync(t => t.Id == eingabe.PositionstypId, abbruch))
            return Fehler.Ungueltig("Bitte einen Positionstyp wählen.");

        db.Entry(position).Property(p => p.Version).OriginalValue = eingabe.Version;
        position.Bezeichnung = eingabe.Bezeichnung!.Trim();
        position.PositionstypId = eingabe.PositionstypId;
        position.Stockwerk = eingabe.Stockwerk;
        if (verschieben)
        {
            var welt = Geometrie.NachAbsolut(plan, new Planpunkt(eingabe.U!.Value, eingabe.V!.Value));
            position.X = welt.X;
            position.Y = welt.Y;
        }
        // Immer schreiben, damit die Versionsprüfung greift (auch ohne geänderte Werte)
        db.Entry(position).Property(p => p.Bezeichnung).IsModified = true;

        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return Geaendert(); }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt($"Die Bezeichnung «{position.Bezeichnung}» gibt es an diesem Standort bereits.");
        }

        return Results.Ok(await EinzelnLaden(db, id, plan, abbruch));
    }

    /// <summary>Use Case «Position löschen»: nur ohne Geräte und Verbindungen (Regel 10).</summary>
    private static async Task<IResult> PositionLoeschen(int id, uint version, NetzDbContext db, CancellationToken abbruch)
    {
        var position = await db.Positionen.FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (position is null) return Fehler.NichtGefunden("Position nicht gefunden.");

        var geraete = await db.Geraete.CountAsync(g => g.PositionId == id, abbruch);
        var verbindungen = await db.Verbindungen.CountAsync(v => v.PositionAId == id || v.PositionBId == id, abbruch);
        if (geraete > 0 || verbindungen > 0)
            return Fehler.Konflikt(
                $"«{position.Bezeichnung}» kann nicht gelöscht werden: Es hängen noch {geraete} Gerät(e) und " +
                $"{verbindungen} Verbindung(en) daran. Bitte diese zuerst entfernen.");

        db.Entry(position).Property(p => p.Version).OriginalValue = version;
        db.Positionen.Remove(position);
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return Geaendert(); }
        return Results.NoContent();
    }

    // --- Hilfsfunktionen ---

    private static IResult Geaendert() => Fehler.Konflikt(
        "Die Position wurde inzwischen von jemand anderem geändert oder gelöscht. Bitte die Seite neu laden.");

    private static IResult? Pruefen(string? bezeichnung, int stockwerk, Plan plan, double u, double v, bool punktPruefen = true)
    {
        var text = bezeichnung?.Trim() ?? "";
        if (text.Length == 0) return Fehler.Ungueltig("Bitte eine Bezeichnung angeben.");
        if (text.Length > 100) return Fehler.Ungueltig("Die Bezeichnung darf höchstens 100 Zeichen lang sein.");
        if (stockwerk is < MinStockwerk or > MaxStockwerk)
            return Fehler.Ungueltig($"Das Stockwerk muss zwischen {MinStockwerk} und {MaxStockwerk} liegen.");
        if (punktPruefen && (u < 0 || v < 0 || u > plan.BildBreite || v > plan.BildHoehe))
            return Fehler.Ungueltig("Die Position liegt ausserhalb des Plans.");
        return null;
    }

    private record PositionMitZaehlern(Position Position, string Typ, int Geraete, int Verbindungen);

    private static IQueryable<PositionMitZaehlern> AbfrageMitZaehlern(NetzDbContext db, IQueryable<Position> positionen) =>
        positionen.Select(p => new PositionMitZaehlern(
            p,
            p.Positionstyp.Name,
            p.Geraete.Count,
            db.Verbindungen.Count(v => v.PositionAId == p.Id || v.PositionBId == p.Id)));

    private static async Task<PositionImPlanDto> EinzelnLaden(NetzDbContext db, int id, Plan plan, CancellationToken abbruch)
    {
        var k = await AbfrageMitZaehlern(db, db.Positionen.AsNoTracking().Where(p => p.Id == id)).SingleAsync(abbruch);
        return AlsDto(k, plan);
    }

    private static PositionImPlanDto AlsDto(PositionMitZaehlern k, Plan plan)
    {
        var p = k.Position;
        var imPlan = Geometrie.NachPlan(plan, new Weltpunkt(p.X, p.Y));
        return new PositionImPlanDto(p.Id, p.Version, p.Bezeichnung, p.PositionstypId, k.Typ, p.Stockwerk,
            p.X, p.Y, imPlan.U, imPlan.V, k.Geraete, k.Verbindungen);
    }
}
