using Indexa.Dominio;
using Microsoft.Extensions.Options;

namespace Indexa.Api.Documentos;

public sealed class ArmazenamentoOptions
{
    public string Pasta { get; set; } = "armazenamento/documentos";
}

public sealed class ArmazenamentoLocal(IOptions<ArmazenamentoOptions> opcoes, IHostEnvironment ambiente) : IArmazenamentoDocumentos
{
    private string Raiz => Path.GetFullPath(Path.Combine(ambiente.ContentRootPath, opcoes.Value.Pasta));

    public async Task<string> SalvarAsync(string nome, byte[] conteudo, CancellationToken ct)
    {
        var destino = Resolver(nome);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var temporario = destino + ".tmp";
        await File.WriteAllBytesAsync(temporario, conteudo, ct);
        File.Move(temporario, destino, overwrite: true);
        return nome;
    }

    public Task<byte[]> LerAsync(string caminho, CancellationToken ct) => File.ReadAllBytesAsync(Resolver(caminho), ct);

    private string Resolver(string relativo)
    {
        var raiz = Raiz;
        var completo = Path.GetFullPath(Path.Combine(raiz, relativo));
        if (!completo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Caminho fora da pasta de documentos.", nameof(relativo));
        return completo;
    }
}
