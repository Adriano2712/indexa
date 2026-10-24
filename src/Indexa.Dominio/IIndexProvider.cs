namespace Indexa.Dominio;

public sealed record ValorMensalPublicado(DateOnly MesReferencia, decimal Variacao);

/// <summary>Fonte de índices. Meses ainda não publicados simplesmente não aparecem no resultado.</summary>
public interface IIndexProvider
{
    string Fonte { get; }

    Task<IReadOnlyList<ValorMensalPublicado>> ObterAsync(TipoIndice tipo, DateOnly mesInicial, DateOnly mesFinal, CancellationToken ct);
}

/// <summary>Resposta da fonte fora do contrato esperado (formato, código de série etc.).</summary>
public sealed class FonteIndiceInvalidaException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);
