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

/// <summary>Lage eines Plans: Massstab (m/px), Drehung (Grad, −180…180) und Versatz (m).</summary>
public record Ausrichtung(double Massstab, double Drehung, double VersatzX, double VersatzY);

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

        var massstab = MassstabAus(strecken);
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

    /// <summary>
    /// Plan über zwei Passpunkte ausrichten: Zu jedem Punkt im Plan ist die absolute Lage bekannte
    /// (aus dem Bezugsplan umgerechnet). Ergibt Massstab, Drehung und Versatz eindeutig.
    /// </summary>
    /// <remarks>
    /// Rechnet mit komplexen Zahlen: z = u − i·v (Plan, v gespiegelt), w = x + i·y (absolut).
    /// Die Abbildung ist w = Versatz + a·z mit a = Massstab · e^(iθ); aus zwei Paaren folgt
    /// a = (w₂ − w₁) / (z₂ − z₁) und Versatz = w₁ − a·z₁.
    /// </remarks>
    public static Ausrichtung RichteAus(Planpunkt p1, Weltpunkt w1, Planpunkt p2, Weltpunkt w2)
    {
        var zr = p2.U - p1.U;
        var zi = -(p2.V - p1.V);
        var wr = w2.X - w1.X;
        var wi = w2.Y - w1.Y;
        var n = zr * zr + zi * zi;
        if (n == 0) throw new ArgumentException("Die beiden Passpunkte im Plan fallen zusammen.");

        var ar = (wr * zr + wi * zi) / n;
        var ai = (wi * zr - wr * zi) / n;
        var massstab = Math.Sqrt(ar * ar + ai * ai);
        if (massstab == 0) throw new ArgumentException("Die beiden Passpunkte im Bezugsplan fallen zusammen.");

        return new Ausrichtung(
            massstab,
            Drehung: Math.Atan2(ai, ar) * 180.0 / Math.PI,
            VersatzX: w1.X - (ar * p1.U + ai * p1.V),
            VersatzY: w1.Y - (ai * p1.U - ar * p1.V));
    }

    /// <summary>Massstab aus bekannten Strecken, nach Länge gewichtet (Summe Meter / Summe Pixel).</summary>
    public static double MassstabAus(IReadOnlyList<BekannteStrecke> strecken)
    {
        if (strecken.Count == 0) throw new ArgumentException("Mindestens eine Strecke nötig.", nameof(strecken));
        return strecken.Sum(s => s.LaengeMeter) / strecken.Sum(s => s.PixelLaenge);
    }

    /// <summary>
    /// Lage bei bekanntem Massstab und bekannter Drehung: der Ankerpunkt im Plan liegt absolut bei
    /// <paramref name="ankerAbsolut"/>. Daraus folgt der Versatz.
    /// </summary>
    public static Ausrichtung MitEigenemMassstab(double massstab, double drehung, Planpunkt anker, Weltpunkt ankerAbsolut)
    {
        var (sin, cos) = Math.SinCos(drehung * Math.PI / 180.0);
        return new Ausrichtung(
            massstab,
            NormiereWinkel(drehung),
            VersatzX: ankerAbsolut.X - massstab * (anker.U * cos + anker.V * sin),
            VersatzY: ankerAbsolut.Y - massstab * (anker.U * sin - anker.V * cos));
    }

    /// <summary>Richtung von a nach b im Plan in Grad, gemessen wie absolut (0° = rechts, 90° = oben).</summary>
    public static double RichtungImPlan(Planpunkt a, Planpunkt b) => Math.Atan2(-(b.V - a.V), b.U - a.U) * 180.0 / Math.PI;

    /// <summary>Richtung von a nach b absolut in Grad (0° = +x, 90° = +y).</summary>
    public static double RichtungAbsolut(Weltpunkt a, Weltpunkt b) => Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;

    /// <summary>Winkel auf den Bereich −180° … 180° bringen.</summary>
    public static double NormiereWinkel(double grad)
    {
        grad %= 360.0;
        if (grad <= -180.0) grad += 360.0;
        if (grad > 180.0) grad -= 360.0;
        return grad;
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
