using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Dienste;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

public static class StandortEndpunkte
{
    /// <summary>Höchstgrösse für hochgeladene PDFs.</summary>
    public const long MaxPdfGroesse = 100 * 1024 * 1024;

    public static void MapStandortEndpunkte(this IEndpointRouteBuilder app)
    {
        var gruppe = app.MapGroup("/api/standorte");

        gruppe.MapGet("/", async (NetzDbContext db, CancellationToken abbruch) =>
            await db.Standorte
                .OrderBy(s => s.Name)
                .Select(s => new StandortDto(s.Id, s.Name,
                    s.Plaene.Where(p => p.IstMasterplan && p.Aktiv)
                        .Select(p => new PlanKurzDto(p.Id, p.Bezeichnung, p.Referenziert))
                        .FirstOrDefault()))
                .ToListAsync(abbruch));

        gruppe.MapGet("/{id:int}", async (int id, NetzDbContext db, CancellationToken abbruch) =>
        {
            var standort = await db.Standorte
                .Where(s => s.Id == id)
                .Select(s => new StandortDto(s.Id, s.Name,
                    s.Plaene.Where(p => p.IstMasterplan && p.Aktiv)
                        .Select(p => new PlanKurzDto(p.Id, p.Bezeichnung, p.Referenziert))
                        .FirstOrDefault()))
                .FirstOrDefaultAsync(abbruch);
            return standort is null ? Fehler.NichtGefunden("Standort nicht gefunden.") : Results.Ok(standort);
        });

        gruppe.MapPost("/", async (StandortAnlegen eingabe, NetzDbContext db, CancellationToken abbruch) =>
        {
            var name = eingabe.Name?.Trim() ?? "";
            if (name.Length == 0) return Fehler.Ungueltig("Bitte einen Namen für den Standort angeben.");
            if (name.Length > 100) return Fehler.Ungueltig("Der Name darf höchstens 100 Zeichen lang sein.");

            var standort = new Standort { Name = name };
            db.Standorte.Add(standort);
            try
            {
                await db.SaveChangesAsync(abbruch);
            }
            catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
            {
                return Fehler.Konflikt($"Es gibt bereits einen Standort «{name}».");
            }
            return Results.Created($"/api/standorte/{standort.Id}", new StandortDto(standort.Id, standort.Name, null));
        });

        gruppe.MapPost("/{id:int}/masterplan", MasterplanImportieren)
            .DisableAntiforgery() // Schutz über den Pflicht-Header X-NetzDoku, siehe Program.cs
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxPdfGroesse));

        // Alle aktiven Pläne: Masterplan zuerst, dann nach Stockwerk und Bezeichnung
        gruppe.MapGet("/{id:int}/plaene", async (int id, NetzDbContext db, CancellationToken abbruch) =>
        {
            if (!await db.Standorte.AnyAsync(s => s.Id == id, abbruch)) return Fehler.NichtGefunden("Standort nicht gefunden.");
            var plaene = await db.Plaene.AsNoTracking()
                .Where(p => p.StandortId == id && p.Aktiv)
                .OrderByDescending(p => p.IstMasterplan).ThenBy(p => p.Stockwerk).ThenBy(p => p.Bezeichnung)
                .Select(p => new PlanListeDto(p.Id, p.Bezeichnung, p.Stockwerk, p.IstMasterplan, p.Methode.ToString(),
                    p.Referenziert, p.BezugsplanId))
                .ToListAsync(abbruch);
            return Results.Ok(plaene);
        });

        gruppe.MapPost("/{id:int}/plaene", PlanImportieren)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxPdfGroesse));
    }

    /// <summary>
    /// Use Case «Masterplan festlegen», Teil 1: PDF importieren und Planbild erzeugen.
    /// Nullpunkt und Massstab werden im nächsten Schritt bestimmt.
    /// </summary>
    private static async Task<IResult> MasterplanImportieren(
        int id, IFormFile? datei, [Microsoft.AspNetCore.Mvc.FromForm] string? bezeichnung,
        NetzDbContext db, Planablage ablage, ILogger<Planablage> log, CancellationToken abbruch)
    {
        var standort = await db.Standorte.FindAsync([id], abbruch);
        if (standort is null) return Fehler.NichtGefunden("Standort nicht gefunden.");

        if (await db.Plaene.AnyAsync(p => p.StandortId == id && p.IstMasterplan && p.Aktiv, abbruch))
            return Fehler.Konflikt($"Der Standort «{standort.Name}» hat bereits einen Masterplan.");

        return await Importieren(datei, db, ablage, log, abbruch,
            (pdfName, bildName, bild) => new Plan
            {
                Standort = standort,
                Bezeichnung = BezeichnungAus(bezeichnung, datei!.FileName, "Masterplan"),
                PdfDatei = pdfName,
                BildDatei = bildName,
                BildBreite = bild.Breite,
                BildHoehe = bild.Hoehe,
                Stockwerk = 0,
                IstMasterplan = true,
                Methode = Referenzierungsmethode.Master,
                Massstab = 1
            },
            $"Der Standort «{standort.Name}» hat bereits einen Masterplan.");
    }

    /// <summary>
    /// Use Case «Plan importieren»: weiterer Plan (Gebäude, Geschoss, Raum) mit Stockwerk und Bezugsplan.
    /// Die Passpunkte werden im nächsten Schritt gesetzt (PUT /api/plaene/{id}/referenzierung).
    /// </summary>
    private static async Task<IResult> PlanImportieren(
        int id, IFormFile? datei, [Microsoft.AspNetCore.Mvc.FromForm] string? bezeichnung,
        [Microsoft.AspNetCore.Mvc.FromForm] int? stockwerk, [Microsoft.AspNetCore.Mvc.FromForm] int? bezugsplanId,
        NetzDbContext db, Planablage ablage, ILogger<Planablage> log, CancellationToken abbruch)
    {
        var standort = await db.Standorte.FindAsync([id], abbruch);
        if (standort is null) return Fehler.NichtGefunden("Standort nicht gefunden.");

        var stock = stockwerk ?? 0;
        if (stock is < PositionEndpunkte.MinStockwerk or > PositionEndpunkte.MaxStockwerk)
            return Fehler.Ungueltig($"Das Stockwerk muss zwischen {PositionEndpunkte.MinStockwerk} und {PositionEndpunkte.MaxStockwerk} liegen.");

        // Vorgabe: Masterplan als Bezugsplan
        var bezug = bezugsplanId is null
            ? await db.Plaene.FirstOrDefaultAsync(p => p.StandortId == id && p.IstMasterplan && p.Aktiv, abbruch)
            : await db.Plaene.FirstOrDefaultAsync(p => p.Id == bezugsplanId && p.StandortId == id && p.Aktiv, abbruch);
        if (bezug is null)
            return Fehler.Ungueltig(bezugsplanId is null
                ? "Bitte zuerst den Masterplan importieren."
                : "Der Bezugsplan gehört nicht zu diesem Standort.");
        if (!bezug.Referenziert)
            return Fehler.Ungueltig($"Der Bezugsplan «{bezug.Bezeichnung}» ist noch nicht referenziert.");

        var name = BezeichnungAus(bezeichnung, datei?.FileName ?? "", "Plan");
        if (await db.Plaene.AnyAsync(p => p.StandortId == id && p.Aktiv && p.Bezeichnung == name, abbruch))
            return Fehler.Konflikt($"Es gibt bereits einen Plan «{name}».");

        return await Importieren(datei, db, ablage, log, abbruch,
            (pdfName, bildName, bild) => new Plan
            {
                Standort = standort,
                Bezeichnung = name,
                PdfDatei = pdfName,
                BildDatei = bildName,
                BildBreite = bild.Breite,
                BildHoehe = bild.Hoehe,
                Stockwerk = stock,
                IstMasterplan = false,
                Methode = Referenzierungsmethode.Passpunkte,
                BezugsplanId = bezug.Id,
                Massstab = 1
            },
            $"Es gibt bereits einen Plan «{name}».");
    }

    /// <summary>PDF prüfen, in die Ablage übernehmen, Planbild erzeugen und den Plan speichern.</summary>
    private static async Task<IResult> Importieren(
        IFormFile? datei, NetzDbContext db, Planablage ablage, ILogger<Planablage> log, CancellationToken abbruch,
        Func<string, string, Planbild, Plan> neuerPlan, string meldungDoppelt)
    {
        if (datei is null || datei.Length == 0) return Fehler.Ungueltig("Bitte eine PDF-Datei auswählen.");
        if (datei.Length > MaxPdfGroesse) return Fehler.Ungueltig("Die Datei ist grösser als 100 MB.");

        var pdfName = ablage.NeuerDateiname(".pdf");
        var bildName = ablage.NeuerDateiname(".png");
        try
        {
            // PDF in die Ablage kopieren und von dort lesen
            await using (var ziel = File.Create(ablage.VollerPfad(pdfName)))
            {
                await datei.CopyToAsync(ziel, abbruch);
            }

            Planbild bild;
            await using (var pdf = File.OpenRead(ablage.VollerPfad(pdfName)))
            {
                if (!PdfUmwandlung.IstPdf(pdf)) throw new PdfFehler("Die Datei ist kein PDF.");
                var seiten = PdfUmwandlung.AnzahlSeiten(pdf);
                if (seiten != 1)
                    throw new PdfFehler($"Das PDF hat {seiten} Seiten. Erlaubt ist nur ein einseitiges PDF.");
                bild = PdfUmwandlung.AlsPngSpeichern(pdf, ablage.VollerPfad(bildName));
            }

            var plan = neuerPlan(pdfName, bildName, bild);
            db.Plaene.Add(plan);
            await db.SaveChangesAsync(abbruch);

            log.LogInformation("Plan {Plan} «{Bezeichnung}» importiert ({Breite}×{Hoehe} px)",
                plan.Id, plan.Bezeichnung, bild.Breite, bild.Hoehe);
            return Results.Created($"/api/plaene/{plan.Id}", PlanDto.Aus(plan));
        }
        catch (PdfFehler e)
        {
            Aufraeumen();
            return Fehler.Ungueltig(e.Message);
        }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            Aufraeumen();
            return Fehler.Konflikt(meldungDoppelt);
        }
        catch
        {
            Aufraeumen();
            throw;
        }

        void Aufraeumen()
        {
            ablage.Loeschen(pdfName);
            ablage.Loeschen(bildName);
        }
    }

    private static string BezeichnungAus(string? bezeichnung, string dateiname, string ersatz)
    {
        var text = string.IsNullOrWhiteSpace(bezeichnung)
            ? Path.GetFileNameWithoutExtension(dateiname)
            : bezeichnung.Trim();
        if (string.IsNullOrWhiteSpace(text)) text = ersatz;
        return text.Length > 100 ? text[..100] : text;
    }
}
