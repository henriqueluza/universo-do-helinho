using System.Net;
using System.Text.RegularExpressions;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.UnitTests;

/// <summary>Um e-mail de exemplo: o modelo, se leva link, o pré-cabeçalho esperado e o que os dois formatos precisam dizer.</summary>
public sealed record EmailScenario(EmailModel Model, bool HasLink, string Preheader, params string[] Essentials);

/// <summary>Modelos dos e-mails de acesso (ADR-0003 §8 e §11): só dados sintéticos; nenhum e-mail é enviado.</summary>
public class EmailTemplateRendererTests
{
    private const string Attack = "<script>alert(1)</script>\" onmouseover=\"x";
    private const string Token = "tok-EN_123";
    private const string Link = "https://painel.example.org/conta/definir-senha?usuario=11111111-2222-3333-4444-555555555555&token=tok-EN_123";
    private const string SiteLine = "A página que vai abrir fica em https://painel.example.org";
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly TimeSpan Invite = TimeSpan.FromHours(72);
    private static readonly DateOnly ChangedOn = new(2026, 11, 22);

    /// <summary>Tokens do tema Recreio (claro) usados no layout; o lilás (`--ai`) é exclusivo de IA e não entra.</summary>
    private static readonly string[] Palette = ["#F8FAFD", "#FFFFFF", "#DEE6EC", "#1C2C41", "#5A6472", "#005497", "#00386E"];

    private static readonly EmailTemplateSettings Settings = new(
        PublicBaseUrl.Parse("https://painel.example.org", allowHttpLoopback: false), "FUNDHAS (identificação sintética)", "contato@example.org");

    internal static readonly EmailTemplateRenderer Renderer = new(Settings);

