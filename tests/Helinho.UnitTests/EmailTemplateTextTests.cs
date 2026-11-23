using Helinho.Infrastructure.Email;

namespace Helinho.UnitTests;

/// <summary>Substituição segura dos modelos de e-mail (#205, ADR-0003 §8.2): só dados sintéticos.</summary>
public class EmailTemplateTextTests
{
    private const string Attack = "<script>alert(1)</script>\" onmouseover=\"x";

    private static Dictionary<string, string> Values(string name) => new() { ["nome"] = name };

    [Fact]
    public void Html_codifica_todo_valor_em_texto_e_em_atributo_e_o_texto_puro_recebe_o_valor_cru()
    {
        var values = new Dictionary<string, string> { ["nome"] = Attack, ["link"] = "https://painel.example.org/conta?a=1&b='2'" };

        var html = EmailTemplateText.FillHtml("<p>{{nome}}</p><a href=\"{{link}}\" title=\"{{nome}}\">{{link}}</a>", values);

        Assert.DoesNotMatch("(?i)<script|\" onmouseover|&b=|'2'", html);
        Assert.Equal(2, html.Split("&lt;script&gt;alert(1)&lt;/script&gt;&quot; onmouseover=&quot;x").Length - 1);
        Assert.Contains("href=\"https://painel.example.org/conta?a=1&amp;b=&#x27;2&#x27;\"", html, StringComparison.Ordinal);
        Assert.Equal($"Olá, {Attack}.", EmailTemplateText.FillText("Olá, {{nome}}.", Values(Attack)));
    }

    [Theory]
    [InlineData("Marina\r\nBcc: alvo@example.org", "Marina Bcc: alvo@example.org")]
    [InlineData("Marina\nAssunto: outro\n\nCorpo novo", "Marina Assunto: outro Corpo novo")]
    [InlineData(" Ma\u200Bri\u202Ena\t\u2028Teste\u2029\0\U000E0041 ", "Marina Teste")]
    [InlineData("{{contato}} {{params.x}}", "((contato)) ((params.x))")]
    public void Valor_vira_uma_linha_sem_controle_sem_bidi_e_sem_cara_de_marcador(string value, string expected)
    {
        Assert.Equal($"Olá, {expected}.", EmailTemplateText.FillText("Olá, {{nome}}.", Values(value)));
        Assert.Equal($"<p>{expected}</p>", EmailTemplateText.FillHtml("<p>{{nome}}</p>", Values(value)));
    }

    [Theory]
    [InlineData("Olá, {{nome}} de {{unidade}}.", "{{unidade}} desconhecido ou sem valor")]
    [InlineData("Olá.", "Valor sem marcador no modelo: nome")]
    [InlineData("Olá, {{nome}} {{ nome }}.", "malformado")]
    [InlineData("Olá, {{nome}} {{Nome}}.", "malformado")]
    [InlineData("Olá, {{nome}} [[conteudo]].", "malformado")]
    public void Marcador_faltando_sobrando_ou_malformado_falha_em_vez_de_sair_no_email(string template, string expected)
    {
        var values = Values("Ana");

        Assert.Contains(expected, Assert.Throws<InvalidOperationException>(() => EmailTemplateText.FillHtml(template, values)).Message, StringComparison.Ordinal);
        Assert.Contains(expected, Assert.Throws<InvalidOperationException>(() => EmailTemplateText.FillText(template, values)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valor_vazio_falha_sem_ecoar_valores_e_so_o_assunto_pode_usar_parte_deles()
    {
        var values = new Dictionary<string, string> { ["nome"] = " \r\n\u200B\u00A0\0", ["segredo"] = "valor-que-nao-pode-vazar" };

        var error = Assert.Throws<InvalidOperationException>(() => EmailTemplateText.FillText("Olá, {{nome}}. {{segredo}}", values));

        Assert.Contains("{{nome}}", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("valor-que-nao-pode-vazar", error.Message, StringComparison.Ordinal);
        Assert.Equal("Assunto fixo", EmailTemplateText.FillText("Assunto fixo", Values("Ana"), allowUnused: true));
    }
}
