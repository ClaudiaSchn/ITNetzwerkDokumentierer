using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

/// <summary>Gerätetyp mit Kurzbeschreibung der Ports, z.B. «24× RJ45 + 4× SFP».</summary>
public record GeraetetypDto(int Id, string Name, string Kategorie, int AnzahlNummern, string Ports);

/// <summary>Gruppe gleicher Ports in einem neuen Gerätetyp: <see cref="Anzahl"/> Nummern mit dieser Portart.</summary>
public record PortgruppeEingabe(int Anzahl, int PortartId);

public record GeraetetypAnlegen(string? Name, string? Kategorie, List<PortgruppeEingabe>? Gruppen);

/// <summary>Gerät in der Liste einer Position. <see cref="BelegtePorts"/>: Ports mit angeschlossenem Medium.</summary>
public record GeraetDto(
    int Id, uint Version, int PositionId, string Bezeichnung, int GeraetetypId, string Geraetetyp, string Kategorie,
    int AnzahlPorts, int BelegtePorts);

/// <summary>Belegung eines Ports: welches Medium, welches Ende, welche Faser/Ader.</summary>
public record BelegungDto(int MediumId, string Kabelnummer, string Ende, int Nummer, string Medientyp);

/// <summary>Patchung an einem Port: Partner-Port (Gerät, Nummer, Seite) und Beschriftung des Patchkabels.</summary>
public record PatchDto(int PatchungId, uint Version, int PartnerGeraetId, string PartnerGeraet, int PartnerNummer, string PartnerSeite, string? Bezeichnung);

public record PortDto(
    int Id, uint Version, int Nummer, string Seite, int PortartId, string Portart, string? Bezeichnung, BelegungDto? Belegung,
    PatchDto? Patch);

public record GeraetDetailDto(GeraetDto Geraet, string Position, IReadOnlyList<PortDto> Ports);

public record GeraetPlatzieren(string? Bezeichnung, int GeraetetypId);

public record GeraetAendern(uint Version, string? Bezeichnung);

public record PortBezeichnung(int Id, uint Version, string? Bezeichnung);

public record PortBezeichnungen(List<PortBezeichnung>? Ports);

public static class GeraetEndpunkte
{
    /// <summary>Höchstzahl Portnummern pro Gerätetyp.</summary>
    public const int MaxNummern = 512;

    public static void MapGeraetEndpunkte(this IEndpointRouteBuilder app)
    {
        // --- Gerätetypen ---
        app.MapGet("/api/geraetetypen", async (NetzDbContext db, CancellationToken abbruch) =>
        {
            var typen = await db.Geraetetypen.AsNoTracking().Include(t => t.Portvorlagen).ThenInclude(v => v.Portart)
                .OrderBy(t => t.Name).ToListAsync(abbruch);
            return typen.Select(TypAlsDto);
        });
        app.MapPost("/api/geraetetypen", TypAnlegen);

        // --- Geräte ---
        app.MapGet("/api/positionen/{positionId:int}/geraete", async (int positionId, NetzDbContext db, CancellationToken abbruch) =>
        {
            if (!await db.Positionen.AnyAsync(p => p.Id == positionId, abbruch)) return Fehler.NichtGefunden("Position nicht gefunden.");
            return Results.Ok(await Abfrage(db.Geraete.AsNoTracking().Where(g => g.PositionId == positionId)
                .OrderBy(g => g.Bezeichnung)).ToListAsync(abbruch));
        });
        app.MapPost("/api/positionen/{positionId:int}/geraete", Platzieren);
        app.MapGet("/api/geraete/{id:int}", Detail);
        app.MapPut("/api/geraete/{id:int}", Aendern);
        app.MapDelete("/api/geraete/{id:int}", Entfernen);
        app.MapPut("/api/geraete/{id:int}/portbezeichnungen", PortsBeschriften);
    }

