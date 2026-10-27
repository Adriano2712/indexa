using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Indexa.Api.IntegrationTests;

/// <summary>Garante no banco (não só na aplicação) as restrições de idempotência e unicidade.</summary>
[Collection("api")]
public sealed class BancoTests(ApiFactory api)
{
    private static readonly DateOnly Aniversario = new(2026, 3, 10);

    private async Task<Guid> CriarContratoAsync()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        return (await http.CriarContratoAsync(Auxiliares.ContratoIpca(await http.CriarClienteAsync()))).Id;
    }

    private static void AssertViolacaoDeUnicidade(DbUpdateException ex) =>
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);

    [Fact]
    public async Task Dois_reajustes_vigentes_no_mesmo_ciclo_violam_unicidade()
    {
        var contratoId = await CriarContratoAsync();
        await api.ComBancoAsync(async db =>
        {
            db.Reajustes.Add(Reajuste.NovoAguardandoIndice(contratoId, 1, Aniversario, 2500m, false, "job-a", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        // Simula um segundo worker que não viu o primeiro registro: contexto separado, mesma chave de ciclo.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => api.ComBancoAsync(async db =>
        {
            db.Reajustes.Add(Reajuste.NovoAguardandoIndice(contratoId, 1, Aniversario, 2500m, false, "job-b", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }));

        AssertViolacaoDeUnicidade(ex);
    }

    [Fact]
    public async Task Inserts_concorrentes_do_mesmo_ciclo_gravam_apenas_um()
    {
        var contratoId = await CriarContratoAsync();

        var tentativas = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            try
            {
                await api.ComBancoAsync(async db =>
                {
                    db.Reajustes.Add(Reajuste.NovoAguardandoIndice(contratoId, 1, Aniversario, 2500m, false, $"job-{i}", DateTimeOffset.UtcNow));
                    await db.SaveChangesAsync();
                });
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }));
        var resultados = await Task.WhenAll(tentativas);

        Assert.Equal(1, resultados.Count(ok => ok));
        await api.ComBancoAsync(async db => Assert.Equal(1, await db.Reajustes.CountAsync(r => r.ContratoId == contratoId)));
    }

    [Fact]
    public async Task Revisao_substitui_registro_anterior_e_mantem_ambos_no_historico()
    {
        var contratoId = await CriarContratoAsync();
        Guid originalId = default;
        await api.ComBancoAsync(async db =>
        {
            var original = Reajuste.NovoAguardandoIndice(contratoId, 1, Aniversario, 2500m, false, "job", DateTimeOffset.UtcNow);
            originalId = original.Id;
            db.Reajustes.Add(original);
            await db.SaveChangesAsync();
        });

        await api.ComBancoAsync(async db =>
        {
            var original = await db.Reajustes.SingleAsync(r => r.Id == originalId);
            db.Reajustes.Add(original.NovaRevisao("usuario:teste", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        await api.ComBancoAsync(async db =>
        {
            var historico = await db.Reajustes.Where(r => r.ContratoId == contratoId).OrderBy(r => r.Revisao).ToListAsync();
            Assert.Equal([StatusReajuste.Substituido, StatusReajuste.AguardandoIndice], historico.Select(r => r.Status));
            Assert.Equal(originalId, historico[1].SubstituiReajusteId);
        });
    }

    [Fact]
    public async Task Valor_de_indice_duplicado_para_mesmo_mes_viola_unicidade()
    {
        // Mês fora do intervalo real das séries para não colidir com dados coletados em outras etapas.
        var mes = new DateOnly(1900 + Random.Shared.Next(0, 90), 1, 1);
        await api.ComBancoAsync(async db =>
        {
            db.ValoresIndiceMensal.Add(ValorIndiceMensal.Criar(TipoIndice.Ipca, mes, 0.5m, "teste", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => api.ComBancoAsync(async db =>
        {
            db.ValoresIndiceMensal.Add(ValorIndiceMensal.Criar(TipoIndice.Ipca, mes, 0.6m, "teste", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }));

        AssertViolacaoDeUnicidade(ex);
    }

    [Fact]
    public async Task Mesma_notificacao_por_canal_nao_e_duplicada()
    {
        var contratoId = await CriarContratoAsync();
        var reajuste = Reajuste.NovoAguardandoIndice(contratoId, 1, Aniversario, 2500m, false, "job", DateTimeOffset.UtcNow);
        await api.ComBancoAsync(async db =>
        {
            db.Reajustes.Add(reajuste);
            db.Notificacoes.Add(Notificacao.Pendente(reajuste.Id, CanalNotificacao.Email, "dono@teste.com", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => api.ComBancoAsync(async db =>
        {
            db.Notificacoes.Add(Notificacao.Pendente(reajuste.Id, CanalNotificacao.Email, "dono@teste.com", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }));

        AssertViolacaoDeUnicidade(ex);
    }
}
