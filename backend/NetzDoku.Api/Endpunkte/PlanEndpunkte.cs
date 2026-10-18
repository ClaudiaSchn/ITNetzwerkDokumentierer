using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Data;
using NetzDoku.Api.Dienste;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Endpunkte;

public static class PlanEndpunkte
{
    /// <summary>Kürzeste erlaubte Messstrecke in Pixeln; kürzere sind zu ungenau.</summary>
    public const double MinStreckePixel = 20;

    public static void MapPlanEndpunkte(this IEndpointRouteBuilder app)
    {
        var gruppe = app.MapGroup("/api/plaene");

        gruppe.MapGet("/{id:int}", async (int id, NetzDbContext db, CancellationToken abbruch) =>
        {
            var plan = await db.Plaene.AsNoTracking().Include(p => p.Messstrecken).Include(p => p.Passpunkte)
                .FirstOrDefaultAsync(p => p.Id == id, abbruch);
            return plan is null ? Fehler.NichtGefunden("Plan nicht gefunden.") : Results.Ok(PlanDto.Aus(plan));
        });

        gruppe.MapPut("/{id:int}/kalibrierung", MasterKalibrieren);
        gruppe.MapPut("/{id:int}/referenzierung", Referenzieren);
        gruppe.MapPut("/{id:int}/eigener-massstab", EigenerMassstab);
        gruppe.MapPut("/{id:int}", Aendern);
        gruppe.MapDelete("/{id:int}", Loeschen);

        // Planbild (PNG). Dateinamen sind eindeutig und ändern sich nie → darf lange gecacht werden.
        gruppe.MapGet("/{id:int}/bild", async (int id, NetzDbContext db, Planablage ablage, HttpContext http,
            CancellationToken abbruch) =>
        {
            var datei = await db.Plaene.Where(p => p.Id == id).Select(p => p.BildDatei).FirstOrDefaultAsync(abbruch);
            if (datei is null || !File.Exists(ablage.VollerPfad(datei)))
                return Fehler.NichtGefunden("Planbild nicht gefunden.");
            http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(ablage.VollerPfad(datei), "image/png", enableRangeProcessing: true);
        });

        // Original-PDF zum Herunterladen
        gruppe.MapGet("/{id:int}/pdf", async (int id, NetzDbContext db, Planablage ablage, CancellationToken abbruch) =>
        {
            var plan = await db.Plaene.Where(p => p.Id == id)
                .Select(p => new { p.PdfDatei, p.Bezeichnung }).FirstOrDefaultAsync(abbruch);
            if (plan is null || !File.Exists(ablage.VollerPfad(plan.PdfDatei)))
                return Fehler.NichtGefunden("PDF nicht gefunden.");
            return Results.File(ablage.VollerPfad(plan.PdfDatei), "application/pdf", $"{plan.Bezeichnung}.pdf");
        });
    }

    /// <summary>
    /// Use Case «Masterplan festlegen», Teil 2: Nullpunkt und Massstab über zwei bekannte Strecken.
    /// Kann wiederholt werden; die bisherigen Messstrecken werden ersetzt.
    /// </summary>
    private static async Task<IResult> MasterKalibrieren(
        int id, MasterKalibrierungEingabe eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var plan = await db.Plaene.Include(p => p.Messstrecken).Include(p => p.Passpunkte)
            .FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan nicht gefunden.");
        if (!plan.IstMasterplan)
            return Fehler.Ungueltig("Nur der Masterplan wird über Nullpunkt und Strecken kalibriert.");

        if (eingabe.Nullpunkt is null) return Fehler.Ungueltig("Bitte den Nullpunkt setzen.");
        if (eingabe.Strecken is not { Count: 2 }) return Fehler.Ungueltig("Bitte zwei bekannte Strecken angeben.");

        if (!ImPlan(plan, eingabe.Nullpunkt.U, eingabe.Nullpunkt.V)) return Fehler.Ungueltig("Der Nullpunkt liegt ausserhalb des Plans.");

        var (strecken, streckenFehler) = StreckenPruefen(plan, eingabe.Strecken);
        if (streckenFehler is not null) return streckenFehler;

        var ergebnis = Geometrie.KalibriereMaster(new Planpunkt(eingabe.Nullpunkt.U, eingabe.Nullpunkt.V), strecken);

        // Optimistisches Sperren: mit der Version vergleichen, die der Browser geladen hatte
        db.Entry(plan).Property(p => p.Version).OriginalValue = eingabe.Version;

        plan.Massstab = ergebnis.Massstab;
        plan.Drehung = 0;
        plan.VersatzX = ergebnis.VersatzX;
        plan.VersatzY = ergebnis.VersatzY;
        plan.Referenziert = true;

        // Auch wenn sich die Werte nicht ändern (z.B. nur Messstrecken neu), muss der Plan
        // geschrieben werden – sonst prüft die Datenbank die Version nicht.
        db.Entry(plan).Property(p => p.Massstab).IsModified = true;

        MessstreckenErsetzen(db, plan, strecken);

        // Pläne, die auf den Masterplan referenziert sind, verschieben sich mit
        var plaene = await Referenzierung.PlaeneMitLageangaben(db, plan.StandortId, abbruch);
        Referenzierung.NachfolgerNeuBerechnen(plaene, plan);

        try
        {
            // SaveChanges speichert Plan, Messstrecken und abhängige Pläne gemeinsam in einer Transaktion
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Geaendert();
        }

        // Neue Version (xmin) aus der Datenbank holen
        await db.Entry(plan).ReloadAsync(abbruch);
        return Results.Ok(PlanDto.Aus(plan));
    }

