using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClienteUltimoErroSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UltimoErroSync",
                table: "Clientes",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UltimoErroSync",
                table: "Clientes");
        }
    }
}
