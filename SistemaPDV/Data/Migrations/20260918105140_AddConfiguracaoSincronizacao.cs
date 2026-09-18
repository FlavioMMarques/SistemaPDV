using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracaoSincronizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracoesSincronizacao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UrlApi = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ApiClienteId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ApiClienteSecretProtegido = table.Column<string>(type: "TEXT", nullable: true),
                    NomeDispositivo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UltimaSincronizacaoProdutos = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UltimaSincronizacaoClientes = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UltimaSincronizacaoFormasPagamento = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UltimaSincronizacaoEmpresa = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UltimaSincronizacaoFuncionarios = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesSincronizacao", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracoesSincronizacao");
        }
    }
}