    /// <summary>
    /// Use Case «Über Passpunkte referenzieren»: zwei Punkte, die im Plan und im Bezugsplan erkennbar
    /// sind, ergeben Massstab, Drehung und Versatz. Kann wiederholt werden (auch mit anderem Bezugsplan).
    /// Pläne, die auf diesen Plan referenziert sind, werden mitgeführt.
    /// </summary>
    private static async Task<IResult> Referenzieren(
        int id, ReferenzierungEingabe eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var plan = await db.Plaene.Include(p => p.Messstrecken).Include(p => p.Passpunkte)
            .FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan nicht gefunden.");
        if (plan.IstMasterplan) return Fehler.Ungueltig("Der Masterplan wird über Nullpunkt und Strecken kalibriert.");
        if (!plan.Aktiv) return Fehler.Ungueltig("Dieser Plan wurde ersetzt und kann nicht mehr referenziert werden.");

        var plaene = await Referenzierung.PlaeneMitLageangaben(db, plan.StandortId, abbruch);
        var bezug = plaene.FirstOrDefault(p => p.Id == eingabe.BezugsplanId);
        if (bezug is null) return Fehler.Ungueltig("Der Bezugsplan gehört nicht zu diesem Standort.");
        if (bezug.Id == plan.Id || Referenzierung.Nachfolger(plaene, plan.Id).Contains(bezug.Id))
            return Fehler.Ungueltig($"«{bezug.Bezeichnung}» ist selbst auf diesen Plan referenziert und kann nicht Bezugsplan sein.");
        if (!bezug.Referenziert)
            return Fehler.Ungueltig($"Der Bezugsplan «{bezug.Bezeichnung}» ist noch nicht referenziert.");

        if (eingabe.Passpunkte is not { Count: 2 }) return Fehler.Ungueltig("Bitte zwei Passpunkte setzen.");
        var pp = eingabe.Passpunkte;
        for (var i = 0; i < 2; i++)
        {
            if (!ImPlan(plan, pp[i].PlanU, pp[i].PlanV))
                return Fehler.Ungueltig($"Passpunkt {i + 1} liegt ausserhalb des Plans.");
            if (!ImPlan(bezug, pp[i].BezugU, pp[i].BezugV))
                return Fehler.Ungueltig($"Passpunkt {i + 1} liegt ausserhalb des Bezugsplans.");
        }
        if (Geometrie.Abstand(new(pp[0].PlanU, pp[0].PlanV), new(pp[1].PlanU, pp[1].PlanV)) < MinStreckePixel)
            return Fehler.Ungueltig("Die Passpunkte liegen im Plan zu nahe beieinander. Bitte zwei weit auseinanderliegende Punkte wählen.");
        if (Geometrie.Abstand(new(pp[0].BezugU, pp[0].BezugV), new(pp[1].BezugU, pp[1].BezugV)) < MinStreckePixel)
            return Fehler.Ungueltig("Die Passpunkte liegen im Bezugsplan zu nahe beieinander. Bitte zwei weit auseinanderliegende Punkte wählen.");

        db.Entry(plan).Property(p => p.Version).OriginalValue = eingabe.Version;

        db.Passpunkte.RemoveRange(plan.Passpunkte);
        plan.Passpunkte = pp.Select((p, i) => new Passpunkt
        {
            Nummer = i + 1, PlanX = p.PlanU, PlanY = p.PlanV, BezugX = p.BezugU, BezugY = p.BezugV
        }).ToList();
        plan.BezugsplanId = bezug.Id;
        plan.Bezugsplan = bezug;
        plan.Methode = Referenzierungsmethode.Passpunkte;
        // Angaben einer früheren Referenzierung mit eigenem Massstab gelten nicht mehr
        plan.EigenerMassstabDaten = null;
        db.Messstrecken.RemoveRange(plan.Messstrecken);
        plan.Messstrecken = [];
        Referenzierung.AusPasspunkten(plan, bezug);
        db.Entry(plan).Property(p => p.Massstab).IsModified = true; // Versionsprüfung erzwingen

        Referenzierung.NachfolgerNeuBerechnen(plaene, plan);

        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Geaendert();
        }

