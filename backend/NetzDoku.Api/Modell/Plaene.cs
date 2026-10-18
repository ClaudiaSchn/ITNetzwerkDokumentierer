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
/// Umrechnung Planpunkt (u, v) in Planeinheiten → absolute Koordinate (x, y) in Metern,
/// mit θ = <see cref="Drehung"/> in Grad:
/// <code>
/// x = VersatzX + Massstab · (u · cos θ − v · sin θ)
/// y = VersatzY + Massstab · (u · sin θ + v · cos θ)
/// </code>
/// Beim Masterplan ist θ = 0; der Versatz legt den absoluten Nullpunkt fest.
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

    /// <summary>False, sobald der Plan durch eine neue Version ersetzt wurde.</summary>
    public bool Aktiv { get; set; } = true;

    /// <summary>Plan, auf den dieser referenziert ist. Beim Masterplan leer.</summary>
    public int? BezugsplanId { get; set; }
    public Plan? Bezugsplan { get; set; }

    /// <summary>Ältere Planversion, die durch diesen Plan ersetzt wurde.</summary>
    public int? ErsetztPlanId { get; set; }
    public Plan? ErsetztPlan { get; set; }

    public List<Passpunkt> Passpunkte { get; set; } = [];
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
