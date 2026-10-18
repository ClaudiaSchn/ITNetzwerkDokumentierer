using PDFtoImage;
using SkiaSharp;

namespace NetzDoku.Api.Dienste;

public record Planbild(int Breite, int Hoehe);

public class PdfFehler(string meldung) : Exception(meldung);

/// <summary>
/// Liest PDFs und erzeugt daraus Planbilder (PNG) mit PDFium (über das Paket PDFtoImage).
/// </summary>
// PDFtoImage ist für Windows, Linux und macOS freigegeben – genau die Plattformen,
// auf denen das Backend läuft. Die Plattform-Warnung CA1416 ist hier daher unbegründet.
#pragma warning disable CA1416
public static class PdfUmwandlung
{
    /// <summary>Längste Bildkante in Pixeln; begrenzt Speicherbedarf und Ladezeit im Browser.</summary>
    public const int MaxKante = 6000;

    /// <summary>Höchste Auflösung; kleinere Pläne werden nicht unnötig gross.</summary>
    public const int MaxDpi = 200;

    public static bool IstPdf(Stream datei)
    {
        Span<byte> kopf = stackalloc byte[5];
        var gelesen = datei.Read(kopf);
        datei.Position = 0;
        return gelesen == 5 && kopf.SequenceEqual("%PDF-"u8);
    }

    public static int AnzahlSeiten(Stream pdf)
    {
        try
        {
            return Conversion.GetPageCount(pdf, leaveOpen: true);
        }
        catch (Exception e)
        {
            throw new PdfFehler($"Das PDF konnte nicht gelesen werden ({e.Message}).");
        }
        finally
        {
            pdf.Position = 0;
        }
    }

    /// <summary>Wandelt die erste Seite in ein PNG um und speichert es unter <paramref name="zielpfad"/>.</summary>
    public static Planbild AlsPngSpeichern(Stream pdf, string zielpfad)
    {
        try
        {
            // Seitengrösse in Punkt (1/72 Zoll) → Auflösung so wählen, dass die längste Kante passt
            var groesse = Conversion.GetPageSize(pdf, leaveOpen: true, page: 0);
            pdf.Position = 0;
            var laengsteKanteZoll = Math.Max(groesse.Width, groesse.Height) / 72.0;
            var dpi = (int)Math.Clamp(Math.Floor(MaxKante / laengsteKanteZoll), 36, MaxDpi);

            using var bild = Conversion.ToImage(pdf, leaveOpen: true, page: 0,
                options: new RenderOptions(Dpi: dpi, WithAnnotations: true, BackgroundColor: SKColors.White));
            using var daten = bild.Encode(SKEncodedImageFormat.Png, 100);
            using (var ziel = File.Create(zielpfad))
            {
                daten.SaveTo(ziel);
            }
            return new Planbild(bild.Width, bild.Height);
        }
        catch (Exception e) when (e is not PdfFehler)
        {
            throw new PdfFehler($"Das PDF konnte nicht in ein Bild umgewandelt werden ({e.Message}).");
        }
        finally
        {
            pdf.Position = 0;
        }
    }
}
#pragma warning restore CA1416
