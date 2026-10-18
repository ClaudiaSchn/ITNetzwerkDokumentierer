using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;

var builder = WebApplication.CreateBuilder(args);

var verbindung = builder.Configuration.GetConnectionString("NetzDoku")
    ?? throw new InvalidOperationException(
        "Connection String 'NetzDoku' fehlt (Umgebungsvariable ConnectionStrings__NetzDoku).");

builder.Services.AddDbContext<NetzDbContext>(optionen => optionen.UseNpgsql(verbindung));

var app = builder.Build();

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

app.Run();
