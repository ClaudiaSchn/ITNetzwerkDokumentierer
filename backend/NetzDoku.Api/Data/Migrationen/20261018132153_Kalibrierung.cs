using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NetzDoku.Api.Data.Migrationen
{
    /// <inheritdoc />
    public partial class Kalibrierung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "referenziert",
                table: "plaene",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "messstrecken",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    nummer = table.Column<int>(type: "integer", nullable: false),
                    u1 = table.Column<double>(type: "double precision", nullable: false),
                    v1 = table.Column<double>(type: "double precision", nullable: false),
                    u2 = table.Column<double>(type: "double precision", nullable: false),
                    v2 = table.Column<double>(type: "double precision", nullable: false),
                    laenge_meter = table.Column<double>(type: "double precision", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messstrecken", x => x.id);
                    table.CheckConstraint("ck_messstrecken_laenge_positiv", "laenge_meter > 0");
                    table.CheckConstraint("ck_messstrecken_nummer_positiv", "nummer >= 1");
                    table.ForeignKey(
                        name: "fk_messstrecken_plaene_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plaene",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messstrecken_plan_id_nummer",
                table: "messstrecken",
                columns: new[] { "plan_id", "nummer" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messstrecken");

            migrationBuilder.DropColumn(
                name: "referenziert",
                table: "plaene");
        }
    }
}
