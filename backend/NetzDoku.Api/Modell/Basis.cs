namespace NetzDoku.Api.Modell;

/// <summary>
/// Gemeinsame Basis aller gespeicherten Objekte.
/// </summary>
public abstract class Entitaet
{
    public int Id { get; set; }

    /// <summary>
    /// Versionsnummer für optimistisches Sperren. Wird auf die PostgreSQL-Systemspalte
    /// <c>xmin</c> abgebildet und bei jeder Änderung von der Datenbank selbst erhöht.
    /// Stimmt sie beim Speichern nicht mehr, wirft EF Core eine
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
    /// </summary>
    public uint Version { get; set; }
}

public enum Referenzierungsmethode
{
    /// <summary>Masterplan: absoluter Nullpunkt, Massstab über bekannte Strecken.</summary>
    Master,
    /// <summary>Über zwei Passpunkte auf einen Bezugsplan ausgerichtet.</summary>
    Passpunkte,
    /// <summary>Eigener Massstab, Lage und Drehung manuell angegeben.</summary>
    EigenerMassstab
}

public enum Kommentarstatus
{
    Offen,
    Erledigt
}

public enum Geraetekategorie
{
    /// <summary>Reicht ein Medium durch (Patchpanel, Netzwerkdose).</summary>
    Durchgang,
    /// <summary>Hier endet eine Strecke (Switch, Access Point, PC …).</summary>
    Endgeraet
}

public enum Portseite
{
    /// <summary>Port eines Endgeräts.</summary>
    Einzel,
    /// <summary>Patchseite eines Durchgangsgeräts.</summary>
    Vorne,
    /// <summary>Installationsseite eines Durchgangsgeräts.</summary>
    Hinten
}

public enum Mediumende
{
    /// <summary>Ende an Verbindung.PositionA.</summary>
    A,
    /// <summary>Ende an Verbindung.PositionB.</summary>
    B
}
