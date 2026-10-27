namespace Indexa.Dominio;

public sealed class Notificacao
{
    public const int MaximoTentativas = 5;
    /// <summary>Envio "preso" (worker morreu no meio) volta a ser elegível depois deste tempo.</summary>
    public static readonly TimeSpan ExpiracaoEnvio = TimeSpan.FromMinutes(15);

    private Notificacao() { }

    public Guid Id { get; private set; }
    public Guid ReajusteId { get; private set; }
    public CanalNotificacao Canal { get; private set; }
    public string Destinatario { get; private set; } = "";
    public StatusNotificacao Status { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? EnviandoDesde { get; private set; }
    public DateTimeOffset? EnviadaEm { get; private set; }
    public uint Versao { get; private set; }

    public static Notificacao Pendente(Guid reajusteId, CanalNotificacao canal, string destinatario, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinatario);
        return new Notificacao
        {
            Id = Guid.NewGuid(),
            ReajusteId = reajusteId,
            Canal = canal,
            Destinatario = destinatario,
            Status = StatusNotificacao.Pendente,
            CriadoEm = agora,
        };
    }

    /// <summary>
    /// Reserva o envio para este worker. Combinado com o controle de versão da linha, garante que dois workers
    /// não enviem a mesma notificação ao mesmo tempo.
    /// </summary>
    public bool TentarIniciarEnvio(DateTimeOffset agora)
    {
        var travadoHaPouco = Status == StatusNotificacao.Enviando && EnviandoDesde > agora - ExpiracaoEnvio;
        if (Status is StatusNotificacao.Enviada or StatusNotificacao.Falhou or StatusNotificacao.Cancelada || travadoHaPouco)
            return false;

        Status = StatusNotificacao.Enviando;
        EnviandoDesde = agora;
        Tentativas++;
        return true;
    }

    public void RegistrarSucesso(DateTimeOffset agora)
    {
        Status = StatusNotificacao.Enviada;
        EnviadaEm = agora;
        EnviandoDesde = null;
        UltimoErro = null;
    }

    public void RegistrarFalha(string erro)
    {
        UltimoErro = erro.Length > 2000 ? erro[..2000] : erro;
        EnviandoDesde = null;
        Status = Tentativas >= MaximoTentativas ? StatusNotificacao.Falhou : StatusNotificacao.Pendente;
    }

    public void Cancelar()
    {
        if (Status == StatusNotificacao.Enviada) throw new RegraDeNegocioException("Notificação já enviada.");
        Status = StatusNotificacao.Cancelada;
        EnviandoDesde = null;
    }
}