    /// <summary>
    /// Use Case «Gerätetypen verwalten»: Durchgang = pro Nummer ein Port Vorne und einer Hinten;
    /// Endgerät = Ports Einzel, fortlaufend nummeriert über alle Gruppen (z.B. 24× RJ45, dann 4× SFP).
    /// </summary>
    private static async Task<IResult> TypAnlegen(GeraetetypAnlegen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var name = eingabe.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100) return Fehler.Ungueltig("Bitte einen Namen (höchstens 100 Zeichen) angeben.");
        if (!Enum.TryParse<Geraetekategorie>(eingabe.Kategorie, out var kategorie) || !Enum.IsDefined(kategorie))
            return Fehler.Ungueltig("Bitte die Kategorie wählen: Durchgang oder Endgerät.");
        var gruppen = eingabe.Gruppen ?? [];
        if (gruppen.Count == 0) return Fehler.Ungueltig("Bitte mindestens eine Portgruppe angeben.");
        if (gruppen.Any(g => g.Anzahl < 1)) return Fehler.Ungueltig("Jede Portgruppe braucht mindestens einen Port.");
        if (gruppen.Sum(g => g.Anzahl) > MaxNummern) return Fehler.Ungueltig($"Höchstens {MaxNummern} Portnummern pro Gerätetyp.");
        var ids = gruppen.Select(g => g.PortartId).Distinct().ToList();
        var portarten = await db.Portarten.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, abbruch);
        if (portarten.Count != ids.Count) return Fehler.Ungueltig("Unbekannte Portart.");

        var typ = new Geraetetyp { Name = name, Kategorie = kategorie };
        var nummer = 1;
        foreach (var g in gruppen)
        {
            for (var i = 0; i < g.Anzahl; i++, nummer++)
            {
                var portart = portarten[g.PortartId];
                if (kategorie == Geraetekategorie.Durchgang)
                {
                    typ.Portvorlagen.Add(new Portvorlage { Nummer = nummer, Seite = Portseite.Vorne, Portart = portart });
                    typ.Portvorlagen.Add(new Portvorlage { Nummer = nummer, Seite = Portseite.Hinten, Portart = portart });
                }
                else typ.Portvorlagen.Add(new Portvorlage { Nummer = nummer, Seite = Portseite.Einzel, Portart = portart });
            }
        }
        db.Geraetetypen.Add(typ);
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateException e) when (Fehler.IstDoppelt(e))
        {
            return Fehler.Konflikt($"Den Gerätetyp «{name}» gibt es bereits.");
        }
        return Results.Created($"/api/geraetetypen/{typ.Id}", TypAlsDto(typ));
    }

    /// <summary>Use Case «Gerät platzieren»: an einer Position; die Ports werden aus der Vorlage erzeugt.</summary>
    private static async Task<IResult> Platzieren(int positionId, GeraetPlatzieren eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        if (!await db.Positionen.AnyAsync(p => p.Id == positionId, abbruch)) return Fehler.NichtGefunden("Position nicht gefunden.");
        var bezeichnung = eingabe.Bezeichnung?.Trim() ?? "";
        if (bezeichnung.Length is 0 or > 100) return Fehler.Ungueltig("Bitte eine Bezeichnung (höchstens 100 Zeichen) angeben.");
        var typ = await db.Geraetetypen.AsNoTracking().Include(t => t.Portvorlagen)
            .FirstOrDefaultAsync(t => t.Id == eingabe.GeraetetypId, abbruch);
        if (typ is null) return Fehler.Ungueltig("Bitte einen Gerätetyp wählen.");
        if (await db.Geraete.AnyAsync(g => g.PositionId == positionId && g.Bezeichnung == bezeichnung, abbruch))
            return Fehler.Konflikt($"An dieser Position gibt es bereits ein Gerät «{bezeichnung}».");

        var geraet = new Geraet
        {
            PositionId = positionId,
            GeraetetypId = typ.Id,
            Bezeichnung = bezeichnung,
            Ports = typ.Portvorlagen.OrderBy(v => v.Nummer).ThenBy(v => v.Seite)
                .Select(v => new Port { Nummer = v.Nummer, Seite = v.Seite, PortartId = v.PortartId }).ToList()
        };
        db.Geraete.Add(geraet);
        await db.SaveChangesAsync(abbruch); // Gerät und alle Ports in einer Transaktion

        var dto = await Abfrage(db.Geraete.AsNoTracking().Where(g => g.Id == geraet.Id)).SingleAsync(abbruch);
        return Results.Created($"/api/geraete/{geraet.Id}", dto);
    }

    /// <summary>Gerät mit allen Ports und deren Belegung.</summary>
    private static async Task<IResult> Detail(int id, NetzDbContext db, CancellationToken abbruch)
    {
        var geraet = await Abfrage(db.Geraete.AsNoTracking().Where(g => g.Id == id)).SingleOrDefaultAsync(abbruch);
        if (geraet is null) return Fehler.NichtGefunden("Gerät nicht gefunden.");
        var position = await db.Positionen.Where(p => p.Id == geraet.PositionId).Select(p => p.Bezeichnung).SingleAsync(abbruch);
        var ports = await db.Ports.AsNoTracking().Where(p => p.GeraetId == id)
            .OrderBy(p => p.Nummer).ThenBy(p => p.Seite)
            .Select(p => new
            {
                p.Id, p.Version, p.Nummer, p.Seite, p.PortartId, Portart = p.Portart.Name, p.Bezeichnung,
                Anschluss = p.Anschluss == null ? null : new
                {
                    p.Anschluss.MediumId, p.Anschluss.Medium.Kabelnummer, p.Anschluss.Ende, p.Anschluss.Nummer,
                    Medientyp = p.Anschluss.Medium.Medientyp.Name
                },
                Patch = p.Patchende == null ? null : new
                {
                    p.Patchende.PatchungId, p.Patchende.Patchung.Version, p.Patchende.Patchung.Bezeichnung,
                    Partner = p.Patchende.Patchung.Enden.Where(x => x.PortId != p.Id)
                        .Select(x => new { x.Port.GeraetId, Geraet = x.Port.Geraet.Bezeichnung, x.Port.Nummer, x.Port.Seite })
                        .FirstOrDefault()
                }
            })
            .ToListAsync(abbruch);
        return Results.Ok(new GeraetDetailDto(geraet, position, ports.Select(p => new PortDto(
            p.Id, p.Version, p.Nummer, p.Seite.ToString(), p.PortartId, p.Portart, p.Bezeichnung,
            p.Anschluss is null ? null
                : new BelegungDto(p.Anschluss.MediumId, p.Anschluss.Kabelnummer, p.Anschluss.Ende.ToString(), p.Anschluss.Nummer, p.Anschluss.Medientyp),
            p.Patch?.Partner is null ? null
                : new PatchDto(p.Patch.PatchungId, p.Patch.Version, p.Patch.Partner.GeraetId, p.Patch.Partner.Geraet,
                    p.Patch.Partner.Nummer, p.Patch.Partner.Seite.ToString(), p.Patch.Bezeichnung)))
            .ToList()));
    }

    private static async Task<IResult> Aendern(int id, GeraetAendern eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var geraet = await db.Geraete.FirstOrDefaultAsync(g => g.Id == id, abbruch);
        if (geraet is null) return Fehler.NichtGefunden("Gerät nicht gefunden.");
        var bezeichnung = eingabe.Bezeichnung?.Trim() ?? "";
        if (bezeichnung.Length is 0 or > 100) return Fehler.Ungueltig("Bitte eine Bezeichnung (höchstens 100 Zeichen) angeben.");
        if (await db.Geraete.AnyAsync(g => g.PositionId == geraet.PositionId && g.Id != id && g.Bezeichnung == bezeichnung, abbruch))
            return Fehler.Konflikt($"An dieser Position gibt es bereits ein Gerät «{bezeichnung}».");

        db.Entry(geraet).Property(g => g.Version).OriginalValue = eingabe.Version;
        geraet.Bezeichnung = bezeichnung;
        db.Entry(geraet).Property(g => g.Bezeichnung).IsModified = true;
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return GeraetGeaendert(); }
        return Results.Ok(await Abfrage(db.Geraete.AsNoTracking().Where(g => g.Id == id)).SingleAsync(abbruch));
    }

    /// <summary>Use Case «Gerät entfernen»: nur, solange kein Port belegt ist (Regel 10). Ports werden mitgelöscht.</summary>
    private static async Task<IResult> Entfernen(int id, uint version, NetzDbContext db, CancellationToken abbruch)
    {
        var geraet = await db.Geraete.FirstOrDefaultAsync(g => g.Id == id, abbruch);
        if (geraet is null) return Fehler.NichtGefunden("Gerät nicht gefunden.");
        var belegt = await db.Ports.CountAsync(p => p.GeraetId == id && (p.Anschluss != null || p.Patchende != null), abbruch);
        if (belegt > 0)
            return Fehler.Konflikt(belegt == 1
                ? $"An «{geraet.Bezeichnung}» ist noch ein Port belegt (Kabel oder Patchung). Bitte ihn zuerst freigeben."
                : $"An «{geraet.Bezeichnung}» sind noch {belegt} Ports belegt (Kabel oder Patchungen). Bitte sie zuerst freigeben.");

        db.Entry(geraet).Property(g => g.Version).OriginalValue = version;
        db.Geraete.Remove(geraet);
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException) { return GeraetGeaendert(); }
        return Results.NoContent();
    }

    /// <summary>Individuelle Portbeschriftungen (z.B. «Gi1/0/1») für mehrere Ports eines Geräts in einer Transaktion.</summary>
    private static async Task<IResult> PortsBeschriften(int id, PortBezeichnungen eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        if (!await db.Geraete.AnyAsync(g => g.Id == id, abbruch)) return Fehler.NichtGefunden("Gerät nicht gefunden.");
        var liste = eingabe.Ports ?? [];
        if (liste.Any(p => (p.Bezeichnung?.Trim().Length ?? 0) > 100))
            return Fehler.Ungueltig("Eine Portbezeichnung darf höchstens 100 Zeichen lang sein.");
        var ids = liste.Select(p => p.Id).ToList();
        var ports = await db.Ports.Where(p => p.GeraetId == id && ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, abbruch);
        if (ports.Count != ids.Distinct().Count()) return Fehler.Ungueltig("Unbekannter Port.");

        foreach (var e in liste)
        {
            var port = ports[e.Id];
            db.Entry(port).Property(p => p.Version).OriginalValue = e.Version;
            port.Bezeichnung = string.IsNullOrWhiteSpace(e.Bezeichnung) ? null : e.Bezeichnung.Trim();
            db.Entry(port).Property(p => p.Bezeichnung).IsModified = true;
        }
        try { await db.SaveChangesAsync(abbruch); }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Konflikt("Ports wurden inzwischen von jemand anderem geändert. Bitte neu laden und die Eingabe wiederholen.");
        }
        return await Detail(id, db, abbruch);
    }

    private static IResult GeraetGeaendert() =>
        Fehler.Konflikt("Das Gerät wurde inzwischen von jemand anderem geändert. Bitte neu laden und die Eingabe wiederholen.");

    private static IQueryable<GeraetDto> Abfrage(IQueryable<Geraet> geraete) =>
        geraete.Select(g => new GeraetDto(
            g.Id, g.Version, g.PositionId, g.Bezeichnung, g.GeraetetypId, g.Geraetetyp.Name, g.Geraetetyp.Kategorie.ToString(),
            g.Ports.Count, g.Ports.Count(p => p.Anschluss != null || p.Patchende != null)));

    private static GeraetetypDto TypAlsDto(Geraetetyp t)
    {
        // Aufeinanderfolgende Nummern mit gleicher Portart zusammenfassen: «24× RJ45 + 4× SFP»
        var nummern = t.Portvorlagen.GroupBy(v => v.Nummer).OrderBy(g => g.Key).Select(g => g.First().Portart.Name).ToList();
        var teile = new List<string>();
        for (var i = 0; i < nummern.Count;)
        {
            var j = i;
            while (j < nummern.Count && nummern[j] == nummern[i]) j++;
            teile.Add($"{j - i}× {nummern[i]}");
            i = j;
        }
        return new GeraetetypDto(t.Id, t.Name, t.Kategorie.ToString(), nummern.Count, string.Join(" + ", teile));
    }
}
