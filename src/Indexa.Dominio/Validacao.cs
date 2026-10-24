using System.Net.Mail;

namespace Indexa.Dominio;

public sealed class DadosInvalidosException(Dictionary<string, string[]> erros) : Exception("Dados inválidos.")
{
    public Dictionary<string, string[]> Erros { get; } = erros;
}

public sealed class RegraDeNegocioException(string mensagem) : Exception(mensagem);

internal sealed class ColetorErros
{
    private readonly Dictionary<string, List<string>> _erros = [];

    public void Se(bool condicao, string campo, string mensagem)
    {
        if (!condicao) return;
        if (!_erros.TryGetValue(campo, out var lista)) _erros[campo] = lista = [];
        lista.Add(mensagem);
    }

    public void LancarSeHouver()
    {
        if (_erros.Count > 0)
            throw new DadosInvalidosException(_erros.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}

internal static class Validacao
{
    public static bool TemNoMaximoCasas(decimal valor, int casas) => decimal.Round(valor, casas) == valor;

    public static bool EmailValido(string? email) =>
        email is { Length: <= 254 } && MailAddress.TryCreate(email, out var m) && m.Address == email;

    public static string? NormalizarEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
