using Microsoft.EntityFrameworkCore;
using NetzDoku.Api.Modell;

namespace NetzDoku.Api.Data;

/// <summary>
/// Datenbankzugriff nach dem Klassendiagramm docs/diagramme/kd_01_datenmodell.puml.
/// Regeln, die eine einzelne Zeile oder Eindeutigkeit betreffen, sind hier als
/// Constraints hinterlegt und werden von PostgreSQL durchgesetzt. Regeln über mehrere
/// Tabellen hinweg prüft das Backend (siehe docs/Datenbank.md).
/// </summary>
public class NetzDbContext(DbContextOptions<NetzDbContext> optionen) : DbContext(optionen)
{
    // Pläne
    public DbSet<Standort> Standorte => Set<Standort>();
    public DbSet<Plan> Plaene => Set<Plan>();
    public DbSet<Passpunkt> Passpunkte => Set<Passpunkt>();
    public DbSet<Messstrecke> Messstrecken => Set<Messstrecke>();

    // Topologie
    public DbSet<Position> Positionen => Set<Position>();
    public DbSet<Verbindung> Verbindungen => Set<Verbindung>();
    public DbSet<Kommentar> Kommentare => Set<Kommentar>();

    // Infrastruktur
    public DbSet<Geraet> Geraete => Set<Geraet>();
    public DbSet<Port> Ports => Set<Port>();
    public DbSet<Medium> Medien => Set<Medium>();
    public DbSet<Anschluss> Anschluesse => Set<Anschluss>();

