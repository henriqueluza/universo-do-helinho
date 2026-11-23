using System.Text.RegularExpressions;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// Endereço público da aplicação (<c>App__PublicBaseUrl</c>) e única origem dos links e imagens dos e-mails (ADR-0003 §10.3).
/// Nunca vem do cabeçalho <c>Host</c>. Todo link sai daqui, montado com <see cref="Uri"/> e conferido contra a base.
/// </summary>
public sealed partial class PublicBaseUrl
{
    private readonly Uri root;

    private PublicBaseUrl(Uri root) => this.root = root;

    /// <summary>Esquema e host, sem barra final: o que o e-mail mostra para a pessoa reconhecer um link legítimo.</summary>
    public string Origin => root.GetLeftPart(UriPartial.Authority);

    /// <param name="value">Valor da configuração.</param>
    /// <param name="allowHttpLoopback">Só em <c>Development</c>: aceita <c>http://localhost</c>.</param>
    public static PublicBaseUrl Parse(string? value, bool allowHttpLoopback)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.UserInfo.Length == 0 && uri.PathAndQuery == "/" && uri.Fragment.Length == 0
            && (uri.Scheme == Uri.UriSchemeHttps || (allowHttpLoopback && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return new PublicBaseUrl(uri);
        }

        // A mensagem diz a regra, nunca o valor recebido.
        throw new ArgumentException("App:PublicBaseUrl precisa ser uma URL absoluta https://, sem usuário, caminho, query nem fragmento (http://localhost só em Development).", nameof(value));
    }

    /// <summary>Página única de definir senha: <c>usuario</c> é o id da conta, nunca matrícula nem e-mail.</summary>
    public Uri SetPasswordLink(Guid userId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Link("/conta/definir-senha", ("usuario", userId.ToString()), ("token", token));
    }

    /// <summary>Link absoluto para um caminho da própria aplicação. Recusa o que possa trocar o host (<c>//</c>, <c>@</c>, <c>\</c>, <c>..</c>).</summary>
    public Uri Link(string path, params (string Name, string Value)[] query)
    {
        if (!SafePath().IsMatch(path))
        {
            throw new ArgumentException("Caminho de link inválido: use /segmento/segmento, só com letras, dígitos, ponto, hífen e sublinhado.", nameof(path));
        }

        var link = new UriBuilder(root)
        {
            Path = path,
            Query = string.Join('&', query.Select(q => $"{Uri.EscapeDataString(q.Name)}={Uri.EscapeDataString(q.Value)}")),
        }.Uri;

        return link.Scheme == root.Scheme && link.Host == root.Host && link.Port == root.Port && link.UserInfo.Length == 0
            ? link
            : throw new InvalidOperationException("O link montado saiu da base pública configurada.");
    }

    [GeneratedRegex(@"^(/[A-Za-z0-9_-][A-Za-z0-9._-]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafePath();
}
