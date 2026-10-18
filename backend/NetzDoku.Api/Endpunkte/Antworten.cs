using Microsoft.EntityFrameworkCore;
using Npgsql;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

// Datenübertragungsobjekte (was die API nach aussen gibt)

public record StandortDto(int Id, string Name, PlanKurzDto? Masterplan);

public record PlanKurzDto(int Id, string Bezeichnung);

public record PlanDto(
    int Id,
    int StandortId,
    string Bezeichnung,
    int Stockwerk,
    bool IstMasterplan,
    string Methode,
    int BildBreite,
    int BildHoehe,
    string BildUrl,
    string PdfUrl)
{
    public static PlanDto Aus(Plan p) => new(
        p.Id, p.StandortId, p.Bezeichnung, p.Stockwerk, p.IstMasterplan, p.Methode.ToString(),
        p.BildBreite, p.BildHoehe, $"/api/plaene/{p.Id}/bild", $"/api/plaene/{p.Id}/pdf");
}

public record StandortAnlegen(string? Name);

/// <summary>Einheitliche Fehlerantworten (RFC 9457 Problem Details) mit deutschem Text.</summary>
public static class Fehler
{
    public static IResult Ungueltig(string meldung) =>
        Results.Problem(title: meldung, statusCode: StatusCodes.Status400BadRequest);

    public static IResult NichtGefunden(string meldung) =>
        Results.Problem(title: meldung, statusCode: StatusCodes.Status404NotFound);

    public static IResult Konflikt(string meldung) =>
        Results.Problem(title: meldung, statusCode: StatusCodes.Status409Conflict);

    /// <summary>True, wenn die Datenbank eine Eindeutigkeitsregel verletzt sieht.</summary>
    public static bool IstDoppelt(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
