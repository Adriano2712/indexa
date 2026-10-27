using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Indexa.Api.Dados.Migrations
{
    /// <inheritdoc />
    public partial class JobsAvisosEImutabilidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AntecedenciaAvisoDias",
                table: "usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<bool>(
                name: "AvisoEmailAtivo",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailAviso",
                table: "usuarios",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AplicadoEm",
                table: "reajustes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CalculadoEm",
                table: "reajustes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClausulasJson",
                table: "reajustes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndicesUsadosJson",
                table: "reajustes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotificadoEm",
                table: "reajustes",
                type: "timestamp with time zone",
                nullable: true);


            migrationBuilder.AddColumn<string>(
                name: "Destinatario",
                table: "notificacoes",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EnviandoDesde",
                table: "notificacoes",
                type: "timestamp with time zone",
                nullable: true);


            migrationBuilder.AddColumn<bool>(
                name: "AfetaReajusteEmitido",
                table: "divergencias_indice",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReajustesRecalculados",
                table: "divergencias_indice",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UltimoCicloAplicado",
                table: "contratos",
                type: "integer",
                nullable: false,
                defaultValue: 0);


            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_Status",
                table: "notificacoes",
                column: "Status");

            // Defesa em profundidade: mesmo um UPDATE manual ou um bug na aplicação não altera reajuste já emitido.
            migrationBuilder.Sql("""
                CREATE FUNCTION reajustes_imutavel() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'reajustes não podem ser excluídos (id %)', OLD."Id" USING ERRCODE = 'P0001';
                    END IF;

                    IF OLD."Status" IN ('Notificado', 'Aplicado') THEN
                        IF (OLD."ContratoId", OLD."Ciclo", OLD."Revisao", OLD."DataAniversario", OLD."ValorAntigo", OLD."ValorNovo",
                            OLD."PercentualAcumulado", OLD."PercentualAplicado", OLD."JanelaInicio", OLD."JanelaFim",
                            OLD."RegraAplicada", OLD."IndicesUsadosJson", OLD."ClausulasJson", OLD."Retroativo",
                            OLD."ChaveIdempotencia", OLD."SubstituiReajusteId", OLD."CriadoEm", OLD."CalculadoEm", OLD."GeradoPor")
                           IS DISTINCT FROM
                           (NEW."ContratoId", NEW."Ciclo", NEW."Revisao", NEW."DataAniversario", NEW."ValorAntigo", NEW."ValorNovo",
                            NEW."PercentualAcumulado", NEW."PercentualAplicado", NEW."JanelaInicio", NEW."JanelaFim",
                            NEW."RegraAplicada", NEW."IndicesUsadosJson", NEW."ClausulasJson", NEW."Retroativo",
                            NEW."ChaveIdempotencia", NEW."SubstituiReajusteId", NEW."CriadoEm", NEW."CalculadoEm", NEW."GeradoPor") THEN
                            RAISE EXCEPTION 'reajuste % já emitido: valores são imutáveis', OLD."Id" USING ERRCODE = 'P0001';
                        END IF;

                        IF OLD."CaminhoPdf" IS NOT NULL AND NEW."CaminhoPdf" IS DISTINCT FROM OLD."CaminhoPdf" THEN
                            RAISE EXCEPTION 'reajuste %: documento emitido não pode ser trocado', OLD."Id" USING ERRCODE = 'P0001';
                        END IF;

                        IF NEW."Status" IS DISTINCT FROM OLD."Status" AND NOT (
                               (OLD."Status" = 'Notificado' AND NEW."Status" = 'Aplicado') OR
                               (OLD."Status" = 'Aplicado' AND NEW."Status" = 'Substituido' AND OLD."Retroativo" AND OLD."NotificadoEm" IS NULL)) THEN
                            RAISE EXCEPTION 'reajuste %: transição % -> % não permitida', OLD."Id", OLD."Status", NEW."Status" USING ERRCODE = 'P0001';
                        END IF;
                    END IF;

                    IF OLD."Status" = 'Substituido' AND NEW IS DISTINCT FROM OLD THEN
                        RAISE EXCEPTION 'reajuste % substituído é histórico e não muda', OLD."Id" USING ERRCODE = 'P0001';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_reajustes_imutavel
                    BEFORE UPDATE OR DELETE ON reajustes
                    FOR EACH ROW EXECUTE FUNCTION reajustes_imutavel();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_reajustes_imutavel ON reajustes;
                DROP FUNCTION IF EXISTS reajustes_imutavel();
                """);

            migrationBuilder.DropIndex(
                name: "IX_notificacoes_Status",
                table: "notificacoes");

            migrationBuilder.DropColumn(
                name: "AntecedenciaAvisoDias",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "AvisoEmailAtivo",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "EmailAviso",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "AplicadoEm",
                table: "reajustes");

            migrationBuilder.DropColumn(
                name: "CalculadoEm",
                table: "reajustes");

            migrationBuilder.DropColumn(
                name: "ClausulasJson",
                table: "reajustes");

            migrationBuilder.DropColumn(
                name: "IndicesUsadosJson",
                table: "reajustes");

            migrationBuilder.DropColumn(
                name: "NotificadoEm",
                table: "reajustes");


            migrationBuilder.DropColumn(
                name: "Destinatario",
                table: "notificacoes");

            migrationBuilder.DropColumn(
                name: "EnviandoDesde",
                table: "notificacoes");


            migrationBuilder.DropColumn(
                name: "AfetaReajusteEmitido",
                table: "divergencias_indice");

            migrationBuilder.DropColumn(
                name: "ReajustesRecalculados",
                table: "divergencias_indice");

            migrationBuilder.DropColumn(
                name: "UltimoCicloAplicado",
                table: "contratos");

        }
    }
}