        await db.Entry(plan).ReloadAsync(abbruch);
        return Results.Ok(PlanDto.Aus(plan));
    }

    /// <summary>Bezeichnung und Stockwerk eines Plans ändern (Masterplan: Stockwerk bleibt 0).</summary>
    private static async Task<IResult> Aendern(int id, PlanAendern eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var plan = await db.Plaene.Include(p => p.Messstrecken).Include(p => p.Passpunkte)
            .FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan nicht gefunden.");

        var name = eingabe.Bezeichnung?.Trim() ?? "";
        if (name.Length is 0 or > 100) return Fehler.Ungueltig("Bitte eine Bezeichnung (höchstens 100 Zeichen) angeben.");
        if (plan.IstMasterplan && eingabe.Stockwerk != 0)
            return Fehler.Ungueltig("Der Masterplan liegt immer auf Stockwerk 0.");
        if (eingabe.Stockwerk is < PositionEndpunkte.MinStockwerk or > PositionEndpunkte.MaxStockwerk)
            return Fehler.Ungueltig($"Das Stockwerk muss zwischen {PositionEndpunkte.MinStockwerk} und {PositionEndpunkte.MaxStockwerk} liegen.");
        if (await db.Plaene.AnyAsync(p => p.StandortId == plan.StandortId && p.Aktiv && p.Id != id && p.Bezeichnung == name, abbruch))
            return Fehler.Konflikt($"Es gibt bereits einen Plan «{name}».");

        db.Entry(plan).Property(p => p.Version).OriginalValue = eingabe.Version;
        plan.Bezeichnung = name;
        plan.Stockwerk = eingabe.Stockwerk;
        db.Entry(plan).Property(p => p.Bezeichnung).IsModified = true;

        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Geaendert();
        }
        await db.Entry(plan).ReloadAsync(abbruch);
        return Results.Ok(PlanDto.Aus(plan));
    }

    /// <summary>
    /// Plan löschen (z.B. falsches PDF importiert). Positionen bleiben erhalten, sie hängen an
    /// absoluten Koordinaten, nicht am Plan. Gesperrt, solange andere Pläne darauf referenziert sind.
    /// </summary>
    private static async Task<IResult> Loeschen(int id, uint version, NetzDbContext db, Planablage ablage, CancellationToken abbruch)
    {
        var plan = await db.Plaene.FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan nicht gefunden.");
        if (plan.IstMasterplan) return Fehler.Ungueltig("Der Masterplan kann nicht gelöscht werden.");

        var abhaengig = await db.Plaene.Where(p => p.BezugsplanId == id || p.ErsetztPlanId == id)
            .Select(p => p.Bezeichnung).ToListAsync(abbruch);
        if (abhaengig.Count > 0)
            return Fehler.Konflikt(
                $"Auf diesen Plan sind noch referenziert: {string.Join(", ", abhaengig.Select(b => $"«{b}»"))}. " +
                "Bitte diese zuerst auf einen anderen Plan referenzieren oder löschen.");

        db.Entry(plan).Property(p => p.Version).OriginalValue = version;
        db.Plaene.Remove(plan);
        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Geaendert();
        }

        ablage.Loeschen(plan.PdfDatei);
        ablage.Loeschen(plan.BildDatei);
        return Results.NoContent();
    }

    private static bool ImPlan(Plan plan, double u, double v) =>
        u >= 0 && v >= 0 && u <= plan.BildBreite && v <= plan.BildHoehe;

    /// <summary>
    /// Use Case «Mit eigenem Massstab referenzieren»: für Pläne, die auf keinem Bezugsplan erkennbar sind.
    /// Massstab über bekannte Strecken im Plan, Lage über einen Ankerpunkt (im Bezugsplan angeklickt
    /// oder als Koordinate in Metern), Drehung als Winkel oder als Richtung (z.B. Nordpfeile).
    /// </summary>
    private static async Task<IResult> EigenerMassstab(
        int id, EigenerMassstabEingabe eingabe, NetzDbContext db, CancellationToken abbruch)
    {
        var plan = await db.Plaene.Include(p => p.Messstrecken).Include(p => p.Passpunkte)
            .FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (plan is null) return Fehler.NichtGefunden("Plan nicht gefunden.");
        if (plan.IstMasterplan) return Fehler.Ungueltig("Der Masterplan wird über Nullpunkt und Strecken kalibriert.");
        if (!plan.Aktiv) return Fehler.Ungueltig("Dieser Plan wurde ersetzt und kann nicht mehr referenziert werden.");

        var plaene = await Referenzierung.PlaeneMitLageangaben(db, plan.StandortId, abbruch);
        var bezug = plaene.FirstOrDefault(p => p.Id == eingabe.BezugsplanId);
        if (bezug is null) return Fehler.Ungueltig("Der Bezugsplan gehört nicht zu diesem Standort.");
        if (bezug.Id == plan.Id || Referenzierung.Nachfolger(plaene, plan.Id).Contains(bezug.Id))
            return Fehler.Ungueltig($"«{bezug.Bezeichnung}» ist selbst auf diesen Plan referenziert und kann nicht Bezugsplan sein.");
        if (!bezug.Referenziert)
            return Fehler.Ungueltig($"Der Bezugsplan «{bezug.Bezeichnung}» ist noch nicht referenziert.");

        // Massstab
        if (eingabe.Strecken is not { Count: 1 or 2 }) return Fehler.Ungueltig("Bitte eine oder zwei bekannte Strecken angeben.");
        var (strecken, streckenFehler) = StreckenPruefen(plan, eingabe.Strecken);
        if (streckenFehler is not null) return streckenFehler;

        // Lage
        if (eingabe.Anker is null) return Fehler.Ungueltig("Bitte den Ankerpunkt im Plan setzen.");
        if (!ImPlan(plan, eingabe.Anker.U, eingabe.Anker.V)) return Fehler.Ungueltig("Der Ankerpunkt liegt ausserhalb des Plans.");
        var mitKoordinate = eingabe.AnkerX is not null || eingabe.AnkerY is not null;
        if (mitKoordinate == (eingabe.AnkerBezug is not null))
            return Fehler.Ungueltig("Bitte die Lage des Ankerpunkts entweder im Bezugsplan anklicken oder als Koordinate angeben.");
        Weltpunkt? ankerAbsolut = null;
        if (mitKoordinate)
        {
            if (eingabe.AnkerX is not { } x || eingabe.AnkerY is not { } y || !double.IsFinite(x) || !double.IsFinite(y)
                || Math.Abs(x) > 100_000 || Math.Abs(y) > 100_000)
                return Fehler.Ungueltig("Bitte für den Ankerpunkt x und y in Metern angeben (höchstens ±100 000 m).");
            ankerAbsolut = new Weltpunkt(x, y);
        }
        else if (!ImPlan(bezug, eingabe.AnkerBezug!.U, eingabe.AnkerBezug.V))
            return Fehler.Ungueltig("Der Ankerpunkt liegt ausserhalb des Bezugsplans.");

        // Drehung
        var mitRichtung = eingabe.RichtungPlan is not null || eingabe.RichtungBezug is not null;
        if (mitRichtung == (eingabe.Winkel is not null))
            return Fehler.Ungueltig("Bitte die Drehung entweder als Winkel oder als Richtung angeben.");
        if (eingabe.Winkel is { } w && (!double.IsFinite(w) || Math.Abs(w) > 360))
            return Fehler.Ungueltig("Bitte einen Winkel zwischen −360° und 360° angeben.");
        if (mitRichtung)
        {
            if (eingabe.RichtungPlan is not { } rp || eingabe.RichtungBezug is not { } rb)
                return Fehler.Ungueltig("Bitte die Richtung im Plan und im Bezugsplan angeben.");
            if (!ImPlan(plan, rp.U1, rp.V1) || !ImPlan(plan, rp.U2, rp.V2))
                return Fehler.Ungueltig("Die Richtung liegt nicht vollständig im Plan.");
            if (!ImPlan(bezug, rb.U1, rb.V1) || !ImPlan(bezug, rb.U2, rb.V2))
                return Fehler.Ungueltig("Die Richtung liegt nicht vollständig im Bezugsplan.");
            if (Geometrie.Abstand(new(rp.U1, rp.V1), new(rp.U2, rp.V2)) < MinStreckePixel
                || Geometrie.Abstand(new(rb.U1, rb.V1), new(rb.U2, rb.V2)) < MinStreckePixel)
                return Fehler.Ungueltig("Die Richtungslinie ist zu kurz. Bitte eine längere Linie wählen.");
        }

        var daten = new EigenerMassstabDaten(
            new Planpunkt(eingabe.Anker.U, eingabe.Anker.V),
            eingabe.AnkerBezug is { } ab ? new Planpunkt(ab.U, ab.V) : null,
            ankerAbsolut,
            eingabe.Winkel,
            eingabe.RichtungPlan is { } r1 ? new Richtungslinie(new(r1.U1, r1.V1), new(r1.U2, r1.V2)) : null,
            eingabe.RichtungBezug is { } r2 ? new Richtungslinie(new(r2.U1, r2.V1), new(r2.U2, r2.V2)) : null);

        db.Entry(plan).Property(p => p.Version).OriginalValue = eingabe.Version;
        db.Passpunkte.RemoveRange(plan.Passpunkte);
        plan.Passpunkte = [];
        MessstreckenErsetzen(db, plan, strecken);
        plan.EigenerMassstabDaten = daten.AlsJson();
        plan.BezugsplanId = bezug.Id;
        plan.Bezugsplan = bezug;
        plan.Methode = Referenzierungsmethode.EigenerMassstab;
        Referenzierung.AusEigenemMassstab(plan, bezug);
        db.Entry(plan).Property(p => p.Massstab).IsModified = true; // Versionsprüfung erzwingen

        Referenzierung.NachfolgerNeuBerechnen(plaene, plan);

        try
        {
            await db.SaveChangesAsync(abbruch);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fehler.Geaendert();
        }

        await db.Entry(plan).ReloadAsync(abbruch);
        return Results.Ok(PlanDto.Aus(plan));
    }

    /// <summary>Bekannte Strecken prüfen: im Plan, Länge 0–100 000 m, mindestens <see cref="MinStreckePixel"/> lang.</summary>
    private static (List<BekannteStrecke> Strecken, IResult? Fehler) StreckenPruefen(Plan plan, List<StreckeEingabe> eingabe)
    {
        var strecken = new List<BekannteStrecke>();
        for (var i = 0; i < eingabe.Count; i++)
        {
            var s = eingabe[i];
            var nr = i + 1;
            if (!ImPlan(plan, s.U1, s.V1) || !ImPlan(plan, s.U2, s.V2))
                return ([], Fehler.Ungueltig($"Strecke {nr} liegt nicht vollständig im Plan."));
            if (!(s.LaengeMeter > 0 && s.LaengeMeter <= 100_000))
                return ([], Fehler.Ungueltig($"Bitte für Strecke {nr} eine Länge zwischen 0 und 100 000 m angeben."));
            var strecke = new BekannteStrecke(new Planpunkt(s.U1, s.V1), new Planpunkt(s.U2, s.V2), s.LaengeMeter);
            if (strecke.PixelLaenge < MinStreckePixel)
                return ([], Fehler.Ungueltig($"Strecke {nr} ist zu kurz. Bitte eine möglichst lange bekannte Strecke wählen."));
            strecken.Add(strecke);
        }
        return (strecken, null);
    }

    private static void MessstreckenErsetzen(NetzDbContext db, Plan plan, List<BekannteStrecke> strecken)
    {
        db.Messstrecken.RemoveRange(plan.Messstrecken);
        plan.Messstrecken = strecken.Select((s, i) => new Messstrecke
        {
            Nummer = i + 1,
            U1 = s.Anfang.U, V1 = s.Anfang.V, U2 = s.Ende.U, V2 = s.Ende.V,
            LaengeMeter = s.LaengeMeter
        }).ToList();
    }
}
