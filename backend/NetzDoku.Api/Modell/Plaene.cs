namespace NetzDoku.Api.Modell;

/// <summary>Firmenstandort mit genau einem (aktiven) Masterplan.</summary>
public class Standort : Entitaet
{
    public string Name { get; set; } = "";

    public List<Plan> Plaene { get; set; } = [];
    public List<Position> Positionen { get; set; } = [];
}

/// <summary>
/// Ein einseitiges PDF (Gelände, Gebäude, Geschoss, Raum), referenziert auf den Masterplan.
/// </summary>
/// <remarks>
/// Planeinheiten (u, v) sind Pixel des Planbilds: Ursprung oben links, v nach unten.
/// Absolute Koordinaten (x, y) sind Meter: x nach rechts, y nach oben, (0, 0) = Nullpunkt
/// des Masterplans. Umrechnung mit θ = <see cref="Drehung"/> in Grad:
/// <code>
/// x = VersatzX + Massstab · ( u · cos θ + v · sin θ)
/// y = VersatzY + Massstab · ( u · sin θ − v · cos θ)
/// </code>
/// Beim Masterplan ist θ = 0; der Versatz legt den absoluten Nullpunkt fest.
/// Siehe <see cref="NetzDoku.Api.Dienste.Geometrie"/>.
/// </remarks>
public class Plan : Entitaet
{
    public int StandortId { get; set; }
    public Standort Standort { get; set; } = null!;

    public string Bezeichnung { get; set; } = "";

    /// <summary>Dateiname des Original-PDFs in der Planablage.</summary>
    public string PdfDatei { get; set; } = "";

    /// <summary>Dateiname des beim Import erzeugten Planbilds (PNG) in der Planablage.</summary>
    public string? BildDatei { get; set; }

    /// <summary>Grösse des Planbilds in Pixeln. Planeinheiten (u, v) sind Pixel dieses Bilds.</summary>
    public int BildBreite { get; set; }
    public int BildHoehe { get; set; }

    /// <summary>0 = EG, 1 = 1.OG, −1 = 1.UG. Masterplan immer 0.</summary>
    public int Stockwerk { get; set; }

    public bool IstMasterplan { get; set; }
    public Referenzierungsmethode Methode { get; set; }

    /// <summary>Meter pro Planeinheit, &gt; 0.</summary>
    public double Massstab { get; set; } = 1;

    /// <summary>Drehung gegenüber dem Masterplan in Grad.</summary>
    public double Drehung { get; set; }

    public double VersatzX { get; set; }
    public double VersatzY { get; set; }

    /// <summary>
    /// True, sobald Massstab, Drehung und Versatz bestimmt sind (Masterplan kalibriert bzw.
    /// Plan referenziert). Vorher sind die Werte nur Platzhalter.
    /// </summary>
    public bool Referenziert { get; set; }

    /// <summary>False, sobald der Plan durch eine neue Version ersetzt wurde.</summary>
    public bool Aktiv { get; set; } = true;

    /// <summary>Plan, auf den dieser referenziert ist. Beim Masterplan leer.</summary>
    public int? BezugsplanId { get; set; }
    public Plan? Bezugsplan { get; set; }

    /// <summary>Ältere Planversion, die durch diesen Plan ersetzt wurde.</summary>
    public int? ErsetztPlanId { get; set; }
    public Plan? ErsetztPlan { get; set; }

    public List<Passpunkt> Passpunkte { get; set; } = [];
    public List<Messstrecke> Messstrecken { get; set; } = [];
}

/// <summary>
/// Gespeicherter Passpunkt einer Referenzierung, damit sie nachvollziehbar und korrigierbar bleibt.
/// </summary>
public class Passpunkt : Entitaet
{
    public int PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    /// <summary>1 oder 2.</summary>
    public int Nummer { get; set; }

    /// <summary>Lage im neuen Plan (Planeinheiten).</summary>
    public double PlanX { get; set; }
    public double PlanY { get; set; }

    /// <summary>Lage im Bezugsplan (Planeinheiten des Bezugsplans).</summary>
    public double BezugX { get; set; }
    public double BezugY { get; set; }
}

/// <summary>
/// Bekannte Strecke im Plan, über die der Massstab bestimmt wurde (Masterplan,
/// eigener Massstab). Gespeichert, damit die Kalibrierung nachvollziehbar und korrigierbar bleibt.
/// </summary>
public class Messstrecke : Entitaet
{
    public int PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    public int Nummer { get; set; }

    /// <summary>Anfang und Ende in Planeinheiten.</summary>
    public double U1 { get; set; }
    public double V1 { get; set; }
    public double U2 { get; set; }
    public double V2 { get; set; }

    /// <summary>Bekannte Länge in Metern.</summary>
    public double LaengeMeter { get; set; }
}
