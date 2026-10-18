using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Data;

/// <summary>
/// Legt beim ersten Start eine Grundausstattung an Stammdaten an.
/// Jede Liste wird nur befüllt, wenn sie noch leer ist; danach werden die
/// Stammdaten in der Anwendung selbst gepflegt.
/// </summary>
public static class Startwerte
{
    public static async Task AnlegenAsync(NetzDbContext db, CancellationToken abbruch = default)
    {
        if (!await db.Positionstypen.AnyAsync(abbruch))
        {
            db.Positionstypen.AddRange(
                new Positionstyp { Name = "Rack" },
                new Positionstyp { Name = "Verteiler" },
                new Positionstyp { Name = "Bodendose" },
                new Positionstyp { Name = "Wanddose" },
                new Positionstyp { Name = "Richtfunksender" });
        }

        if (!await db.Portarten.AnyAsync(abbruch))
        {
            var rj45 = new Portart { Name = "RJ45" };
            var lc = new Portart { Name = "LC" };
            var sc = new Portart { Name = "SC" };
            var sfp = new Portart { Name = "SFP" };
            var funk = new Portart { Name = "Funk" };
            db.Portarten.AddRange(rj45, lc, sc, sfp, funk);

            if (!await db.Medientypen.AnyAsync(abbruch))
            {
                db.Medientypen.AddRange(
                    new Medientyp { Name = "LWL Singlemode", ErlaubtePortarten = [lc, sc] },
                    new Medientyp { Name = "LWL Multimode", ErlaubtePortarten = [lc, sc] },
                    new Medientyp { Name = "Kupfer", ErlaubtePortarten = [rj45] },
                    new Medientyp { Name = "Richtfunk", ErlaubtePortarten = [funk] });
            }

            if (!await db.Geraetetypen.AnyAsync(abbruch))
            {
                db.Geraetetypen.AddRange(
                    Durchgang("Patchpanel 24× RJ45", 24, rj45),
                    Durchgang("Netzwerkdose 2× RJ45", 2, rj45),
                    Durchgang("LWL-Patchpanel 12× LC", 12, lc),
                    Endgeraet("Switch 24× RJ45 + 4× SFP", (24, rj45), (4, sfp)));
            }
        }

        await db.SaveChangesAsync(abbruch);
    }

    private static Geraetetyp Durchgang(string name, int anzahl, Portart portart)
    {
        var typ = new Geraetetyp { Name = name, Kategorie = Geraetekategorie.Durchgang };
        for (var nummer = 1; nummer <= anzahl; nummer++)
        {
            typ.Portvorlagen.Add(new Portvorlage { Nummer = nummer, Seite = Portseite.Vorne, Portart = portart });
            typ.Portvorlagen.Add(new Portvorlage { Nummer = nummer, Seite = Portseite.Hinten, Portart = portart });
        }
        return typ;
    }

    private static Geraetetyp Endgeraet(string name, params (int Anzahl, Portart Portart)[] gruppen)
    {
        var typ = new Geraetetyp { Name = name, Kategorie = Geraetekategorie.Endgeraet };
        var nummer = 1;
        foreach (var (anzahl, portart) in gruppen)
        {
            for (var i = 0; i < anzahl; i++)
            {
                typ.Portvorlagen.Add(new Portvorlage { Nummer = nummer++, Seite = Portseite.Einzel, Portart = portart });
            }
        }
        return typ;
    }
}