    public static TheoryData<EmailScenario> Scenarios => new()
    {
        new EmailScenario(
            new AdminInviteEmail("Marina Teste", "Unidade Norte (fictícia)", UserId, Token, Invite), true,
            "Defina a sua senha em até 72 horas para entrar no painel.", "Marina Teste", "Unidade Norte (fictícia)", "72 horas"),
        new EmailScenario(
            new PasswordResetEmail(UserId, Token, TimeSpan.FromHours(1)), true,
            "Use o link em até 1 hora para criar uma nova senha.", "vale por 1 hora e"),
        new EmailScenario(
            new PasswordChangedEmail("Marina Teste"), false,
            "Se não foi você, avise a FUNDHAS.", "Marina Teste", "Se foi você, não precisa fazer nada."),
        new EmailScenario(
            new StudentAccessEmail("Ana", "2026-0042", UserId, Token, Invite), true,
            "Falta definir a senha do jogo. O link vale por 72 horas.", "Ana", "2026-0042", "72 horas", "uma senha que você define agora"),
        new EmailScenario(
            new StudentAccessEmail("Ana", "2026-0042", UserId, Token, TimeSpan.FromHours(1), Renewal: true), true,
            "Link para criar a nova senha do jogo. Vale por 1 hora.", "Esta é uma troca de senha.", "responsável por Ana", "2026-0042",
            "vale por 1 hora e só pode ser usado uma vez", "a senha atual continua valendo", "Se você não pediu a troca"),
        new EmailScenario(
            new GuardianEmailChangedEmail("Ana", ChangedOn), false,
            "Se você não reconhece a troca, procure a unidade da FUNDHAS.", "de Ana no Universo", "Em 22/11/2026, a unidade",
            "enviados antes para este e-mail deixaram de valer", "Se você não reconhece a troca, procure a unidade da FUNDHAS"),
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Cada_modelo_renderiza_html_e_texto_com_o_mesmo_conteudo_essencial(EmailScenario scenario)
    {
        var email = Renderer.Render(scenario.Model);
        var visible = WebUtility.HtmlDecode(Regex.Replace(email.Html, @"(<[^>]+>|\s)+", " "));

        string[] common = ["FUNDHAS (identificação sintética)", "Contato: contato@example.org", "que fica em https://painel.example.org", "Ninguém da FUNDHAS pede senha"];

        Assert.All([.. scenario.Essentials, .. common], essential =>
        {
            Assert.Contains(essential, email.Text, StringComparison.Ordinal);
            Assert.Contains(essential, visible, StringComparison.Ordinal);
        });
        Assert.Equal(scenario.HasLink, email.Text.Contains(SiteLine, StringComparison.Ordinal));
        Assert.Equal(scenario.HasLink, visible.Contains(SiteLine, StringComparison.Ordinal));
        Assert.DoesNotMatch(@"\{\{|\}\}|\[\[|\]\]", email.Subject + email.Html + email.Text);
        Assert.InRange(email.Subject.Length, 10, EmailTemplateRenderer.MaxSubjectLength);
        Assert.Contains($"<title>{email.Subject}</title>", WebUtility.HtmlDecode(email.Html), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Html_e_seguro_para_cliente_de_email_o_link_so_existe_no_botao_e_nenhum_modelo_carrega_senha(EmailScenario scenario)
    {
        var email = Renderer.Render(scenario.Model);
        var links = scenario.HasLink ? 1 : 0;

        Assert.StartsWith("<!doctype html>\n<html lang=\"pt-BR\"", email.Html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(email.Html, "<html[ >]"));
        Assert.Equal(
            "<img src=\"https://painel.example.org/img/fundhas.png\" alt=\"FUNDHAS\" width=\"151\" height=\"40\"",
            Assert.Single(Regex.Matches(email.Html, "<img(?: (?:src|alt|width|height)=\"[^\"]*\")*")).Value);
        Assert.All(Regex.Matches(email.Html, "<table[^>]*>"), table => Assert.Contains("role=\"presentation\"", table.Value, StringComparison.Ordinal));
        Assert.DoesNotMatch("(?i)<script|<form|<link|<iframe|<style|javascript:|http://| on[a-z]+=", email.Html);

        // Layout: coluna de até 600 px, pré-cabeçalho escondido antes do conteúdo e só as cores do tema Recreio (sem lilás).
        Assert.Contains("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;\">", email.Html, StringComparison.Ordinal);
        Assert.Contains($"<body style=\"margin:0;padding:0;background-color:#F8FAFD;\">\n<div style=\"display:none;max-height:0;max-width:0;overflow:hidden;opacity:0;mso-hide:all;\">{scenario.Preheader}</div>", WebUtility.HtmlDecode(email.Html), StringComparison.Ordinal);
        Assert.All(Regex.Matches(email.Html, "#[0-9A-Fa-f]{3,8}\\b"), color => Assert.Contains(color.Value.ToUpperInvariant(), Palette));

        // ADR-0003 §8.4: no HTML a URL com o token é só o destino do botão, que tem texto de ação; por extenso, só o endereço do site.
        var buttons = Regex.Matches(email.Html, "<a href=\"([^\"]*)\"[^>]*>([^<]*)</a>");
        Assert.Equal(links, buttons.Count);
        Assert.Equal(links, Regex.Count(email.Html, "(?i)href"));
        Assert.Equal(links, Regex.Count(email.Html, Regex.Escape(Token)));
        Assert.All(buttons, button => Assert.Equal(Link, WebUtility.HtmlDecode(button.Groups[1].Value)));
        Assert.All(buttons, button => Assert.DoesNotMatch("(?i)https?:|www|/", button.Groups[2].Value));
        // No texto puro não há botão: a URL completa fica sozinha numa linha. Nenhum formato manda copiar o link nem conferi-lo antes de abrir.
        Assert.Equal(links, Regex.Count(email.Text, $"(?m)^{Regex.Escape(Link)}$"));
        Assert.DoesNotMatch("(?i)copie|cole no navegador|antes de clicar|confira o link", email.Html + email.Text);

        Assert.DoesNotMatch(@"(?i)senha\s*:", email.Subject + email.Html + email.Text);
        Assert.DoesNotContain(scenario.Model.GetType().GetProperties(), property => Regex.IsMatch(property.Name, "(?i)password|senha"));
        Assert.DoesNotContain(Token, scenario.Model.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, email.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Os_modelos_so_usam_os_marcadores_conhecidos_e_nenhum_e_de_senha()
    {
        var assembly = typeof(EmailTemplateRenderer).Assembly;
        var templates = assembly.GetManifestResourceNames().Where(name => name.Contains(".Email.Templates.", StringComparison.Ordinal)).ToList();
        var markers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in templates)
        {
            var stream = assembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            markers.UnionWith(Regex.Matches(reader.ReadToEnd(), @"\{\{([a-z_]+)\}\}").Select(match => match.Groups[1].Value));
        }

        Assert.Equal(14, templates.Count);
        Assert.Equal(["aluno", "assunto", "conta", "contato", "data", "link", "logo", "matricula", "nome", "organizacao", "painel", "unidade", "validade"], markers);
    }

    [Fact]
    public void Aviso_de_troca_do_email_do_responsavel_nao_leva_endereco_novo_matricula_nem_link()
    {
        var email = Renderer.Render(new GuardianEmailChangedEmail("Ana", ChangedOn));

        // O modelo nem tem onde receber o endereço novo, a matrícula ou um token (ADR-0003 §11.4).
        Assert.Equal(["StudentFirstName", "ChangedOn"], typeof(GuardianEmailChangedEmail).GetProperties().Select(property => property.Name));
        Assert.Equal("O e-mail de responsável no Universo do Helinho foi alterado", email.Subject); // sem nome de aluno no assunto (§5.6)
        Assert.Equal(["contato@example.org"], Regex.Matches(email.Text, @"[^\s<>]+@[^\s<>]+").Select(match => match.Value));
        Assert.Equal(["contato@example.org"], Regex.Matches(email.Html, @"[^\s<>]+@[^\s<>]+").Select(match => match.Value));
        Assert.DoesNotMatch("(?i)matr[ií]cula|href|token|definir-senha", email.Html + email.Text);
    }

    [Fact]
    public void Acesso_de_conta_com_senha_diz_que_e_troca_e_nao_que_o_acesso_acabou_de_ser_liberado()
    {
        var first = Renderer.Render(new StudentAccessEmail("Ana", "2026-0042", UserId, Token, Invite));
        var change = Renderer.Render(new StudentAccessEmail("Ana", "2026-0042", UserId, Token, TimeSpan.FromHours(1), Renewal: true));

        // ADR-0003 §11.4: com senha, "o e-mail diz que é uma troca de senha". Assunto sem nome de aluno (§5.6).
        Assert.Equal("Acesso ao jogo Universo do Helinho", first.Subject);
        Assert.Equal("Troca de senha do jogo Universo do Helinho", change.Subject);
        Assert.DoesNotMatch("(?i)troca", first.Subject + first.Html + first.Text);
        Assert.DoesNotMatch("(?i)já pode jogar|agora tem acesso|72 horas", change.Html + change.Text);
        Assert.Contains("Definir a nova senha</a>", change.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Nome_de_aluno_de_unidade_e_de_administrador_saem_codificados_no_html_e_crus_so_no_texto()
    {
        EmailModel[] models =
        [
            new AdminInviteEmail(Attack, Attack, UserId, Token, Invite), new PasswordChangedEmail(Attack), new StudentAccessEmail(Attack, Attack, UserId, Token, Invite),
            new StudentAccessEmail(Attack, Attack, UserId, Token, Invite, Renewal: true), new GuardianEmailChangedEmail(Attack, ChangedOn),
        ];

        Assert.All(models, model =>
        {
            var email = Renderer.Render(model);

            Assert.DoesNotMatch("(?i)<script|\" onmouseover", email.Html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;&quot; onmouseover=&quot;x", email.Html, StringComparison.Ordinal);
            Assert.Contains(Attack, email.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Quebra_de_linha_nao_injeta_cabecalho_no_assunto_nem_linha_no_texto_e_o_assunto_tem_teto_tambem_no_title()
    {
        var unit = "Leste\r\nBcc: alvo@example.org\u2028" + new string('x', 300);

        var email = Renderer.Render(new AdminInviteEmail("Marina\r\nBcc: alvo@example.org", unit, UserId, Token, Invite));

        Assert.DoesNotMatch(@"[\p{Cc}\p{Cf}\u2028\u2029]", email.Subject);
        Assert.StartsWith("Convite para administrar Leste Bcc: alvo@example.org xxx", email.Subject, StringComparison.Ordinal);
        Assert.Equal(EmailTemplateRenderer.MaxSubjectLength, email.Subject.Length);
        Assert.Contains($"<title>{email.Subject}</title>", WebUtility.HtmlDecode(email.Html), StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^Bcc:|[\r\u2028]", email.Text);
        Assert.Contains("Olá, Marina Bcc: alvo@example.org.\n", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Parametro_vazio_validade_quebrada_ou_token_vazio_falham_em_vez_de_gerar_email()
    {
        Assert.Throws<InvalidOperationException>(() => Renderer.Render(new PasswordChangedEmail(" \r\n")));
        Assert.Throws<InvalidOperationException>(() => Renderer.Render(new GuardianEmailChangedEmail(" ", ChangedOn)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer.Render(new PasswordResetEmail(UserId, Token, TimeSpan.FromMinutes(90))));
        Assert.Throws<ArgumentException>(() => Renderer.Render(new PasswordResetEmail(UserId, " ", Invite)));
    }

    [Fact]
    public void AddEmailTemplates_registra_o_renderizador_com_as_configuracoes_recebidas()
    {
        using var provider = new ServiceCollection().AddEmailTemplates(_ => Settings).BuildServiceProvider();

        Assert.Contains("Contato: contato@example.org", provider.GetRequiredService<EmailTemplateRenderer>().Render(new PasswordChangedEmail("Ana")).Text, StringComparison.Ordinal);
    }
}
