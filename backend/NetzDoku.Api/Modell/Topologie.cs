namespace NetzDoku.Api.Modell;

/// <summary>
/// Punkt im Netz (Rack, Bodendose, Richtfunksender …). Hängt am Standort, nicht an einem Plan:
/// gespeichert wird nur die absolute Koordinate. Pläne sind Ansichten darauf.
/// </summary>
public class Position : Entitaet
{
    public int StandortId { get; set; }
    public Standort Standort { get; set; } = null!;

    public int PositionstypId { get; set; }
    public Positionstyp Positionstyp { get; set; } = null!;

    /// <summary>Vom Benutzer vergeben, eindeutig pro Standort (z.B. «Serverraum 1», «HV4»).</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>Absolute Koordinate in Metern (System des Masterplans).</summary>
    public double X { get; set; }
    public double Y { get; set; }

    public int Stockwerk { get; set; }

    public List<Geraet> Geraete { get; set; } = [];
    public List<Patchung> Patchungen { get; set; } = [];
    public List<Kommentar> Kommentare { get; set; } = [];
}

/// <summary>
/// Tatsache, dass zwischen zwei Positionen ein Weg für Medien existiert –
/// ohne Leitungsverlauf und Ausführungsart.
/// </summary>
public class Verbindung : Entitaet
{
    /// <summary>Standort beider Positionen (für Abfragen und Eindeutigkeit der Kabelnummer).</summary>
    public int StandortId { get; set; }
    public Standort Standort { get; set; } = null!;

    /// <summary>Die zuerst angeklickte Position (Ende A der Medien).</summary>
    public int PositionAId { get; set; }
    public Position PositionA { get; set; } = null!;

    /// <summary>Ende B der Medien.</summary>
    public int PositionBId { get; set; }
    public Position PositionB { get; set; } = null!;

    public List<Medium> Medien { get; set; } = [];
}

/// <summary>Kommentar zu einer Position, z.B. vor Ort mobil erfasst.</summary>
public class Kommentar : Entitaet
{
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;

    public string Text { get; set; } = "";

    /// <summary>Freies Feld, da in Phase 1 alle mit demselben Login arbeiten.</summary>
    public string Verfasser { get; set; } = "";

    public Kommentarstatus Status { get; set; } = Kommentarstatus.Offen;
    public DateTimeOffset ErstelltAm { get; set; }
}
