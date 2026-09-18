using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDigitacaoCaixa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigitacoesBandeiraCaixa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CaixaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Bandeira = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Valor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitacoesBandeiraCaixa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigitacoesBandeiraCaixa_Caixas_CaixaId",
                        column: x => x.CaixaId,
                        principalTable: "Caixas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DigitacoesCaixa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CaixaId = table.Column<int>(type: "INTEGER", nullable: false),
                    FormaPagamentoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Valor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitacoesCaixa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigitacoesCaixa_Caixas_CaixaId",
                        column: x => x.CaixaId,
                        principalTable: "Caixas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DigitacoesCaixa_FormasPagamento_FormaPagamentoId",
                        column: x => x.FormaPagamentoId,
                        principalTable: "FormasPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DigitacoesBandeiraCaixa_CaixaId",
                table: "DigitacoesBandeiraCaixa",
                column: "CaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitacoesCaixa_CaixaId",
                table: "DigitacoesCaixa",
                column: "CaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitacoesCaixa_FormaPagamentoId",
                table: "DigitacoesCaixa",
                column: "FormaPagamentoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigitacoesBandeiraCaixa");

            migrationBuilder.DropTable(
                name: "DigitacoesCaixa");
        }
    }
}
