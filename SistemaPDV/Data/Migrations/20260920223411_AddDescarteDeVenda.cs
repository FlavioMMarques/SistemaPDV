using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDescarteDeVenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DescartadaEm",
                table: "Vendas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DescartadaPorId",
                table: "Vendas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoDescarte",
                table: "Vendas",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SolicitadaPorId",
                table: "Vendas",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescartadaEm",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "DescartadaPorId",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "MotivoDescarte",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "SolicitadaPorId",
                table: "Vendas");
        }
    }
}
