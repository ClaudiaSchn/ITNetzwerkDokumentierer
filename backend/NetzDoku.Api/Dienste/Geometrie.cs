using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Dienste;

/// <summary>Punkt in Planeinheiten (Pixel des Planbilds, v nach unten).</summary>
public readonly record struct Planpunkt(double U, double V);

/// <summary>Punkt in absoluten Koordinaten (Meter, y nach oben).</summary>
public readonly record struct Weltpunkt(double X, double Y);

/// <summary>Bekannte Strecke im Plan.</summary>
public readonly record struct BekannteStrecke(Planpunkt Anfang, Planpunkt Ende, double LaengeMeter)
{
    public double PixelLaenge => Geometrie.Abstand(Anfang, Ende);

    /// <summary>Meter pro Planeinheit nach dieser Strecke allein.</summary>
    public double Massstab => LaengeMeter / PixelLaenge;
}

/// <summary>Ergebnis einer Masterplan-Kalibrierung.</summary>
public record MasterKalibrierung(
    double Massstab,
    double VersatzX,
    double VersatzY,
    IReadOnlyList<double> MassstabJeStrecke,
    double AbweichungProzent);

/// <summary>
/// Umrechnungen zwischen Plan und absoluten Koordinaten.
/// <para>
/// Planeinheiten (u, v): Pixel des Planbilds, Ursprung oben links, v nach unten.<br/>
/// Absolute Koordinaten (x, y): Meter, x nach rechts, y nach oben, (0, 0) = Nullpunkt des Masterplans.
/// </para>
/// <code>
/// x = VersatzX + Massstab · ( u · cos θ + v · sin θ)
/// y = VersatzY + Massstab · ( u · sin θ − v · cos θ)
/// </code>
/// </summary>
public static class Geometrie
{
    /// <summary>Ab dieser Abweichung zwischen den Strecken wird ein Hinweis angezeigt.</summary>
    public const double HinweisAbweichungProzent = 2.0;

    public static double Abstand(Planpunkt a, Planpunkt b) => Math.Sqrt(Math.Pow(b.U - a.U, 2) + Math.Pow(b.V - a.V, 2));

    /// <summary>
    /// Masterplan: Nullpunkt und bekannte Strecken → Massstab und Versatz (Drehung 0).
    /// Der Massstab wird über die Längen gewichtet gemittelt (Summe Meter / Summe Pixel);
    /// längere Strecken sind genauer und zählen deshalb mehr.
    /// </summary>
    public static MasterKalibrierung KalibriereMaster(Planpunkt nullpunkt, IReadOnlyList<BekannteStrecke> strecken)
    {
        if (strecken.Count == 0) throw new ArgumentException("Mindestens eine Strecke nötig.", nameof(strecken));

        var massstab = strecken.Sum(s => s.LaengeMeter) / strecken.Sum(s => s.PixelLaenge);
        var jeStrecke = strecken.Select(s => s.Massstab).ToList();
        var abweichung = (jeStrecke.Max() - jeStrecke.Min()) / massstab * 100.0;

        // Nullpunkt soll auf (0, 0) fallen: 0 = VersatzX + s·u0  und  0 = VersatzY − s·v0
        return new MasterKalibrierung(
            massstab,
            VersatzX: -massstab * nullpunkt.U,
            VersatzY: massstab * nullpunkt.V,
            jeStrecke,
            abweichung);
    }

    public static Weltpunkt NachAbsolut(Plan plan, Planpunkt p)
    {
        var (sin, cos) = Math.SinCos(plan.Drehung * Math.PI / 180.0);
        return new Weltpunkt(
            plan.VersatzX + plan.Massstab * (p.U * cos + p.V * sin),
            plan.VersatzY + plan.Massstab * (p.U * sin - p.V * cos));
    }

    public static Planpunkt NachPlan(Plan plan, Weltpunkt w)
    {
        var (sin, cos) = Math.SinCos(plan.Drehung * Math.PI / 180.0);
        var dx = (w.X - plan.VersatzX) / plan.Massstab;
        var dy = (w.Y - plan.VersatzY) / plan.Massstab;
        // Umkehrung der Drehung/Spiegelung oben
        return new Planpunkt(dx * cos + dy * sin, dx * sin - dy * cos);
    }
}
