using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetzDoku.Api.Data.Migrationen
{
    /// <inheritdoc />
    public partial class EigenerMassstab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "eigener_massstab_daten",
                table: "plaene",
                type: "jsonb",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "eigener_massstab_daten",
                table: "plaene");
        }
    }
}
