using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Indexa.Api.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    SenhaHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "valores_indice_mensal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoIndice = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MesReferencia = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Fonte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ColetadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_valores_indice_mensal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clientes_usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contratos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ValorInicial = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ValorAtual = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    DataBase = table.Column<DateOnly>(type: "date", nullable: false),
                    TipoIndice = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PercentualFixo = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    PeriodicidadeMeses = table.Column<int>(type: "integer", nullable: false),
                    DefasagemMeses = table.Column<int>(type: "integer", nullable: false),
                    Teto = table.Column<decimal>(type: "numeric(16,4)", precision: 16, scale: 4, nullable: true),
                    Piso = table.Column<decimal>(type: "numeric(16,4)", precision: 16, scale: 4, nullable: true),
                    UnidadeLimite = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NaoReduzir = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contratos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contratos_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reajustes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ciclo = table.Column<int>(type: "integer", nullable: false),
                    Revisao = table.Column<int>(type: "integer", nullable: false),
                    DataAniversario = table.Column<DateOnly>(type: "date", nullable: false),
                    ValorAntigo = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ValorNovo = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    PercentualAcumulado = table.Column<decimal>(type: "numeric(12,8)", precision: 12, scale: 8, nullable: true),
                    PercentualAplicado = table.Column<decimal>(type: "numeric(12,8)", precision: 12, scale: 8, nullable: true),
                    JanelaInicio = table.Column<DateOnly>(type: "date", nullable: true),
                    JanelaFim = table.Column<DateOnly>(type: "date", nullable: true),
                    RegraAplicada = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Retroativo = table.Column<bool>(type: "boolean", nullable: false),
                    GeradoPor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CaminhoPdf = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SubstituiReajusteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reajustes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reajustes_contratos_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "contratos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reajustes_reajustes_SubstituiReajusteId",
                        column: x => x.SubstituiReajusteId,
                        principalTable: "reajustes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReajusteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    UltimoErro = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnviadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notificacoes_reajustes_ReajusteId",
                        column: x => x.ReajusteId,
                        principalTable: "reajustes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_UsuarioId_Nome",
                table: "clientes",
                columns: new[] { "UsuarioId", "Nome" });

            migrationBuilder.CreateIndex(
                name: "IX_contratos_ClienteId",
                table: "contratos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_contratos_Status",
                table: "contratos",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_ReajusteId_Canal",
                table: "notificacoes",
                columns: new[] { "ReajusteId", "Canal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_ChaveIdempotencia",
                table: "reajustes",
                column: "ChaveIdempotencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_Status_DataAniversario",
                table: "reajustes",
                columns: new[] { "Status", "DataAniversario" });

            migrationBuilder.CreateIndex(
                name: "IX_reajustes_SubstituiReajusteId",
                table: "reajustes",
                column: "SubstituiReajusteId");

            migrationBuilder.CreateIndex(
                name: "ux_reajustes_contrato_ciclo_vigente",
                table: "reajustes",
                columns: new[] { "ContratoId", "Ciclo" },
                unique: true,
                filter: "\"Status\" <> 'Substituido'");

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_Email",
                table: "usuarios",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_valores_indice_mensal_TipoIndice_MesReferencia",
                table: "valores_indice_mensal",
                columns: new[] { "TipoIndice", "MesReferencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notificacoes");

            migrationBuilder.DropTable(
                name: "valores_indice_mensal");

            migrationBuilder.DropTable(
                name: "reajustes");

            migrationBuilder.DropTable(
                name: "contratos");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
