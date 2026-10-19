using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Wegsuche;

/// <summary>
/// Netz eines Standorts im Speicher: Positionen, Verbindungen, Medien mit Anschlüssen, Ports und Patchungen.
/// Grundlage für die Wegsuche. Wird pro Anfrage frisch geladen (Standorte haben überschaubar viele Objekte).
/// </summary>
public sealed class Netz
{
    public sealed record PositionInfo(int Id, string Bezeichnung, string Typ, int Stockwerk, double X, double Y);
    public sealed record VerbindungInfo(int Id, int A, int B, double LaengeMeter);
    public sealed record MediumInfo(int Id, int VerbindungId, string Kabelnummer, int MedientypId, string Medientyp, int Anzahl);
    public sealed record AnschlussInfo(int MediumId, Mediumende Ende, int Nummer, int? PortId);
    public sealed record PortInfo(
        int Id, int GeraetId, string Geraet, Geraetekategorie Kategorie, int Nummer, Portseite Seite, string? Bezeichnung,
        string Portart, int PositionId);
    public sealed record PatchInfo(int PatchungId, string? Bezeichnung, int Port1, int Port2);

    public Dictionary<int, PositionInfo> Positionen { get; private init; } = [];
    public Dictionary<int, VerbindungInfo> Verbindungen { get; private init; } = [];
    public Dictionary<int, MediumInfo> Medien { get; private init; } = [];
    public Dictionary<int, PortInfo> Ports { get; private init; } = [];

    /// <summary>Anschlüsse je (Medium, Ende, Nummer).</summary>
    private Dictionary<(int, Mediumende, int), AnschlussInfo> anschluesse = [];
    /// <summary>Anschluss, der einen Port belegt.</summary>
    private Dictionary<int, AnschlussInfo> anschlussAmPort = [];
    /// <summary>Patchung an einem Port.</summary>
    private Dictionary<int, PatchInfo> patchAmPort = [];
    /// <summary>Port je (Gerät, Nummer, Seite) – für den Durchgang vorne ↔ hinten.</summary>
    private Dictionary<(int, int, Portseite), int> portNachNummer = [];

