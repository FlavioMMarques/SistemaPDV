using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentativaOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProximaTentativaEm",
                table: "Vendas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProximaTentativaEm",
                table: "Clientes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasEnvio",
                table: "Clientes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProximaTentativaEm",
                table: "Caixas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasEnvio",
                table: "Caixas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProximaTentativaEm",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ProximaTentativaEm",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "TentativasEnvio",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "ProximaTentativaEm",
                table: "Caixas");

            migrationBuilder.DropColumn(
                name: "TentativasEnvio",
                table: "Caixas");
        }
    }
}
