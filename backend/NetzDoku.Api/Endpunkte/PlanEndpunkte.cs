using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Dienste;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

public static class PlanEndpunkte
{
    /// <summary>Kürzeste erlaubte Messstrecke in Pixeln; kürzere sind zu ungenau.</summary>
    public const double MinStreckePixel = 20;

    public static void MapPlanEndpunkte(this IEndpointRouteBuilder app)
    {
        var gruppe = app.MapGroup("/api/plaene");

        gruppe.MapGet("/{id:int}", async (int id, NetzDbContext db, CancellationToken abbruch) =>
        {
            var plan = await db.Plaene.AsNoTracking().Include(p => p.Messstrecken)
                .FirstOrDefaultAsync(p => p.Id == id, abbruch);
            return plan is null ? Fehler.NichtGefunden("Plan nicht gefunden.") : Results.Ok(PlanDto.Aus(plan));
        });

        gruppe.MapPut("/{id:int}/kalibrierung", MasterKalibrieren);

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

    /// <summary>
    /// Use Case «Masterplan festlegen», Teil 2: Nullpunkt und Massstab über zwei bekannte Strecken.
    /// Kann wiederholt werden; die bisherigen Messstrecken werden ersetzt.
    /// </summary>
    private static async Task<IResult> MasterKalibrieren(
        int id, MasterKalibrierungEingabe eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var plan = await db.Plaene.Include(p => p.Messstrecken).FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan nicht gefunden.");
        if (!plan.IstMasterplan)
            return Fehler.Ungueltig("Nur der Masterplan wird über Nullpunkt und Strecken kalibriert.");

        if (eingabe.Nullpunkt is null) return Fehler.Ungueltig("Bitte den Nullpunkt setzen.");
        if (eingabe.Strecken is not { Count: 2 }) return Fehler.Ungueltig("Bitte zwei bekannte Strecken angeben.");

        bool ImPlan(double u, double v) => u >= 0 && v >= 0 && u <= plan.BildBreite && v <= plan.BildHoehe;
        if (!ImPlan(eingabe.Nullpunkt.U, eingabe.Nullpunkt.V)) return Fehler.Ungueltig("Der Nullpunkt liegt ausserhalb des Plans.");

        var strecken = new List<BekannteStrecke>();
        for (var i = 0; i < eingabe.Strecken.Count; i++)
        {
            var s = eingabe.Strecken[i];
            var nr = i + 1;
            if (!ImPlan(s.U1, s.V1) || !ImPlan(s.U2, s.V2))
                return Fehler.Ungueltig($"Strecke {nr} liegt nicht vollständig im Plan.");
            if (!(s.LaengeMeter > 0 && s.LaengeMeter <= 100_000))
                return Fehler.Ungueltig($"Bitte für Strecke {nr} eine Länge zwischen 0 und 100 000 m angeben.");
            var strecke = new BekannteStrecke(new Planpunkt(s.U1, s.V1), new Planpunkt(s.U2, s.V2), s.LaengeMeter);
            if (strecke.PixelLaenge < MinStreckePixel)
                return Fehler.Ungueltig($"Strecke {nr} ist zu kurz. Bitte eine möglichst lange bekannte Strecke wählen.");
            strecken.Add(strecke);
        }

        var ergebnis = Geometrie.KalibriereMaster(new Planpunkt(eingabe.Nullpunkt.U, eingabe.Nullpunkt.V), strecken);

        // Optimistisches Sperren: mit der Version vergleichen, die der Browser geladen hatte
        db.Entry(plan).Property(p => p.Version).OriginalValue = eingabe.Version;

        plan.Massstab = ergebnis.Massstab;
        plan.Drehung = 0;
        plan.VersatzX = ergebnis.VersatzX;
        plan.VersatzY = ergebnis.VersatzY;
        plan.Referenziert = true;

        // Auch wenn sich die Werte nicht ändern (z.B. nur Messstrecken neu), muss der Plan
        // geschrieben werden – sonst prüft die Datenbank die Version nicht.
        db.Entry(plan).Property(p => p.Massstab).IsModified = true;

        db.Messstrecken.RemoveRange(plan.Messstrecken);
        plan.Messstrecken = strecken.Select((s, i) => new Messstrecke
        {
            Nummer = i + 1,
            U1 = s.Anfang.U, V1 = s.Anfang.V, U2 = s.Ende.U, V2 = s.Ende.V,
            LaengeMeter = s.LaengeMeter
        }).ToList();

        try
        {
            // SaveChanges speichert Plan und Messstrecken gemeinsam in einer Transaktion
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Geaendert();
        }

        // Neue Version (xmin) aus der Datenbank holen
        await db.Entry(plan).ReloadAsync(abbruch);
        return Results.Ok(PlanDto.Aus(plan));
    }
}