    public static async Task<Netz> LadenAsync(NetzDbContext db, int standortId, CancellationToken abbruch)
    {
        var positionen = await db.Positionen.AsNoTracking().Where(p => p.StandortId == standortId)
            .Select(p => new PositionInfo(p.Id, p.Bezeichnung, p.Positionstyp.Name, p.Stockwerk, p.X, p.Y))
            .ToDictionaryAsync(p => p.Id, abbruch);
        var verbindungen = (await db.Verbindungen.AsNoTracking().Where(v => v.StandortId == standortId)
                .Select(v => new { v.Id, v.PositionAId, v.PositionBId }).ToListAsync(abbruch))
            .Select(v =>
            {
                var a = positionen[v.PositionAId];
                var b = positionen[v.PositionBId];
                return new VerbindungInfo(v.Id, v.PositionAId, v.PositionBId, Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)));
            })
            .ToDictionary(v => v.Id);
        var medien = await db.Medien.AsNoTracking().Where(m => m.StandortId == standortId)
            .Select(m => new MediumInfo(m.Id, m.VerbindungId, m.Kabelnummer, m.MedientypId, m.Medientyp.Name, m.AnzahlAnschluesse))
            .ToDictionaryAsync(m => m.Id, abbruch);
        var anschluesse = await db.Anschluesse.AsNoTracking().Where(a => a.Medium.StandortId == standortId)
            .Select(a => new AnschlussInfo(a.MediumId, a.Ende, a.Nummer, a.PortId)).ToListAsync(abbruch);
        var ports = await db.Ports.AsNoTracking().Where(p => p.Geraet.Position.StandortId == standortId)
            .Select(p => new PortInfo(p.Id, p.GeraetId, p.Geraet.Bezeichnung, p.Geraet.Geraetetyp.Kategorie, p.Nummer, p.Seite,
                p.Bezeichnung, p.Portart.Name, p.Geraet.PositionId))
            .ToDictionaryAsync(p => p.Id, abbruch);
        var patchungen = (await db.Patchungen.AsNoTracking().Where(p => p.Position.StandortId == standortId)
                .Select(p => new { p.Id, p.Bezeichnung, Ports = p.Enden.OrderBy(e => e.Nummer).Select(e => e.PortId).ToList() })
                .ToListAsync(abbruch))
            .Where(p => p.Ports.Count == 2)
            .Select(p => new PatchInfo(p.Id, p.Bezeichnung, p.Ports[0], p.Ports[1]))
            .ToList();

        var netz = new Netz { Positionen = positionen, Verbindungen = verbindungen, Medien = medien, Ports = ports };
        netz.anschluesse = anschluesse.ToDictionary(a => (a.MediumId, a.Ende, a.Nummer));
        netz.anschlussAmPort = anschluesse.Where(a => a.PortId != null).ToDictionary(a => a.PortId!.Value);
        netz.patchAmPort = patchungen.SelectMany(p => new[] { (p.Port1, p), (p.Port2, p) }).ToDictionary(x => x.Item1, x => x.p);
        netz.portNachNummer = ports.Values.ToDictionary(p => (p.GeraetId, p.Nummer, p.Seite), p => p.Id);
        return netz;
    }

    // ---------------------------------------------------------------- Ports und Fasern

    public bool PortFrei(int portId) => !anschlussAmPort.ContainsKey(portId) && !patchAmPort.ContainsKey(portId);

    /// <summary>Gegenüberliegender Port eines Durchgangsgeräts (vorne ↔ hinten), sonst null.</summary>
    public int? Gegenseite(PortInfo port)
    {
        if (port.Kategorie != Geraetekategorie.Durchgang || port.Seite == Portseite.Einzel) return null;
        var seite = port.Seite == Portseite.Vorne ? Portseite.Hinten : Portseite.Vorne;
        return portNachNummer.TryGetValue((port.GeraetId, port.Nummer, seite), out var id) ? id : null;
    }

    /// <summary>
    /// Ist dieses Faser-Ende für eine neue Strecke verfügbar? Ja, wenn es nicht aufgelegt ist oder auf einem
    /// Durchgangsgerät aufliegt, dessen Gegenseite (z.B. Patchpanel vorne) noch frei ist.
    /// </summary>
    private bool EndeVerfuegbar(AnschlussInfo? a)
    {
        if (a?.PortId is not { } portId) return true;
        var port = Ports[portId];
        return Gegenseite(port) is { } gegen && PortFrei(gegen);
    }

    /// <summary>Anzahl freier Fasern/Adern eines Mediums: beide Enden verfügbar.</summary>
    public int FreieFasern(MediumInfo m)
    {
        var frei = 0;
        for (var n = 1; n <= m.Anzahl; n++)
        {
            anschluesse.TryGetValue((m.Id, Mediumende.A, n), out var a);
            anschluesse.TryGetValue((m.Id, Mediumende.B, n), out var b);
            if (EndeVerfuegbar(a) && EndeVerfuegbar(b)) frei++;
        }
        return frei;
    }

    // ---------------------------------------------------------------- Leitungsverlauf

    /// <summary>Glied zwischen zwei Punkten einer Kette.</summary>
    public sealed record Glied(string Art, int? MediumId = null, string? Kabelnummer = null, string? Faser = null,
        int? VerbindungId = null, int? PatchungId = null, string? Bezeichnung = null);

    /// <summary>Punkt einer Kette: ein Port oder ein offenes Faser-Ende an einer Position.</summary>
    public sealed record Punkt(PortInfo? Port, int PositionId);

    private IEnumerable<(Glied Glied, Punkt Ziel)> Nachbarn(PortInfo port)
    {
        if (Gegenseite(port) is { } gegen)
            yield return (new Glied("durchgang"), new Punkt(Ports[gegen], port.PositionId));
        if (anschlussAmPort.TryGetValue(port.Id, out var a))
        {
            var m = Medien[a.MediumId];
            var v = Verbindungen[m.VerbindungId];
            var andereEnde = a.Ende == Mediumende.A ? Mediumende.B : Mediumende.A;
            anschluesse.TryGetValue((m.Id, andereEnde, a.Nummer), out var partner);
            var glied = new Glied("kabel", m.Id, m.Kabelnummer, a.Nummer.ToString(), v.Id);
            var zielPosition = andereEnde == Mediumende.A ? v.A : v.B;
            yield return (glied, new Punkt(partner?.PortId is { } pid ? Ports[pid] : null, zielPosition));
        }
        if (patchAmPort.TryGetValue(port.Id, out var p))
        {
            var partner = p.Port1 == port.Id ? p.Port2 : p.Port1;
            yield return (new Glied("patch", PatchungId: p.PatchungId, Bezeichnung: p.Bezeichnung), new Punkt(Ports[partner], port.PositionId));
        }
    }

    /// <summary>
    /// Physischer Verlauf durch einen Port: Kabel, Durchgänge (vorne ↔ hinten) und Patchungen in beide
    /// Richtungen, bis zu einem Endgerät oder einem offenen Ende. Aktive Geräte (Switches) werden nicht durchquert.
    /// </summary>
    public (List<Punkt> Punkte, List<Glied> Glieder) Verlauf(int portId)
    {
        var start = Ports[portId];
        var richtungen = Nachbarn(start).ToList();
        var besucht = new HashSet<int> { start.Id };
        var seiten = richtungen.Take(2).Select(r => Gehen(start, r, besucht)).ToList();
        // Ist der Start selbst ein Ende der Kette (z.B. Switch-Port), steht er zuoberst
        if (seiten.Count == 1) seiten.Insert(0, []);

        var punkte = new List<Punkt>();
        var glieder = new List<Glied>();
        if (seiten.Count > 0)
        {
            // linke Seite umgekehrt voranstellen
            for (var i = seiten[0].Count - 1; i >= 0; i--)
            {
                punkte.Add(seiten[0][i].Ziel);
                glieder.Add(seiten[0][i].Glied);
            }
        }
        punkte.Add(new Punkt(start, start.PositionId));
        if (seiten.Count > 1)
        {
            foreach (var (glied, ziel) in seiten[1])
            {
                glieder.Add(glied);
                punkte.Add(ziel);
            }
        }
        return (punkte, glieder);
    }

    private List<(Glied Glied, Punkt Ziel)> Gehen(PortInfo von, (Glied Glied, Punkt Ziel) erster, HashSet<int> besucht)
    {
        var weg = new List<(Glied, Punkt)>();
        var vorher = von;
        var schritt = erster;
        while (true)
        {
            weg.Add(schritt);
            if (schritt.Ziel.Port is not { } aktuell || !besucht.Add(aktuell.Id)) break;
            // Rückweg ausschliessen; ein Port hat höchstens zwei Nachbarn, übrig bleibt also höchstens einer
            var weiter = Nachbarn(aktuell).Where(n => n.Ziel.Port?.Id != vorher.Id).ToList();
            if (weiter.Count == 0) break;
            vorher = aktuell;
            schritt = weiter[0];
        }
        return weg;
    }
}