    // Stammdaten
    public DbSet<Positionstyp> Positionstypen => Set<Positionstyp>();
    public DbSet<Portart> Portarten => Set<Portart>();
    public DbSet<Medientyp> Medientypen => Set<Medientyp>();
    public DbSet<Geraetetyp> Geraetetypen => Set<Geraetetyp>();
    public DbSet<Portvorlage> Portvorlagen => Set<Portvorlage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder konventionen)
    {
        // Aufzählungen als lesbarer Text speichern ('Master', 'Vorne', …)
        konventionen.Properties<Referenzierungsmethode>().HaveConversion<string>().HaveMaxLength(20);
        konventionen.Properties<Kommentarstatus>().HaveConversion<string>().HaveMaxLength(20);
        konventionen.Properties<Geraetekategorie>().HaveConversion<string>().HaveMaxLength(20);
        konventionen.Properties<Portseite>().HaveConversion<string>().HaveMaxLength(20);
        konventionen.Properties<Mediumende>().HaveConversion<string>().HaveMaxLength(1);

        // Standardlänge für Texte, wo nichts anderes angegeben ist
        konventionen.Properties<string>().HaveMaxLength(100);
    }

    protected override void OnModelCreating(ModelBuilder modell)
    {
        // Optimistisches Sperren über die Systemspalte xmin
        foreach (var typ in modell.Model.GetEntityTypes().Where(t => typeof(Entitaet).IsAssignableFrom(t.ClrType)))
        {
            modell.Entity(typ.ClrType).Property(nameof(Entitaet.Version)).IsRowVersion();
        }

        KonfigurierePlaene(modell);
        KonfiguriereTopologie(modell);
        KonfiguriereInfrastruktur(modell);
        KonfiguriereStammdaten(modell);
    }

    private static void KonfigurierePlaene(ModelBuilder modell)
    {
        modell.Entity<Standort>(e =>
        {
            e.HasIndex(s => s.Name).IsUnique();
        });

        modell.Entity<Plan>(e =>
        {
            e.Property(p => p.PdfDatei).HasMaxLength(500);
            e.Property(p => p.BildDatei).HasMaxLength(500);

            e.HasOne(p => p.Standort).WithMany(s => s.Plaene)
                .HasForeignKey(p => p.StandortId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.Bezugsplan).WithMany()
                .HasForeignKey(p => p.BezugsplanId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.ErsetztPlan).WithOne()
                .HasForeignKey<Plan>(p => p.ErsetztPlanId).OnDelete(DeleteBehavior.Restrict);

            // Regel 1: pro Standort genau ein aktiver Masterplan
            e.HasIndex(p => p.StandortId)
                .HasDatabaseName("ux_plaene_ein_masterplan_pro_standort")
                .IsUnique()
                .HasFilter("ist_masterplan AND aktiv");

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_plaene_massstab_positiv", "massstab > 0");
                // Regel 1: Masterplan hat Stockwerk 0, keinen Bezugsplan, Methode Master
                t.HasCheckConstraint("ck_plaene_masterplan",
                    "NOT ist_masterplan OR (stockwerk = 0 AND bezugsplan_id IS NULL)");
                t.HasCheckConstraint("ck_plaene_methode_passt",
                    "(methode = 'Master') = ist_masterplan");
                // Jeder andere Plan braucht einen Bezugsplan
                t.HasCheckConstraint("ck_plaene_bezugsplan_noetig",
                    "ist_masterplan OR bezugsplan_id IS NOT NULL");
                t.HasCheckConstraint("ck_plaene_nicht_auf_sich_selbst",
                    "bezugsplan_id <> id AND ersetzt_plan_id <> id");
            });
        });

        modell.Entity<Messstrecke>(e =>
        {
            e.HasOne(m => m.Plan).WithMany(p => p.Messstrecken)
                .HasForeignKey(m => m.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.PlanId, m.Nummer }).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_messstrecken_nummer_positiv", "nummer >= 1");
                t.HasCheckConstraint("ck_messstrecken_laenge_positiv", "laenge_meter > 0");
            });
        });

        modell.Entity<Passpunkt>(e =>
        {
            e.HasOne(p => p.Plan).WithMany(p => p.Passpunkte)
                .HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.PlanId, p.Nummer }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_passpunkte_nummer", "nummer IN (1, 2)"));
        });
    }

    private static void KonfiguriereTopologie(ModelBuilder modell)
    {
        modell.Entity<Position>(e =>
        {
            e.HasOne(p => p.Standort).WithMany(s => s.Positionen)
                .HasForeignKey(p => p.StandortId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Positionstyp).WithMany()
                .HasForeignKey(p => p.PositionstypId).OnDelete(DeleteBehavior.Restrict);

            // Regel 4: Bezeichnung eindeutig pro Standort
            e.HasIndex(p => new { p.StandortId, p.Bezeichnung }).IsUnique();
        });

        modell.Entity<Verbindung>(e =>
        {
            e.HasOne(v => v.Standort).WithMany()
                .HasForeignKey(v => v.StandortId).OnDelete(DeleteBehavior.Restrict);
            // Regel 10: Position nicht löschbar, solange Verbindungen daran hängen
            e.HasOne(v => v.PositionA).WithMany()
                .HasForeignKey(v => v.PositionAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.PositionB).WithMany()
                .HasForeignKey(v => v.PositionBId).OnDelete(DeleteBehavior.Restrict);

            // Regel 5: zwei verschiedene Positionen.
            // Eindeutigkeit des Paares (A,B) = (B,A) siehe Index in der Migration.
            e.ToTable(t => t.HasCheckConstraint("ck_verbindungen_zwei_positionen",
                "position_a_id <> position_b_id"));
        });

        modell.Entity<Kommentar>(e =>
        {
            e.Property(k => k.Text).HasMaxLength(2000);
            e.Property(k => k.ErstelltAm).HasDefaultValueSql("now()");
            e.Property(k => k.Status).HasDefaultValueSql("'Offen'");
            e.HasOne(k => k.Position).WithMany(p => p.Kommentare)
                .HasForeignKey(k => k.PositionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(k => k.Status);
        });
    }

    private static void KonfiguriereInfrastruktur(ModelBuilder modell)
    {
        modell.Entity<Geraet>(e =>
        {
            // Regel 10: Position nicht löschbar, solange Geräte daran hängen
            e.HasOne(g => g.Position).WithMany(p => p.Geraete)
                .HasForeignKey(g => g.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.Geraetetyp).WithMany()
                .HasForeignKey(g => g.GeraetetypId).OnDelete(DeleteBehavior.Restrict);
        });

        modell.Entity<Port>(e =>
        {
            e.HasOne(p => p.Geraet).WithMany(g => g.Ports)
                .HasForeignKey(p => p.GeraetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Portart).WithMany()
                .HasForeignKey(p => p.PortartId).OnDelete(DeleteBehavior.Restrict);

            // Regel 9: pro Gerät, Nummer und Seite genau ein Port
            e.HasIndex(p => new { p.GeraetId, p.Nummer, p.Seite }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_ports_nummer_positiv", "nummer >= 1"));
        });

        modell.Entity<Medium>(e =>
        {
            e.Property(m => m.Kabelnummer).HasMaxLength(50);
            e.HasOne(m => m.Verbindung).WithMany(v => v.Medien)
                .HasForeignKey(m => m.VerbindungId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Standort).WithMany()
                .HasForeignKey(m => m.StandortId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Medientyp).WithMany()
                .HasForeignKey(m => m.MedientypId).OnDelete(DeleteBehavior.Restrict);

            // Regel 4: Kabelnummer eindeutig pro Standort
            e.HasIndex(m => new { m.StandortId, m.Kabelnummer }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_medien_anzahl_positiv", "anzahl_anschluesse >= 1"));
        });

        modell.Entity<Anschluss>(e =>
        {
            e.HasOne(a => a.Medium).WithMany(m => m.Anschluesse)
                .HasForeignKey(a => a.MediumId).OnDelete(DeleteBehavior.Cascade);

            // Regel 8: ein Port ist mit höchstens einem Anschluss belegt.
            // Regel 10: Gerät nicht löschbar, solange einer seiner Ports belegt ist.
            e.HasOne(a => a.Port).WithOne(p => p.Anschluss)
                .HasForeignKey<Anschluss>(a => a.PortId).OnDelete(DeleteBehavior.Restrict);

            // Regel 6: pro Medium, Ende und Nummer genau ein Anschluss
            e.HasIndex(a => new { a.MediumId, a.Ende, a.Nummer }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_anschluesse_nummer_positiv", "nummer >= 1"));
        });
    }

    private static void KonfiguriereStammdaten(ModelBuilder modell)
    {
        modell.Entity<Positionstyp>(e => e.HasIndex(t => t.Name).IsUnique());
        modell.Entity<Portart>(e => e.HasIndex(t => t.Name).IsUnique());

        modell.Entity<Medientyp>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.HasMany(t => t.ErlaubtePortarten).WithMany()
                .UsingEntity("medientyp_erlaubte_portarten");
        });

        modell.Entity<Geraetetyp>(e => e.HasIndex(t => t.Name).IsUnique());

        modell.Entity<Portvorlage>(e =>
        {
            e.HasOne(v => v.Geraetetyp).WithMany(t => t.Portvorlagen)
                .HasForeignKey(v => v.GeraetetypId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Portart).WithMany()
                .HasForeignKey(v => v.PortartId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.GeraetetypId, v.Nummer, v.Seite }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_portvorlagen_nummer_positiv", "nummer >= 1"));
        });
    }
}
