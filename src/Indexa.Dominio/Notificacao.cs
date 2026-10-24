namespace Indexa.Dominio;

public sealed class Notificacao
{
    private Notificacao() { }

    public Guid Id { get; private set; }
    public Guid ReajusteId { get; private set; }
    public CanalNotificacao Canal { get; private set; }
    public StatusNotificacao Status { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? EnviadaEm { get; private set; }

    public static Notificacao Pendente(Guid reajusteId, CanalNotificacao canal, DateTimeOffset agora) =>
        new() { Id = Guid.NewGuid(), ReajusteId = reajusteId, Canal = canal, Status = StatusNotificacao.Pendente, CriadoEm = agora };
}
