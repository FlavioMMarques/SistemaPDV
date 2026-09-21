using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCartoesEBandeiraNoPagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bandeira",
                table: "PagamentosVenda",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cartoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Credenciadora = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BandeiraId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BandeiraNome = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AliasCartao = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Dia = table.Column<int>(type: "INTEGER", nullable: true),
                    Parcelas = table.Column<int>(type: "INTEGER", nullable: true),
                    TaxaAdministrativa = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cartoes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cartoes_IdExterno",
                table: "Cartoes",
                column: "IdExterno",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cartoes");

            migrationBuilder.DropColumn(
                name: "Bandeira",
                table: "PagamentosVenda");
        }
    }
}
