using NetzDoku.Api.Dienste;
using NetzDoku.Api.Modell;

namespace NetzDoku.Tests;

public class ReferenzierungTests
{
    private const double Genau = 1e-6;

    [Theory]
    [InlineData(0.02, 0, 10, 20)]
    [InlineData(0.03175, -90, 80, 0)]
    [InlineData(0.5, 37.5, -12.3, 4.56)]
    [InlineData(0.001, 180, 0, 0)]
    public void Zwei_Passpunkte_ergeben_die_bekannte_Lage(double massstab, double drehung, double vx, double vy)
    {
        var wahr = new Plan { Massstab = massstab, Drehung = drehung, VersatzX = vx, VersatzY = vy };
        var p1 = new Planpunkt(120, 3100);
        var p2 = new Planpunkt(2200, 450);

        var a = Geometrie.RichteAus(p1, Geometrie.NachAbsolut(wahr, p1), p2, Geometrie.NachAbsolut(wahr, p2));

        Assert.Equal(massstab, a.Massstab, 1e-12);
        Assert.Equal(drehung, a.Drehung, Genau);
        Assert.Equal(vx, a.VersatzX, Genau);
        Assert.Equal(vy, a.VersatzY, Genau);
    }

    [Fact]
    public void Passpunkte_werden_genau_getroffen()
    {
        var p1 = new Planpunkt(100, 200);
        var p2 = new Planpunkt(900, 700);
        var w1 = new Weltpunkt(0, 0);
        var w2 = new Weltpunkt(80, 42.5);

        var a = Geometrie.RichteAus(p1, w1, p2, w2);
        var plan = new Plan { Massstab = a.Massstab, Drehung = a.Drehung, VersatzX = a.VersatzX, VersatzY = a.VersatzY };

        Assert.Equal(w1, Gerundet(Geometrie.NachAbsolut(plan, p1)));
        Assert.Equal(w2, Gerundet(Geometrie.NachAbsolut(plan, p2)));
    }

    [Fact]
    public void Gleiche_Punkte_im_Plan_werden_abgelehnt()
    {
        Assert.Throws<ArgumentException>(() =>
            Geometrie.RichteAus(new(5, 5), new(0, 0), new(5, 5), new(10, 10)));
    }

    [Fact]
    public void Kette_wird_nach_Aenderung_des_Masterplans_neu_berechnet()
    {
        // Masterplan 0.1 m/px, Gebäudeplan über Passpunkte darauf, Raumplan auf dem Gebäudeplan
        var master = new Plan { Id = 1, Massstab = 0.1, Referenziert = true, IstMasterplan = true, Methode = Referenzierungsmethode.Master };
        var gebaeude = MitPasspunkten(2, bezug: 1, (10, 10, 100, 100), (110, 10, 200, 100));
        var raum = MitPasspunkten(3, bezug: 2, (0, 0, 10, 10), (1000, 0, 60, 10));
        var plaene = new List<Plan> { master, gebaeude, raum };
        Referenzierung.AusPasspunkten(gebaeude, master);
        Referenzierung.AusPasspunkten(raum, gebaeude);
        var vorher = Geometrie.NachAbsolut(raum, new(1000, 0));

        // Masterplan doppelt so grob und verschoben
        master.Massstab = 0.2;
        master.VersatzX = 5;
        var anzahl = Referenzierung.NachfolgerNeuBerechnen(plaene, master);

        var nachher = Geometrie.NachAbsolut(raum, new(1000, 0));
        Assert.Equal(2, anzahl);
        // Raumpunkt 2 = Gebäudepunkt (60, 10) = Masterpunkt (150, 100) → x = 5 + 0.2·150
        Assert.Equal(0.1 * 150, vorher.X, Genau);
        Assert.Equal(5 + 0.2 * 150, nachher.X, Genau);
        Assert.Equal(-0.2 * 100, nachher.Y, Genau);
    }

    [Fact]
    public void Nachfolger_findet_die_ganze_Kette()
    {
        var plaene = new List<Plan>
        {
            new() { Id = 1 },
            new() { Id = 2, BezugsplanId = 1 },
            new() { Id = 3, BezugsplanId = 2 },
            new() { Id = 4, BezugsplanId = 1 },
            new() { Id = 5, BezugsplanId = 4 }
        };

        Assert.Equal([2, 3, 4, 5], Referenzierung.Nachfolger(plaene, 1).Order());
        Assert.Equal([3], Referenzierung.Nachfolger(plaene, 2));
        Assert.Empty(Referenzierung.Nachfolger(plaene, 5));
    }

    private static Plan MitPasspunkten(int id, int bezug,
        (double u, double v, double bu, double bv) a, (double u, double v, double bu, double bv) b) => new()
    {
        Id = id,
        BezugsplanId = bezug,
        Methode = Referenzierungsmethode.Passpunkte,
        Referenziert = true,
        Passpunkte =
        [
            new() { Nummer = 1, PlanX = a.u, PlanY = a.v, BezugX = a.bu, BezugY = a.bv },
            new() { Nummer = 2, PlanX = b.u, PlanY = b.v, BezugX = b.bu, BezugY = b.bv }
        ]
    };

    private static Weltpunkt Gerundet(Weltpunkt w) => new(Math.Round(w.X, 9), Math.Round(w.Y, 9));
}
