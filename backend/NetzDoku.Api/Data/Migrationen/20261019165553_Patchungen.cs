using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetzDoku.Api.Data.Migrationen
{
    /// <inheritdoc />
    public partial class Patchungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patchungen",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    position_id = table.Column<int>(type: "integer", nullable: false),
                    bezeichnung = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patchungen", x => x.id);
                    table.ForeignKey(
                        name: "fk_patchungen_positionen_position_id",
                        column: x => x.position_id,
                        principalTable: "positionen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patchenden",
                columns: table => new
                {
                    patchung_id = table.Column<int>(type: "integer", nullable: false),
                    nummer = table.Column<int>(type: "integer", nullable: false),
                    port_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patchenden", x => new { x.patchung_id, x.nummer });
                    table.CheckConstraint("ck_patchenden_nummer", "nummer IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_patchenden_patchungen_patchung_id",
                        column: x => x.patchung_id,
                        principalTable: "patchungen",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_patchenden_ports_port_id",
                        column: x => x.port_id,
                        principalTable: "ports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patchenden_port_id",
                table: "patchenden",
                column: "port_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patchungen_position_id",
                table: "patchungen",
                column: "position_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patchenden");

            migrationBuilder.DropTable(
                name: "patchungen");
        }
    }
}
