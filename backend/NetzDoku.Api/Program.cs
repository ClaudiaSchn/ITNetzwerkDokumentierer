using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Dienste;
using NetzDoku.Api.Endpunkte;

var builder = WebApplication.CreateBuilder(args);

var verbindung = builder.Configuration.GetConnectionString("NetzDoku")
    ?? throw new InvalidOperationException(
        "Connection String 'NetzDoku' fehlt (Umgebungsvariable ConnectionStrings__NetzDoku).");

builder.Services.AddDbContext<NetzDbContext>(optionen => optionen
    .UseNpgsql(verbindung)
    .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton<Planablage>();
builder.Services.AddProblemDetails();

// Uploads bis 100 MB (Pläne können gross sein)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = StandortEndpunkte.MaxPdfGroesse);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Schutz gegen Cross-Site-Request-Forgery: Ändernde API-Aufrufe müssen den Header
// X-NetzDoku tragen. Ein fremde Webseite kann ihn nicht setzen, ohne dass der Browser
// vorher nachfragt (CORS) – und das erlauben wir nicht.
app.Use(async (kontext, weiter) =>
{
    var anfrage = kontext.Request;
    var aendernd = !HttpMethods.IsGet(anfrage.Method) && !HttpMethods.IsHead(anfrage.Method)
                   && !HttpMethods.IsOptions(anfrage.Method);
    if (aendernd && anfrage.Path.StartsWithSegments("/api") && !anfrage.Headers.ContainsKey("X-NetzDoku"))
    {
        await Results.Problem(title: "Anfrage abgelehnt (Header X-NetzDoku fehlt).",
            statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(kontext);
        return;
    }
    await weiter(kontext);
});

// Datenbank beim Start auf den neuesten Stand bringen und Stammdaten anlegen.
using (var bereich = app.Services.CreateScope())
{
    var db = bereich.ServiceProvider.GetRequiredService<NetzDbContext>();
    await db.Database.MigrateAsync();
    await Startwerte.AnlegenAsync(db);
}

// Statusabfrage: zeigt, ob Backend und Datenbank erreichbar sind.
app.MapGet("/api/health", async (NetzDbContext db, CancellationToken abbruch) =>
{
    var datenbankErreichbar = await db.Database.CanConnectAsync(abbruch);
    return Results.Ok(new
    {
        backend = "läuft",
        datenbank = datenbankErreichbar ? "verbunden" : "nicht erreichbar",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unbekannt",
        zeit = DateTimeOffset.Now
    });
});

app.MapStandortEndpunkte();
app.MapPlanEndpunkte();
app.MapPositionEndpunkte();
app.MapVerbindungEndpunkte();
app.MapMedienEndpunkte();
app.MapGeraetEndpunkte();
app.MapAnschlussEndpunkte();
app.MapPatchungEndpunkte();
app.MapWegEndpunkte();
app.MapKommentarEndpunkte();

app.Run();
