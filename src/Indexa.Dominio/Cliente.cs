namespace Indexa.Dominio;

public sealed class Cliente
{
    public const int TamanhoMaximoNome = 200;

    private Cliente() { }

    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Nome { get; private set; } = "";
    public string? Documento { get; private set; }
    public string? Email { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    public static Cliente Criar(Guid usuarioId, string? nome, string? documento, string? email, DateTimeOffset agora)
    {
        var cliente = new Cliente { Id = Guid.NewGuid(), UsuarioId = usuarioId, CriadoEm = agora };
        cliente.Atualizar(nome, documento, email, agora);
        return cliente;
    }

    public void Atualizar(string? nome, string? documento, string? email, DateTimeOffset agora)
    {
        var nomeLimpo = nome?.Trim();
        var doc = NormalizarDocumento(documento);
        var emailNormalizado = Validacao.NormalizarEmail(email);

        var erros = new ColetorErros();
        erros.Se(string.IsNullOrEmpty(nomeLimpo) || nomeLimpo.Length > TamanhoMaximoNome,
            "nome", $"Nome é obrigatório (até {TamanhoMaximoNome} caracteres).");
        erros.Se(doc is not null && !DocumentoValido(doc),
            "documento", "Informe CPF (11 dígitos) ou CNPJ (14 caracteres, numérico ou alfanumérico).");
        erros.Se(emailNormalizado is not null && !Validacao.EmailValido(emailNormalizado), "email", "E-mail inválido.");
        erros.LancarSeHouver();

        Nome = nomeLimpo!;
        Documento = doc;
        Email = emailNormalizado;
        AtualizadoEm = agora;
    }

    public static string? NormalizarDocumento(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento)) return null;
        return new string(documento.Where(c => c is not ('.' or '-' or '/' or ' ')).ToArray()).ToUpperInvariant();
    }

    // CNPJ alfanumérico (IN RFB 2.229/2024): 12 posições [0-9A-Z] + 2 dígitos verificadores numéricos.
    private static bool DocumentoValido(string doc) =>
        (doc.Length == 11 && doc.All(char.IsAsciiDigit)) ||
        (doc.Length == 14 && doc[..12].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)) && doc[12..].All(char.IsAsciiDigit));
}
