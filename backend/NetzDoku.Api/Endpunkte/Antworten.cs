using Microsoft.EntityFrameworkCore;
using Npgsql;
using NetzDoku.Api.Dienste;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

// Datenübertragungsobjekte (was die API nach aussen gibt bzw. annimmt)

public record StandortDto(int Id, string Name, PlanKurzDto? Masterplan);

public record PlanKurzDto(int Id, string Bezeichnung, bool Referenziert);

public record PunktDto(double U, double V);

public record PasspunktDto(int Nummer, double PlanU, double PlanV, double BezugU, double BezugV);

/// <summary>Plan in der Planliste eines Standorts.</summary>
public record PlanListeDto(
    int Id, string Bezeichnung, int Stockwerk, bool IstMasterplan, string Methode, bool Referenziert, int? BezugsplanId);

/// <summary>Linie im Plan (Planeinheiten), z.B. eine Richtung.</summary>
public record LinieDto(double U1, double V1, double U2, double V2);

/// <summary>Angaben einer Referenzierung mit eigenem Massstab (Massstab über die Messstrecken).</summary>
public record EigenerMassstabDto(
    PunktDto Anker, PunktDto? AnkerBezug, double? AnkerX, double? AnkerY,
    double? Winkel, LinieDto? RichtungPlan, LinieDto? RichtungBezug)
{
    public static EigenerMassstabDto? Aus(string? json)
    {
        var d = EigenerMassstabDaten.AusJson(json);
        if (d is null) return null;
        static LinieDto? L(Richtungslinie? r) => r is null ? null : new LinieDto(r.Von.U, r.Von.V, r.Bis.U, r.Bis.V);
        return new EigenerMassstabDto(
            new PunktDto(d.Anker.U, d.Anker.V),
            d.AnkerBezug is { } b ? new PunktDto(b.U, b.V) : null,
            d.AnkerAbsolut?.X, d.AnkerAbsolut?.Y,
            d.Winkel, L(d.RichtungPlan), L(d.RichtungBezug));
    }
}

public record MessstreckeDto(int Nummer, double U1, double V1, double U2, double V2, double LaengeMeter, double Massstab);

public record PlanDto(
    int Id,
    uint Version,
    int StandortId,
    string Bezeichnung,
    int Stockwerk,
    bool IstMasterplan,
    string Methode,
    int BildBreite,
    int BildHoehe,
    string BildUrl,
    string PdfUrl,
    bool Referenziert,
    double Massstab,
    double Drehung,
    double VersatzX,
    double VersatzY,
    PunktDto? Nullpunkt,
    IReadOnlyList<MessstreckeDto> Messstrecken,
    double? AbweichungProzent,
    int? BezugsplanId,
    IReadOnlyList<PasspunktDto> Passpunkte,
    EigenerMassstabDto? EigenerMassstab)
{
    /// <summary>Erwartet einen Plan mit geladenen <see cref="Plan.Messstrecken"/> und <see cref="Plan.Passpunkte"/>.</summary>
    public static PlanDto Aus(Plan p)
    {
        var strecken = p.Messstrecken.OrderBy(m => m.Nummer).Select(m =>
        {
            var pixel = Geometrie.Abstand(new Planpunkt(m.U1, m.V1), new Planpunkt(m.U2, m.V2));
            return new MessstreckeDto(m.Nummer, m.U1, m.V1, m.U2, m.V2, m.LaengeMeter, m.LaengeMeter / pixel);
        }).ToList();

        // Nullpunkt im Masterplan = der Planpunkt, der auf (0, 0) abgebildet wird
        PunktDto? nullpunkt = null;
        if (p.IstMasterplan && p.Referenziert)
        {
            var n = Geometrie.NachPlan(p, new Weltpunkt(0, 0));
            nullpunkt = new PunktDto(n.U, n.V);
        }

        double? abweichung = strecken.Count > 1
            ? (strecken.Max(s => s.Massstab) - strecken.Min(s => s.Massstab)) / p.Massstab * 100.0
            : null;

        return new PlanDto(
            p.Id, p.Version, p.StandortId, p.Bezeichnung, p.Stockwerk, p.IstMasterplan, p.Methode.ToString(),
            p.BildBreite, p.BildHoehe, $"/api/plaene/{p.Id}/bild", $"/api/plaene/{p.Id}/pdf",
            p.Referenziert, p.Massstab, p.Drehung, p.VersatzX, p.VersatzY, nullpunkt, strecken, abweichung,
            p.BezugsplanId,
            p.Passpunkte.OrderBy(x => x.Nummer)
                .Select(x => new PasspunktDto(x.Nummer, x.PlanX, x.PlanY, x.BezugX, x.BezugY)).ToList(),
            EigenerMassstabDto.Aus(p.EigenerMassstabDaten));
    }
}

public record StandortAnlegen(string? Name);

public record StreckeEingabe(double U1, double V1, double U2, double V2, double LaengeMeter);

/// <summary>Kalibrierung des Masterplans. <see cref="Version"/> = Version des Plans beim Laden.</summary>
public record MasterKalibrierungEingabe(uint Version, PunktDto? Nullpunkt, List<StreckeEingabe>? Strecken);

/// <summary>Ein Passpunkt: Lage im neuen Plan und im Bezugsplan (je Planeinheiten).</summary>
public record PasspunktEingabe(double PlanU, double PlanV, double BezugU, double BezugV);

/// <summary>Referenzierung über zwei Passpunkte. <see cref="Version"/> = Version des Plans beim Laden.</summary>
public record ReferenzierungEingabe(uint Version, int BezugsplanId, List<PasspunktEingabe>? Passpunkte);

/// <summary>
/// Referenzierung mit eigenem Massstab: 1–2 bekannte Strecken; Ankerpunkt im Plan und dazu entweder
/// <see cref="AnkerBezug"/> (Klick im Bezugsplan) oder <see cref="AnkerX"/>/<see cref="AnkerY"/> (Meter);
/// Drehung entweder als <see cref="Winkel"/> (Grad) oder als Richtung in Plan und Bezugsplan.
/// </summary>
public record EigenerMassstabEingabe(
    uint Version, int BezugsplanId, List<StreckeEingabe>? Strecken,
    PunktDto? Anker, PunktDto? AnkerBezug, double? AnkerX, double? AnkerY,
    double? Winkel, LinieDto? RichtungPlan, LinieDto? RichtungBezug);

/// <summary>Bezeichnung und Stockwerk eines Plans ändern.</summary>
public record PlanAendern(uint Version, string? Bezeichnung, int Stockwerk);

/// <summary>Einheitliche Fehlerantworten (RFC 9457 Problem Details) mit deutschem Text.</summary>
public static class Fehler
{
    public static IResult Ungueltig(string meldung) =>
        Results.Problem(title: meldung, statusCode: StatusCodes.Status400BadRequest);

    public static IResult NichtGefunden(string meldung) =>
        Results.Problem(title: meldung, statusCode: StatusCodes.Status404NotFound);

    public static IResult Konflikt(string meldung) =>
        Results.Problem(title: meldung, statusCode: StatusCodes.Status409Conflict);

    public static IResult Geaendert() => Konflikt(
        "Der Plan wurde inzwischen von jemand anderem geändert. Bitte die Seite neu laden und die Eingabe wiederholen.");

    /// <summary>True, wenn die Datenbank eine Eindeutigkeitsregel verletzt sieht.</summary>
    public static bool IstDoppelt(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
