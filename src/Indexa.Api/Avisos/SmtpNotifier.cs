using Indexa.Dominio;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Indexa.Api.Avisos;

/// <summary>
/// SMTP genérico: serve para Amazon SES, Resend, provedor próprio ou Mailpit local, sem SDK de fornecedor.
/// Senha vem de segredo (user-secrets / variável de ambiente Smtp__Senha), nunca do appsettings versionado.
/// </summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Porta { get; set; } = 1025;
    public string? Usuario { get; set; }
    public string? Senha { get; set; }
    public string Remetente { get; set; } = "Indexa <avisos@indexa.local>";
    /// <summary>None, Auto, StartTls, StartTlsWhenAvailable ou SslOnConnect.</summary>
    public SecureSocketOptions Seguranca { get; set; } = SecureSocketOptions.StartTlsWhenAvailable;
}

public sealed class SmtpNotifier(IOptions<SmtpOptions> opcoes) : INotifier
{
    public async Task EnviarAsync(MensagemAviso m, CancellationToken ct)
    {
        var o = opcoes.Value;
        var mensagem = new MimeMessage { Subject = m.Assunto, MessageId = m.IdMensagem };
        mensagem.From.Add(MailboxAddress.Parse(o.Remetente));
        mensagem.To.Add(MailboxAddress.Parse(m.Para));

        var corpo = new BodyBuilder { TextBody = m.CorpoTexto, HtmlBody = m.CorpoHtml };
        foreach (var anexo in m.Anexos) corpo.Attachments.Add(anexo.Nome, anexo.Conteudo, ContentType.Parse(anexo.TipoMime));
        mensagem.Body = corpo.ToMessageBody();

        using var smtp = new SmtpClient { Timeout = 30_000 };
        await smtp.ConnectAsync(o.Host, o.Porta, o.Seguranca, ct);
        if (!string.IsNullOrEmpty(o.Usuario)) await smtp.AuthenticateAsync(o.Usuario, o.Senha ?? "", ct);
        await smtp.SendAsync(mensagem, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
