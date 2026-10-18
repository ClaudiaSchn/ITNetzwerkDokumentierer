using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetzDoku.Api.Data.Migrationen
{
    /// <inheritdoc />
    public partial class Anfangsschema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "geraetetypen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kategorie = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geraetetypen", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "medientypen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medientypen", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portarten",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portarten", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "positionstypen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positionstypen", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "standorte",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standorte", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "medientyp_erlaubte_portarten",
                columns: table => new
                {
                    erlaubte_portarten_id = table.Column<int>(type: "integer", nullable: false),
                    medientyp_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medientyp_erlaubte_portarten", x => new { x.erlaubte_portarten_id, x.medientyp_id });
                    table.ForeignKey(
                        name: "fk_medientyp_erlaubte_portarten_medientypen_medientyp_id",
                        column: x => x.medientyp_id,
                        principalTable: "medientypen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_medientyp_erlaubte_portarten_portarten_erlaubte_portarten_id",
                        column: x => x.erlaubte_portarten_id,
                        principalTable: "portarten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "portvorlagen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    geraetetyp_id = table.Column<int>(type: "integer", nullable: false),
                    nummer = table.Column<int>(type: "integer", nullable: false),
                    seite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    portart_id = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portvorlagen", x => x.id);
                    table.CheckConstraint("ck_portvorlagen_nummer_positiv", "nummer >= 1");
                    table.ForeignKey(
                        name: "fk_portvorlagen_geraetetypen_geraetetyp_id",
                        column: x => x.geraetetyp_id,
                        principalTable: "geraetetypen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_portvorlagen_portarten_portart_id",
                        column: x => x.portart_id,
                        principalTable: "portarten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plaene",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    standort_id = table.Column<int>(type: "integer", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pdf_datei = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stockwerk = table.Column<int>(type: "integer", nullable: false),
                    ist_masterplan = table.Column<bool>(type: "boolean", nullable: false),
                    methode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    massstab = table.Column<double>(type: "double precision", nullable: false),
                    drehung = table.Column<double>(type: "double precision", nullable: false),
                    versatz_x = table.Column<double>(type: "double precision", nullable: false),
                    versatz_y = table.Column<double>(type: "double precision", nullable: false),
                    aktiv = table.Column<bool>(type: "boolean", nullable: false),
                    bezugsplan_id = table.Column<int>(type: "integer", nullable: true),
                    ersetzt_plan_id = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plaene", x => x.id);
                    table.CheckConstraint("ck_plaene_bezugsplan_noetig", "ist_masterplan OR bezugsplan_id IS NOT NULL");
                    table.CheckConstraint("ck_plaene_massstab_positiv", "massstab > 0");
                    table.CheckConstraint("ck_plaene_masterplan", "NOT ist_masterplan OR (stockwerk = 0 AND bezugsplan_id IS NULL)");
                    table.CheckConstraint("ck_plaene_methode_passt", "(methode = 'Master') = ist_masterplan");
                    table.CheckConstraint("ck_plaene_nicht_auf_sich_selbst", "bezugsplan_id <> id AND ersetzt_plan_id <> id");
                    table.ForeignKey(
                        name: "fk_plaene_plaene_bezugsplan_id",
                        column: x => x.bezugsplan_id,
                        principalTable: "plaene",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plaene_plaene_ersetzt_plan_id",
                        column: x => x.ersetzt_plan_id,
                        principalTable: "plaene",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plaene_standorte_standort_id",
                        column: x => x.standort_id,
                        principalTable: "standorte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positionen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    standort_id = table.Column<int>(type: "integer", nullable: false),
                    positionstyp_id = table.Column<int>(type: "integer", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    x = table.Column<double>(type: "double precision", nullable: false),
                    y = table.Column<double>(type: "double precision", nullable: false),
                    stockwerk = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positionen", x => x.id);
                    table.ForeignKey(
                        name: "fk_positionen_positionstypen_positionstyp_id",
                        column: x => x.positionstyp_id,
                        principalTable: "positionstypen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positionen_standorte_standort_id",
                        column: x => x.standort_id,
                        principalTable: "standorte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "passpunkte",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    nummer = table.Column<int>(type: "integer", nullable: false),
                    plan_x = table.Column<double>(type: "double precision", nullable: false),
                    plan_y = table.Column<double>(type: "double precision", nullable: false),
                    bezug_x = table.Column<double>(type: "double precision", nullable: false),
                    bezug_y = table.Column<double>(type: "double precision", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passpunkte", x => x.id);
                    table.CheckConstraint("ck_passpunkte_nummer", "nummer IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_passpunkte_plaene_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plaene",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "geraete",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    position_id = table.Column<int>(type: "integer", nullable: false),
                    geraetetyp_id = table.Column<int>(type: "integer", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geraete", x => x.id);
                    table.ForeignKey(
                        name: "fk_geraete_geraetetypen_geraetetyp_id",
                        column: x => x.geraetetyp_id,
                        principalTable: "geraetetypen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_geraete_positionen_position_id",
                        column: x => x.position_id,
                        principalTable: "positionen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kommentare",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    position_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    verfasser = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'Offen'"),
                    erstellt_am = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kommentare", x => x.id);
                    table.ForeignKey(
                        name: "fk_kommentare_positionen_position_id",
                        column: x => x.position_id,
                        principalTable: "positionen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verbindungen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    standort_id = table.Column<int>(type: "integer", nullable: false),
                    position_a_id = table.Column<int>(type: "integer", nullable: false),
                    position_b_id = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verbindungen", x => x.id);
                    table.CheckConstraint("ck_verbindungen_zwei_positionen", "position_a_id <> position_b_id");
                    table.ForeignKey(
                        name: "fk_verbindungen_positionen_position_a_id",
                        column: x => x.position_a_id,
                        principalTable: "positionen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verbindungen_positionen_position_b_id",
                        column: x => x.position_b_id,
                        principalTable: "positionen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verbindungen_standorte_standort_id",
                        column: x => x.standort_id,
                        principalTable: "standorte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ports",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    geraet_id = table.Column<int>(type: "integer", nullable: false),
                    nummer = table.Column<int>(type: "integer", nullable: false),
                    seite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    portart_id = table.Column<int>(type: "integer", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ports", x => x.id);
                    table.CheckConstraint("ck_ports_nummer_positiv", "nummer >= 1");
                    table.ForeignKey(
                        name: "fk_ports_geraete_geraet_id",
                        column: x => x.geraet_id,
                        principalTable: "geraete",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ports_portarten_portart_id",
                        column: x => x.portart_id,
                        principalTable: "portarten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medien",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    verbindung_id = table.Column<int>(type: "integer", nullable: false),
                    standort_id = table.Column<int>(type: "integer", nullable: false),
                    medientyp_id = table.Column<int>(type: "integer", nullable: false),
                    kabelnummer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    anzahl_anschluesse = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medien", x => x.id);
                    table.CheckConstraint("ck_medien_anzahl_positiv", "anzahl_anschluesse >= 1");
                    table.ForeignKey(
                        name: "fk_medien_medientypen_medientyp_id",
                        column: x => x.medientyp_id,
                        principalTable: "medientypen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medien_standorte_standort_id",
                        column: x => x.standort_id,
                        principalTable: "standorte",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medien_verbindungen_verbindung_id",
                        column: x => x.verbindung_id,
                        principalTable: "verbindungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "anschluesse",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    medium_id = table.Column<int>(type: "integer", nullable: false),
                    ende = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    nummer = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anschluesse", x => x.id);
                    table.CheckConstraint("ck_anschluesse_nummer_positiv", "nummer >= 1");
                    table.ForeignKey(
                        name: "fk_anschluesse_medien_medium_id",
                        column: x => x.medium_id,
                        principalTable: "medien",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_anschluesse_ports_port_id",
                        column: x => x.port_id,
                        principalTable: "ports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anschluesse_medium_id_ende_nummer",
                table: "anschluesse",
                columns: new[] { "medium_id", "ende", "nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_anschluesse_port_id",
                table: "anschluesse",
                column: "port_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_geraete_geraetetyp_id",
                table: "geraete",
                column: "geraetetyp_id");

            migrationBuilder.CreateIndex(
                name: "ix_geraete_position_id",
                table: "geraete",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_geraetetypen_name",
                table: "geraetetypen",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kommentare_position_id",
                table: "kommentare",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_kommentare_status",
                table: "kommentare",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_medien_medientyp_id",
                table: "medien",
                column: "medientyp_id");

            migrationBuilder.CreateIndex(
                name: "ix_medien_standort_id_kabelnummer",
                table: "medien",
                columns: new[] { "standort_id", "kabelnummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_medien_verbindung_id",
                table: "medien",
                column: "verbindung_id");

            migrationBuilder.CreateIndex(
                name: "ix_medientyp_erlaubte_portarten_medientyp_id",
                table: "medientyp_erlaubte_portarten",
                column: "medientyp_id");

            migrationBuilder.CreateIndex(
                name: "ix_medientypen_name",
                table: "medientypen",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_passpunkte_plan_id_nummer",
                table: "passpunkte",
                columns: new[] { "plan_id", "nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plaene_bezugsplan_id",
                table: "plaene",
                column: "bezugsplan_id");

            migrationBuilder.CreateIndex(
                name: "ix_plaene_ersetzt_plan_id",
                table: "plaene",
                column: "ersetzt_plan_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plaene_ein_masterplan_pro_standort",
                table: "plaene",
                column: "standort_id",
                unique: true,
                filter: "ist_masterplan AND aktiv");

            migrationBuilder.CreateIndex(
                name: "ix_portarten_name",
                table: "portarten",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ports_geraet_id_nummer_seite",
                table: "ports",
                columns: new[] { "geraet_id", "nummer", "seite" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ports_portart_id",
                table: "ports",
                column: "portart_id");

            migrationBuilder.CreateIndex(
                name: "ix_portvorlagen_geraetetyp_id_nummer_seite",
                table: "portvorlagen",
                columns: new[] { "geraetetyp_id", "nummer", "seite" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portvorlagen_portart_id",
                table: "portvorlagen",
                column: "portart_id");

            migrationBuilder.CreateIndex(
                name: "ix_positionen_positionstyp_id",
                table: "positionen",
                column: "positionstyp_id");

            migrationBuilder.CreateIndex(
                name: "ix_positionen_standort_id_bezeichnung",
                table: "positionen",
                columns: new[] { "standort_id", "bezeichnung" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positionstypen_name",
                table: "positionstypen",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_standorte_name",
                table: "standorte",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_verbindungen_position_a_id",
                table: "verbindungen",
                column: "position_a_id");

            migrationBuilder.CreateIndex(
                name: "ix_verbindungen_position_b_id",
                table: "verbindungen",
                column: "position_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_verbindungen_standort_id",
                table: "verbindungen",
                column: "standort_id");

            // Von Hand ergänzt: höchstens eine Verbindung pro Positionspaar,
            // unabhängig von der Richtung (A–B gilt gleich wie B–A).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_verbindungen_positionspaar ON verbindungen " +
                "(LEAST(position_a_id, position_b_id), GREATEST(position_a_id, position_b_id));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_verbindungen_positionspaar;");

            migrationBuilder.DropTable(
                name: "anschluesse");

            migrationBuilder.DropTable(
                name: "kommentare");

            migrationBuilder.DropTable(
                name: "medientyp_erlaubte_portarten");

            migrationBuilder.DropTable(
                name: "passpunkte");

            migrationBuilder.DropTable(
                name: "portvorlagen");

            migrationBuilder.DropTable(
                name: "medien");

            migrationBuilder.DropTable(
                name: "ports");

            migrationBuilder.DropTable(
                name: "plaene");

            migrationBuilder.DropTable(
                name: "medientypen");

            migrationBuilder.DropTable(
                name: "verbindungen");

            migrationBuilder.DropTable(
                name: "geraete");

            migrationBuilder.DropTable(
                name: "portarten");

            migrationBuilder.DropTable(
                name: "geraetetypen");

            migrationBuilder.DropTable(
                name: "positionen");

            migrationBuilder.DropTable(
                name: "positionstypen");

            migrationBuilder.DropTable(
                name: "standorte");
        }
    }
}
