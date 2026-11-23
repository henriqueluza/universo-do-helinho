using System.Globalization;

namespace Helinho.Infrastructure.Email;

/// <summary>O que vem da configuração: a base pública e o rodapé (identificação da FUNDHAS e um contato).</summary>
public sealed record EmailTemplateSettings(PublicBaseUrl BaseUrl, string Organization, string Contact);

/// <summary>E-mail pronto para o adaptador de envio. O corpo leva o link com o token: não vai para log.</summary>
public sealed record RenderedEmail(string Subject, string Html, string Text)
{
    public override string ToString() => nameof(RenderedEmail);
}

/// <summary>
/// Renderiza os e-mails de acesso a partir dos modelos incorporados em <c>Email/Templates</c> (ADR-0003 §8): um par
/// <c>.html</c> + <c>.txt</c> por e-mail e um layout comum. Os valores entram só por <see cref="EmailTemplateText"/>.
/// </summary>
public sealed class EmailTemplateRenderer(EmailTemplateSettings settings)
{
    public const int MaxSubjectLength = 120;

    public RenderedEmail Render(EmailModel model) => model switch
    {
        AdminInviteEmail m => Compose("AdminInvite", new()
        {
            ["nome"] = m.AdminName,
            ["unidade"] = m.UnitName,
            ["link"] = Link(m.UserId, m.Token),
            ["validade"] = Hours(m.Validity),
        }),
        PasswordResetEmail m => Compose("PasswordReset", new() { ["link"] = Link(m.UserId, m.Token), ["validade"] = Hours(m.Validity) }),
        PasswordChangedEmail m => Compose("PasswordChanged", new() { ["conta"] = m.AccountName }),
        StudentAccessEmail m => Compose("StudentAccess", new()
        {
            ["aluno"] = m.StudentFirstName,
            ["matricula"] = m.Enrollment,
            ["link"] = Link(m.UserId, m.Token),
            ["validade"] = Hours(m.Validity),
        }),
        GuardianEmailChangedEmail m => Compose("GuardianEmailChanged", new()
        {
            ["aluno"] = m.StudentFirstName,
            ["data"] = m.ChangedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(model), "Tipo de e-mail sem modelo."),
    };

    private RenderedEmail Compose(string template, Dictionary<string, string> model)
    {
        // Formato do .txt: assunto na 1ª linha, pré-cabeçalho na 2ª, uma linha em branco e o corpo.
        var parts = Read($"{template}.txt").Split('\n', 4);
        if (parts.Length < 4 || parts[2].Length > 0)
        {
            throw new InvalidOperationException($"Modelo {template}.txt fora do formato: assunto, pré-cabeçalho, linha em branco, corpo.");
        }

        // Dois conjuntos, sem alterar o recebido: o comum (assunto e texto puro) e o do HTML, com a logo e o assunto já cortado.
        var common = new Dictionary<string, string>(model)
        {
            ["painel"] = settings.BaseUrl.Origin,
            ["organizacao"] = settings.Organization,
            ["contato"] = settings.Contact,
        };
        var subject = Truncate(EmailTemplateText.FillText(parts[0], common, allowUnused: true));
        var html = new Dictionary<string, string>(common)
        {
            ["assunto"] = subject,
            ["logo"] = settings.BaseUrl.Link("/img/fundhas.png").AbsoluteUri,
        };

        // Os encaixes [[...]] juntam arquivos do repositório antes da substituição; nenhum valor passa por eles.
        return new RenderedEmail(
            subject,
            EmailTemplateText.FillHtml(Read("Layout.html").Replace("[[pre_cabecalho]]", parts[1]).Replace("[[conteudo]]", Read($"{template}.html")), html),
            EmailTemplateText.FillText(Read("Layout.txt").Replace("[[conteudo]]", parts[3]), common));
    }

    private static string Truncate(string subject)
    {
        if (subject.Length <= MaxSubjectLength)
        {
            return subject;
        }

        // Não corta um par substituto ao meio.
        var cut = MaxSubjectLength - 1;
        return subject[..(char.IsHighSurrogate(subject[cut - 1]) ? cut - 1 : cut)] + "…";
    }

    private string Link(Guid userId, string token) => settings.BaseUrl.SetPasswordLink(userId, token).AbsoluteUri;

    private static string Hours(TimeSpan validity) => validity >= TimeSpan.FromHours(1) && validity.Ticks % TimeSpan.TicksPerHour == 0
        ? validity == TimeSpan.FromHours(1) ? "1 hora" : $"{(long)validity.TotalHours} horas"
        : throw new ArgumentOutOfRangeException(nameof(validity), "A validade do link precisa ser um número inteiro de horas, a partir de 1.");

    private static string Read(string file)
    {
        using var stream = typeof(EmailTemplateRenderer).Assembly.GetManifestResourceStream($"Helinho.Infrastructure.Email.Templates.{file}")
            ?? throw new InvalidOperationException($"Modelo de e-mail {file} não está incorporado ao assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd('\n');
    }
}
