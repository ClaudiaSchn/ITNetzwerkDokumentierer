namespace NetzDoku.Api.Modell;

/// <summary>Gerät an einer Position, erzeugt aus einem Gerätetyp.</summary>
public class Geraet : Entitaet
{
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;

    public int GeraetetypId { get; set; }
    public Geraetetyp Geraetetyp { get; set; } = null!;

    public string Bezeichnung { get; set; } = "";

    public List<Port> Ports { get; set; } = [];
}

/// <summary>
/// Anschlussstelle eines Geräts. Durchgangsgeräte haben pro Nummer einen Port
/// <see cref="Portseite.Vorne"/> und einen <see cref="Portseite.Hinten"/>, intern durchverbunden.
/// </summary>
public class Port : Entitaet
{
    public int GeraetId { get; set; }
    public Geraet Geraet { get; set; } = null!;

    /// <summary>Automatische Nummer 1…n aus der Vorlage.</summary>
    public int Nummer { get; set; }
    public Portseite Seite { get; set; }

    public int PortartId { get; set; }
    public Portart Portart { get; set; } = null!;

    /// <summary>Individuelle Beschriftung, z.B. «Gi1/0/1» oder «A12».</summary>
    public string? Bezeichnung { get; set; }

    /// <summary>Der Anschluss, der diesen Port belegt (höchstens einer).</summary>
    public Anschluss? Anschluss { get; set; }

    /// <summary>Ende einer Patchung an diesem Port (höchstens eines). Ein Port ist entweder angeschlossen oder gepatcht.</summary>
    public Patchende? Patchende { get; set; }
}

/// <summary>
/// Patchkabel zwischen zwei Ports an derselben Position, z.B. Patchpanel vorne → Switch.
/// Die beiden Enden stehen in <see cref="Enden"/>; ein Port kommt dort höchstens einmal vor.
/// </summary>
public class Patchung : Entitaet
{
    public int PositionId { get; set; }
    public Position Position { get; set; } = null!;

    /// <summary>Optionale Beschriftung des Patchkabels.</summary>
    public string? Bezeichnung { get; set; }

    public List<Patchende> Enden { get; set; } = [];
}

/// <summary>Ein Ende einer Patchung: belegt genau einen Port.</summary>
public class Patchende
{
    public int PatchungId { get; set; }
    public Patchung Patchung { get; set; } = null!;

    /// <summary>1 oder 2.</summary>
    public int Nummer { get; set; }

    public int PortId { get; set; }
    public Port Port { get; set; } = null!;
}

/// <summary>Kabel oder Funkstrecke auf einer Verbindung.</summary>
public class Medium : Entitaet
{
    public int VerbindungId { get; set; }
    public Verbindung Verbindung { get; set; } = null!;

    /// <summary>Standort der Verbindung; nötig für die Eindeutigkeit der Kabelnummer.</summary>
    public int StandortId { get; set; }
    public Standort Standort { get; set; } = null!;

    public int MedientypId { get; set; }
    public Medientyp Medientyp { get; set; } = null!;

    /// <summary>Eindeutig pro Standort.</summary>
    public string Kabelnummer { get; set; } = "";

    /// <summary>Anzahl Anschlüsse pro Ende (z.B. 12 bei 12 Fasern, 1 bei Cu-Kabel).</summary>
    public int AnzahlAnschluesse { get; set; } = 1;

    public List<Anschluss> Anschluesse { get; set; } = [];
}

/// <summary>Ein Ende einer Ader/Faser eines Mediums; kann an einen Port angeschlossen werden.</summary>
public class Anschluss : Entitaet
{
    public int MediumId { get; set; }
    public Medium Medium { get; set; } = null!;

    public Mediumende Ende { get; set; }

    /// <summary>1 … Medium.AnzahlAnschluesse.</summary>
    public int Nummer { get; set; }

    /// <summary>Belegter Port; leer = nicht angeschlossen.</summary>
    public int? PortId { get; set; }
    public Port? Port { get; set; }
}
