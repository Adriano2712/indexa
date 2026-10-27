namespace Indexa.Dominio;

public sealed record Anexo(string Nome, string TipoMime, byte[] Conteudo);

public sealed record MensagemAviso(
    string IdMensagem, string Para, string Assunto, string CorpoTexto, string CorpoHtml, IReadOnlyList<Anexo> Anexos);

/// <summary>Canal de aviso (e-mail hoje; Telegram seria outra implementação).</summary>
public interface INotifier
{
    Task EnviarAsync(MensagemAviso mensagem, CancellationToken ct);
}

public sealed record VariacaoMensal(DateOnly Mes, decimal Variacao);

public sealed record DadosCartaReajuste(
    Guid ReajusteId,
    string ResponsavelNome,
    string ClienteNome,
    string? ClienteDocumento,
    string ContratoDescricao,
    int Ciclo,
    DateOnly Aniversario,
    TipoIndice TipoIndice,
    string FonteIndice,
    decimal ValorAntigo,
    decimal ValorNovo,
    decimal PercentualAcumulado,
    decimal PercentualAplicado,
    RegraAplicada Regra,
    DateOnly? JanelaInicio,
    DateOnly? JanelaFim,
    IReadOnlyList<VariacaoMensal> Variacoes,
    DateTimeOffset GeradoEm);

public interface IPdfGenerator
{
    byte[] GerarCartaReajuste(DadosCartaReajuste dados);
}

/// <summary>Armazenamento de documentos gerados (disco local hoje; S3/Object Storage depois).</summary>
public interface IArmazenamentoDocumentos
{
    Task<string> SalvarAsync(string nome, byte[] conteudo, CancellationToken ct);
    Task<byte[]> LerAsync(string caminho, CancellationToken ct);
}
