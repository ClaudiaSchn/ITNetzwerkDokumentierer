using NetzDoku.Api.Dienste;
using NetzDoku.Api.Modell;

namespace NetzDoku.Tests;

public class GeometrieTests
{
    private const double Genau = 1e-9;

    // Beispielplan docs/beispiele/Campusplan_Werk_Nord.pdf bei 200 dpi:
    // Massstabsbalken 50 m = 200 pt = 555.56 px, Nullpunkt-Kreuz bei (222.22, 1052.36) px.
    private static readonly Planpunkt Nullpunkt = new(222.22, 1052.36);

    [Fact]
    public void Massstab_aus_einer_Strecke_ist_Meter_pro_Pixel()
    {
        var strecke = new BekannteStrecke(new(100, 500), new(600, 500), LaengeMeter: 50);

        var k = Geometrie.KalibriereMaster(Nullpunkt, [strecke]);

        Assert.Equal(0.1, k.Massstab, Genau);
        Assert.Equal(0, k.AbweichungProzent, Genau);
    }

    [Fact]
    public void Nullpunkt_wird_auf_0_0_abgebildet()
    {
        var plan = Kalibriert(Nullpunkt, new BekannteStrecke(new(0, 0), new(1000, 0), 90));

        var w = Geometrie.NachAbsolut(plan, Nullpunkt);

        Assert.Equal(0, w.X, Genau);
        Assert.Equal(0, w.Y, Genau);
    }

    [Fact]
    public void Rechts_ist_x_positiv_oben_ist_y_positiv()
    {
        var plan = Kalibriert(Nullpunkt, new BekannteStrecke(new(0, 0), new(1000, 0), 100)); // 0.1 m/px

        var rechts = Geometrie.NachAbsolut(plan, Nullpunkt with { U = Nullpunkt.U + 100 });
        var oben = Geometrie.NachAbsolut(plan, Nullpunkt with { V = Nullpunkt.V - 100 }); // Bild: v nach unten

        Assert.Equal(10, rechts.X, Genau);
        Assert.Equal(0, rechts.Y, Genau);
        Assert.Equal(0, oben.X, Genau);
        Assert.Equal(10, oben.Y, Genau);
    }

    [Fact]
    public void Zwei_Strecken_werden_nach_Laenge_gewichtet_gemittelt()
    {
        // 100 m auf 1000 px (0.1) und 10 m auf 50 px (0.2) → 110 m / 1050 px
        var lang = new BekannteStrecke(new(0, 0), new(1000, 0), 100);
        var kurz = new BekannteStrecke(new(0, 0), new(0, 50), 10);

        var k = Geometrie.KalibriereMaster(Nullpunkt, [lang, kurz]);

        Assert.Equal(110.0 / 1050.0, k.Massstab, Genau);
        Assert.Equal([0.1, 0.2], k.MassstabJeStrecke.Select(m => Math.Round(m, 9)));
        Assert.Equal((0.2 - 0.1) / k.Massstab * 100, k.AbweichungProzent, Genau);
    }

    [Fact]
    public void Diagonale_Strecke_nutzt_die_echte_Laenge()
    {
        var strecke = new BekannteStrecke(new(0, 0), new(300, 400), LaengeMeter: 25); // 500 px

        Assert.Equal(500, strecke.PixelLaenge, Genau);
        Assert.Equal(0.05, strecke.Massstab, Genau);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12.5)]
    [InlineData(90)]
    [InlineData(-137.25)]
    public void NachPlan_ist_die_Umkehrung_von_NachAbsolut(double drehung)
    {
        var plan = new Plan { Massstab = 0.0375, Drehung = drehung, VersatzX = 41.3, VersatzY = -7.9 };
        var p = new Planpunkt(1234.5, 678.9);

        var zurueck = Geometrie.NachPlan(plan, Geometrie.NachAbsolut(plan, p));

        Assert.Equal(p.U, zurueck.U, 1e-6);
        Assert.Equal(p.V, zurueck.V, 1e-6);
    }

    [Fact]
    public void Abstaende_bleiben_bei_Drehung_massstabsgetreu()
    {
        var plan = new Plan { Massstab = 0.02, Drehung = 33, VersatzX = 5, VersatzY = 5 };
        var a = Geometrie.NachAbsolut(plan, new Planpunkt(100, 100));
        var b = Geometrie.NachAbsolut(plan, new Planpunkt(400, 500)); // 500 px auseinander

        var meter = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

        Assert.Equal(10, meter, 1e-9);
    }

    private static Plan Kalibriert(Planpunkt nullpunkt, params BekannteStrecke[] strecken)
    {
        var k = Geometrie.KalibriereMaster(nullpunkt, strecken);
        return new Plan { Massstab = k.Massstab, Drehung = 0, VersatzX = k.VersatzX, VersatzY = k.VersatzY };
    }
}
