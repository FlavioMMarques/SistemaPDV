using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGrupos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Grupos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grupos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Grupos_IdExterno",
                table: "Grupos",
                column: "IdExterno",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Grupos");
        }
    }
}
