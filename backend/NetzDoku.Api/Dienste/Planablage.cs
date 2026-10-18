namespace NetzDoku.Api.Dienste;

/// <summary>
/// Ablage für PDFs und Planbilder im Dateisystem (in Docker: Volume plan-ablage).
/// Dateien erhalten zufällige Namen; in der Datenbank steht nur der Dateiname.
/// </summary>
public class Planablage
{
    public string Pfad { get; }

    public Planablage(IConfiguration konfiguration, IHostEnvironment umgebung)
    {
        Pfad = konfiguration["Ablage:Pfad"] is { Length: > 0 } pfad
            ? pfad
            : Path.Combine(umgebung.ContentRootPath, "daten", "plaene");
        Directory.CreateDirectory(Pfad);
    }

    public string NeuerDateiname(string endung) => $"{Guid.NewGuid():N}{endung}";

    /// <summary>Voller Pfad zu einem Dateinamen; lehnt Pfadangaben ab.</summary>
    public string VollerPfad(string dateiname)
    {
        if (string.IsNullOrWhiteSpace(dateiname) || dateiname != Path.GetFileName(dateiname))
            throw new ArgumentException($"Ungültiger Dateiname: {dateiname}", nameof(dateiname));
        return Path.Combine(Pfad, dateiname);
    }

    public void Loeschen(string? dateiname)
    {
        if (string.IsNullOrEmpty(dateiname)) return;
        var pfad = VollerPfad(dateiname);
        if (File.Exists(pfad)) File.Delete(pfad);
    }
}
