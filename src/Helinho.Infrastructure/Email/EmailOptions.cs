using System.Net.Mail;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// <c>Email:Provider</c>, <c>Brevo:ApiKey</c>, <c>Brevo:SenderEmail</c>, <c>Brevo:SenderName</c> e <c>App:PublicBaseUrl</c>
/// (ADR-0003 §7). É classe, não record: não tem <c>ToString</c> que imprima a chave.
/// </summary>
public sealed class EmailOptions
{
    public const string Fake = "Fake";
    public const string Brevo = "Brevo";

    public string Provider { get; set; } = Fake;
    public string ApiKey { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";

    public bool UsesBrevo => string.Equals(Provider, Brevo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Erros de configuração. Cada mensagem começa pela variável e nunca traz o valor.</summary>
    public IEnumerable<string> Errors()
    {
        if (!UsesBrevo)
        {
            return string.Equals(Provider, Fake, StringComparison.OrdinalIgnoreCase) ? [] : ["Email:Provider deve ser Fake ou Brevo."];
        }

        (bool Invalid, string Message)[] rules =
        [
            // Só ASCII visível: a chave vai num cabeçalho HTTP.
            (ApiKey.Length == 0 || ApiKey.Any(c => c is <= ' ' or > '~'),
                "Brevo:ApiKey é obrigatória com Email:Provider=Brevo e não pode ter espaço nem quebra de linha."),
            (!MailAddress.TryCreate(SenderEmail, out var sender) || sender.Address != SenderEmail,
                "Brevo:SenderEmail é obrigatório com Email:Provider=Brevo e precisa ser um e-mail válido."),
            (string.IsNullOrWhiteSpace(SenderName) || SenderName.Length > 70,
                "Brevo:SenderName é obrigatório com Email:Provider=Brevo e tem até 70 caracteres."),
            (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                || url.PathAndQuery != "/" || url.Fragment.Length > 0 || url.UserInfo.Length > 0,
                "App:PublicBaseUrl é obrigatória com Email:Provider=Brevo: URL https absoluta, sem caminho nem query."),
        ];
        return rules.Where(rule => rule.Invalid).Select(rule => rule.Message);
    }
}
