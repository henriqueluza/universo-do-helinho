using System.Net;
using System.Text.RegularExpressions;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.UnitTests;

/// <summary>Modelos dos e-mails de acesso (ADR-0003 §8 e §11): só dados sintéticos; nenhum e-mail é enviado.</summary>
public class EmailTemplateRendererTests
{
    private const string Attack = "<script>alert(1)</script>\" onmouseover=\"x";
    private const string Token = "tok-EN_123";
    private const string Link = "https://painel.example.org/conta/definir-senha?usuario=11111111-2222-3333-4444-555555555555&token=tok-EN_123";
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly TimeSpan Invite = TimeSpan.FromHours(72);

    private static readonly EmailTemplateSettings Settings = new(
        PublicBaseUrl.Parse("https://painel.example.org", allowHttpLoopback: false), "FUNDHAS (identificação sintética)", "contato@example.org");

    private static readonly EmailTemplateRenderer Renderer = new(Settings);

    private const string SiteLine = "A página que vai abrir fica em https://painel.example.org";

    public static TheoryData<EmailModel, string[]> Models => new()
    {
        { new AdminInviteEmail("Marina Teste", "Unidade Norte (fictícia)", UserId, Token, Invite), ["Marina Teste", "Unidade Norte (fictícia)", SiteLine, "72 horas"] },
        { new PasswordResetEmail(UserId, Token, TimeSpan.FromHours(1)), [SiteLine, "vale por 1 hora e"] },
        { new PasswordChangedEmail("Marina Teste"), ["Marina Teste", "Se não foi você, avise a FUNDHAS"] },
        { new StudentAccessEmail("Ana", "2026-0042", UserId, Token, Invite), ["Ana", "2026-0042", SiteLine, "72 horas"] },
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Cada_modelo_renderiza_html_e_texto_com_o_mesmo_conteudo_essencial(EmailModel model, string[] essentials)
    {
        var email = Renderer.Render(model);
        var visible = WebUtility.HtmlDecode(Regex.Replace(email.Html, @"(<[^>]+>|\s)+", " "));

        string[] common = ["FUNDHAS (identificação sintética)", "Contato: contato@example.org", "que fica em https://painel.example.org", "Ninguém da FUNDHAS pede senha"];

        Assert.All([.. essentials, .. common], essential =>
        {
            Assert.Contains(essential, email.Text, StringComparison.Ordinal);
            Assert.Contains(essential, visible, StringComparison.Ordinal);
        });
        Assert.DoesNotMatch(@"\{\{|\}\}|\[\[|\]\]", email.Subject + email.Html + email.Text);
        Assert.InRange(email.Subject.Length, 10, EmailTemplateRenderer.MaxSubjectLength);
        Assert.Contains($"<title>{email.Subject}</title>", WebUtility.HtmlDecode(email.Html), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void Html_e_seguro_para_cliente_de_email_o_link_so_existe_no_botao_e_nenhum_modelo_carrega_senha(EmailModel model, string[] essentials)
    {
        var email = Renderer.Render(model);
        var links = essentials.Contains(SiteLine) ? 1 : 0;

        Assert.StartsWith("<!doctype html>\n<html lang=\"pt-BR\"", email.Html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(email.Html, "<html[ >]"));
        Assert.Equal(
            "<img src=\"https://painel.example.org/img/fundhas.png\" alt=\"FUNDHAS\" width=\"151\" height=\"40\"",
            Assert.Single(Regex.Matches(email.Html, "<img(?: (?:src|alt|width|height)=\"[^\"]*\")*")).Value);
        Assert.All(Regex.Matches(email.Html, "<table[^>]*>"), table => Assert.Contains("role=\"presentation\"", table.Value, StringComparison.Ordinal));
        Assert.DoesNotMatch("(?i)<script|<form|<link|<iframe|<style|javascript:|http://| on[a-z]+=", email.Html);

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
        Assert.DoesNotContain(model.GetType().GetProperties(), property => Regex.IsMatch(property.Name, "(?i)password|senha"));
        Assert.DoesNotContain(Token, model.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, email.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Nome_de_aluno_de_unidade_e_de_administrador_saem_codificados_no_html_e_crus_so_no_texto()
    {
        EmailModel[] models =
        [
            new AdminInviteEmail(Attack, Attack, UserId, Token, Invite), new PasswordChangedEmail(Attack), new StudentAccessEmail(Attack, Attack, UserId, Token, Invite),
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
    public void Quebra_de_linha_nao_injeta_cabecalho_no_assunto_nem_linha_no_texto_e_o_assunto_tem_teto()
    {
        var unit = "Leste\r\nBcc: alvo@example.org\u2028" + new string('x', 300);

        var email = Renderer.Render(new AdminInviteEmail("Marina\r\nBcc: alvo@example.org", unit, UserId, Token, Invite));

        Assert.DoesNotMatch(@"[\p{Cc}\p{Cf}\u2028\u2029]", email.Subject);
        Assert.StartsWith("Convite para administrar Leste Bcc: alvo@example.org xxx", email.Subject, StringComparison.Ordinal);
        Assert.Equal(EmailTemplateRenderer.MaxSubjectLength, email.Subject.Length);
        Assert.DoesNotMatch(@"(?m)^Bcc:|[\r\u2028]", email.Text);
        Assert.Contains("Olá, Marina Bcc: alvo@example.org.\n", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Parametro_vazio_validade_quebrada_ou_token_vazio_falham_em_vez_de_gerar_email()
    {
        Assert.Throws<InvalidOperationException>(() => Renderer.Render(new PasswordChangedEmail(" \r\n")));
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
