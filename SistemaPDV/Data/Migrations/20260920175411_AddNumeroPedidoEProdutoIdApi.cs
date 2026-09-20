using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNumeroPedidoEProdutoIdApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumeroPedido",
                table: "Vendas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProdutoIdApi",
                table: "Produtos",
                type: "INTEGER",
                nullable: true);

            // Vendas que já existem ficariam todas com NumeroPedido = 0 e o índice único abaixo
            // falharia. Numera pela ordem de criação (rowid) — as novas continuam de MAX+1.
            migrationBuilder.Sql("UPDATE Vendas SET NumeroPedido = rowid;");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_NumeroPedido",
                table: "Vendas",
                column: "NumeroPedido",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_NumeroPedido",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "NumeroPedido",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ProdutoIdApi",
                table: "Produtos");
        }
    }
}
