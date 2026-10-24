using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Indexa.Api.Dados.Migrations
{
    /// <inheritdoc />
    public partial class IndicesEDivergencias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "PercentualAplicado",
                table: "reajustes",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,8)",
                oldPrecision: 12,
                oldScale: 8,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PercentualAcumulado",
                table: "reajustes",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,8)",
                oldPrecision: 12,
                oldScale: 8,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "divergencias_indice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoIndice = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MesReferencia = table.Column<DateOnly>(type: "date", nullable: false),
                    ValorAnterior = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ValorNovo = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Fonte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DetectadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_divergencias_indice", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_divergencias_indice_DetectadaEm",
                table: "divergencias_indice",
                column: "DetectadaEm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "divergencias_indice");

            migrationBuilder.AlterColumn<decimal>(
                name: "PercentualAplicado",
                table: "reajustes",
                type: "numeric(12,8)",
                precision: 12,
                scale: 8,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PercentualAcumulado",
                table: "reajustes",
                type: "numeric(12,8)",
                precision: 12,
                scale: 8,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);
        }
    }
}
