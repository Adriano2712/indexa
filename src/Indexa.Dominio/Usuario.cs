namespace Indexa.Dominio;

public sealed class Usuario
{
    public const int TamanhoMinimoSenha = 10;
    public const int TamanhoMaximoSenha = 128;

    private Usuario() { }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string SenhaHash { get; private set; } = "";
    public DateTimeOffset CriadoEm { get; private set; }

    public static string? NormalizarEmail(string? email) => Validacao.NormalizarEmail(email);

    public static Usuario Criar(string? nome, string? email, string? senha, DateTimeOffset agora)
    {
        var nomeLimpo = nome?.Trim();
        var emailNormalizado = NormalizarEmail(email);

        var erros = new ColetorErros();
        erros.Se(string.IsNullOrEmpty(nomeLimpo) || nomeLimpo.Length > 200, "nome", "Nome é obrigatório (até 200 caracteres).");
        erros.Se(!Validacao.EmailValido(emailNormalizado), "email", "E-mail inválido.");
        erros.Se(senha is null || senha.Length < TamanhoMinimoSenha || senha.Length > TamanhoMaximoSenha,
            "senha", $"Senha deve ter entre {TamanhoMinimoSenha} e {TamanhoMaximoSenha} caracteres.");
        erros.LancarSeHouver();

        return new Usuario { Id = Guid.NewGuid(), Nome = nomeLimpo!, Email = emailNormalizado!, CriadoEm = agora };
    }

    public void DefinirSenhaHash(string hash) => SenhaHash = hash;
}
