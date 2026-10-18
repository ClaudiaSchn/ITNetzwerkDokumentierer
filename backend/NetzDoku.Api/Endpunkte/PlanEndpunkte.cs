using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Dienste;

namespace NetzDoku.Api.Endpunkte;

public static class PlanEndpunkte
{
    public static void MapPlanEndpunkte(this IEndpointRouteBuilder app)
    {
        var gruppe = app.MapGroup("/api/plaene");

        gruppe.MapGet("/{id:int}", async (int id, NetzDbContext db, CancellationToken abbruch) =>
        {
            var plan = await db.Plaene.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, abbruch);
            return plan is null ? Fehler.NichtGefunden("Plan nicht gefunden.") : Results.Ok(PlanDto.Aus(plan));
        });

        // Planbild (PNG). Dateinamen sind eindeutig und ändern sich nie → darf lange gecacht werden.
        gruppe.MapGet("/{id:int}/bild", async (int id, NetzDbContext db, Planablage ablage, HttpContext http,
            CancellationToken abbruch) =>
        {
            var datei = await db.Plaene.Where(p => p.Id == id).Select(p => p.BildDatei).FirstOrDefaultAsync(abbruch);
            if (datei is null || !File.Exists(ablage.VollerPfad(datei)))
                return Fehler.NichtGefunden("Planbild nicht gefunden.");
            http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(ablage.VollerPfad(datei), "image/png", enableRangeProcessing: true);
        });

        // Original-PDF zum Herunterladen
        gruppe.MapGet("/{id:int}/pdf", async (int id, NetzDbContext db, Planablage ablage, CancellationToken abbruch) =>
        {
            var plan = await db.Plaene.Where(p => p.Id == id)
                .Select(p => new { p.PdfDatei, p.Bezeichnung }).FirstOrDefaultAsync(abbruch);
            if (plan is null || !File.Exists(ablage.VollerPfad(plan.PdfDatei)))
                return Fehler.NichtGefunden("PDF nicht gefunden.");
            return Results.File(ablage.VollerPfad(plan.PdfDatei), "application/pdf", $"{plan.Bezeichnung}.pdf");
        });
    }
}
