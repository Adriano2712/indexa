using System.Net;
using Hangfire.States;
using Hangfire.Storage;
using Indexa.Dominio;

namespace Indexa.Api.Jobs;

/// <summary>Monta e envia o alerta de job que esgotou as tentativas (Alertas:Email). Sem destinatário configurado, só registra em log.</summary>
public sealed class ServicoAlertas(INotifier notifier, IConfiguration config, ILogger<ServicoAlertas> log)
{
    public async Task<bool> NotificarFalhaAsync(string jobId, string job, string erro, CancellationToken ct)
    {
        var para = config["Alertas:Email"];
        if (string.IsNullOrWhiteSpace(para))
        {
            log.LogError("Job {Job} ({Id}) falhou de vez e não há Alertas:Email configurado: {Erro}", job, jobId, erro);
            return false;
        }

        var resumo = erro.Length > 1500 ? erro[..1500] + "…" : erro;
        await notifier.EnviarAsync(new MensagemAviso(
            $"alerta-job-{jobId}@indexa",
            para,
            $"[Indexa] Job falhou após todas as tentativas: {job}",
            $"O job {job} (id {jobId}) esgotou as tentativas automáticas.\n\nErro:\n{resumo}\n\nReprocesse pelo painel /hangfire depois de corrigir a causa.",
            $"<p>O job <strong>{WebUtility.HtmlEncode(job)}</strong> (id {WebUtility.HtmlEncode(jobId)}) esgotou as tentativas automáticas.</p>" +
            $"<pre style=\"white-space:pre-wrap\">{WebUtility.HtmlEncode(resumo)}</pre>" +
            "<p>Reprocesse pelo painel <code>/hangfire</code> depois de corrigir a causa.</p>",
            []), ct);
        return true;
    }
}

/// <summary>
/// Filtro global do Hangfire. Retentativas viram ScheduledState; FailedState só ocorre quando as tentativas acabam.
/// O envio roda fora da transição de estado para não travar o worker do Hangfire.
/// </summary>
public sealed class AlertaFalhaJobs(IServiceScopeFactory escopos, ILogger<AlertaFalhaJobs> log) : IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext contexto, IWriteOnlyTransaction transacao)
    {
        if (contexto.NewState is not FailedState falha) return;

        var jobId = contexto.BackgroundJob.Id;
        var job = $"{contexto.BackgroundJob.Job?.Type.Name}.{contexto.BackgroundJob.Job?.Method.Name}";
        var erro = $"{falha.Exception.GetType().Name}: {falha.Exception.Message}";

        _ = Task.Run(async () =>
        {
            try
            {
                using var escopo = escopos.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<ServicoAlertas>().NotificarFalhaAsync(jobId, job, erro, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Não foi possível enviar o alerta do job {Id}.", jobId);
            }
        });
    }

    public void OnStateUnapplied(ApplyStateContext contexto, IWriteOnlyTransaction transacao) { }
}
