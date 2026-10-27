using Hangfire;
using Indexa.Api.Avisos;
using Indexa.Api.Comum;
using Indexa.Api.Indices;
using Indexa.Api.Reajustes;

namespace Indexa.Api.Jobs;

public interface IFilaJobs
{
    void EnfileirarEnvioAviso(Guid notificacaoId);
    void EnfileirarProcessamento();
}

public sealed class FilaHangfire(IBackgroundJobClient hangfire) : IFilaJobs
{
    public void EnfileirarEnvioAviso(Guid notificacaoId) =>
        hangfire.Enqueue<EnvioAvisoJob>(j => j.ExecutarAsync(notificacaoId, CancellationToken.None));

    public void EnfileirarProcessamento() =>
        hangfire.Enqueue<ProcessamentoReajustesJob>(j => j.ExecutarAsync(CancellationToken.None));
}

public sealed class ColetaIncompletaException(string mensagem) : Exception(mensagem);

public sealed class ColetaIndicesJob(ServicoColetaIndices coleta, IFilaJobs fila)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [900, 1800, 3600])]
    public async Task ExecutarAsync(CancellationToken ct)
    {
        var resultados = await coleta.ColetarTodosAsync(ct);
        fila.EnfileirarProcessamento();

        // A falha da fonte não para o sistema (o processamento segue com o último dado), mas fica visível e é retentada.
        var falhas = resultados.Where(r => !r.Sucesso).ToList();
        if (falhas.Count > 0)
            throw new ColetaIncompletaException(string.Join("; ", falhas.Select(f => $"{f.Tipo}: {f.Erro} (dados até {f.UltimoMesDisponivel:yyyy-MM})")));
    }
}

public sealed class ProcessamentoReajustesJob(ServicoReajustes reajustes)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [300, 900, 3600])]
    public Task ExecutarAsync(CancellationToken ct) => reajustes.ProcessarTodosAsync("job:processamento", ct);
}

public sealed class EnvioAvisoJob(ServicoEnvioAvisos envio)
{
    // 1 + 4 = Notificacao.MaximoTentativas. Esgotadas, o job fica em Failed no painel (dead-letter) e a notificação em Falhou.
    [AutomaticRetry(Attempts = 4, DelaysInSeconds = [60, 300, 900, 3600])]
    public Task ExecutarAsync(Guid notificacaoId, CancellationToken ct) => envio.EnviarAsync(notificacaoId, ct);
}

public static class AgendaJobs
{
    public static void Registrar(IRecurringJobManager recorrentes)
    {
        var opcoes = new RecurringJobOptions { TimeZone = Relogio.Brasilia };
        // IPCA e IGP-M saem em dias úteis, manhã; duas coletas por dia cobrem atraso de divulgação.
        recorrentes.AddOrUpdate<ColetaIndicesJob>("coleta-indices", j => j.ExecutarAsync(CancellationToken.None), "0 10,18 * * *", opcoes);
        recorrentes.AddOrUpdate<ProcessamentoReajustesJob>("processamento-reajustes", j => j.ExecutarAsync(CancellationToken.None), "0 7 * * *", opcoes);
    }
}
