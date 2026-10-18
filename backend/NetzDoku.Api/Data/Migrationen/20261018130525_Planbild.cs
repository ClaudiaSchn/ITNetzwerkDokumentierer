using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetzDoku.Api.Data.Migrationen
{
    /// <inheritdoc />
    public partial class Planbild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "bild_breite",
                table: "plaene",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "bild_datei",
                table: "plaene",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bild_hoehe",
                table: "plaene",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bild_breite",
                table: "plaene");

            migrationBuilder.DropColumn(
                name: "bild_datei",
                table: "plaene");

            migrationBuilder.DropColumn(
                name: "bild_hoehe",
                table: "plaene");
        }
    }
}
