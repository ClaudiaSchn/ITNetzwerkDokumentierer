namespace NetzDoku.Api.Modell;

/// <summary>Art einer Position (Rack, Bodendose …). Selbst verwaltbar.</summary>
public class Positionstyp : Entitaet
{
    public string Name { get; set; } = "";
}

/// <summary>Art eines Ports (RJ45, LC, SC, SFP …). Selbst verwaltbar.</summary>
public class Portart : Entitaet
{
    public string Name { get; set; } = "";
}

/// <summary>Art eines Mediums (LWL Singlemode, Cu …) mit den erlaubten Portarten.</summary>
public class Medientyp : Entitaet
{
    public string Name { get; set; } = "";

    public List<Portart> ErlaubtePortarten { get; set; } = [];
}

/// <summary>Vorlage für Geräte; beim Platzieren werden die Ports aus den Portvorlagen erzeugt.</summary>
public class Geraetetyp : Entitaet
{
    public string Name { get; set; } = "";
    public Geraetekategorie Kategorie { get; set; }

    public List<Portvorlage> Portvorlagen { get; set; } = [];
}

public class Portvorlage : Entitaet
{
    public int GeraetetypId { get; set; }
    public Geraetetyp Geraetetyp { get; set; } = null!;

    public int Nummer { get; set; }
    public Portseite Seite { get; set; }

    public int PortartId { get; set; }
    public Portart Portart { get; set; } = null!;
}
