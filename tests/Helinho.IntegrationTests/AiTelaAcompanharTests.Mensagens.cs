namespace Helinho.IntegrationTests;

/// <summary>Issue #286: as mensagens por código de falha da tela de IA (ADR-0004 §15). Arquivo da segunda entrega (Issue #303).</summary>
public partial class AiTelaAcompanharTests
{
    [Theory]
    [InlineData("ai_timeout", "A IA demorou demais. Tente de novo.")]
    [InlineData("ai_unavailable", "A IA está indisponível agora.")]
    [InlineData("ai_rate_limited", "A IA está indisponível agora.")]
    [InlineData("ai_refused", "A IA não gerou sugestões para este assunto. Tente outro assunto.")]
    [InlineData("ai_invalid_output", "As sugestões vieram com problema e foram descartadas.")]
    [InlineData("ai_misconfigured", "A IA não está configurada. Avise o responsável técnico.")]
    [InlineData("expired", "O pedido não terminou a tempo.")]
    [InlineData("max_attempts", "O pedido não terminou a tempo.")]
    [InlineData("codigo_novo", "Não foi possível gerar as sugestões.")]
    public void Falha_vira_mensagem_clara_sem_jargao(string code, string expected)
    {
        var text = Helinho.Web.Pages.Questoes.Ia.IaTexto.Failure(code);

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain("ai_", text, StringComparison.Ordinal);
    }
}
