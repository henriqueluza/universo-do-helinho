using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Infrastructure.Ai;

namespace Helinho.UnitTests;

/// <summary>Issue #227: regras das opções de IA e o gerador desligado (ADR-0004 §14). A subida da aplicação é coberta em <c>AiProviderTests</c>.</summary>
public class AiOptionsTests
{
    private const string SyntheticKey = "chave-sintetica-de-teste";

    [Fact]
    public async Task O_gerador_desligado_recusa_toda_chamada_com_motivo_Disabled_e_mensagem_sem_conteudo()
    {
        var request = QuestionDraftRequest.Create(QuizType.Ambiental, AgeBand.Age8To10, 5, "assunto-sintetico", []);

        var failure = await Assert.ThrowsAsync<QuestionDraftGenerationException>(
            () => new DisabledQuestionDraftGenerator().GenerateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(QuestionDraftFailure.Disabled, failure.Reason);
        Assert.DoesNotContain("assunto-sintetico", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void O_padrao_das_opcoes_e_IA_desligada_e_valida()
    {
        var options = new AiOptions();

        Assert.False(options.IsEnabled);
        Assert.Empty(options.Errors());
        Assert.Equal((90, 60, 300), (options.TimeoutSeconds, options.DailyQuotaPerAdmin, options.DailyGlobalLimit));
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("FAKE", true)]
    [InlineData("anthropic", true)]
    public void Provedor_conhecido_e_aceito_sem_diferenciar_maiusculas(string provider, bool enabled)
    {
        var options = new AiOptions { Provider = provider, ApiKey = SyntheticKey, Model = "modelo-sintetico" };

        Assert.Empty(options.Errors());
        Assert.Equal(enabled, options.IsEnabled);
    }

    [Theory]
    [InlineData("OpenAi", SyntheticKey, "modelo-sintetico", 90, 60, 300, "Ai:Provider")]
    [InlineData("Anthropic", "", "modelo-sintetico", 90, 60, 300, "Ai:ApiKey")]
    [InlineData("Anthropic", "chave com espaco", "modelo-sintetico", 90, 60, 300, "Ai:ApiKey")]
    [InlineData("Anthropic", "chave\nquebrada", "modelo-sintetico", 90, 60, 300, "Ai:ApiKey")]
    [InlineData("Anthropic", SyntheticKey, "", 90, 60, 300, "Ai:Model")]
    [InlineData("Anthropic", SyntheticKey, "modelo sintetico", 90, 60, 300, "Ai:Model")]
    [InlineData("Off", "", "", 4, 60, 300, "Ai:TimeoutSeconds")]
    [InlineData("Fake", "", "", 301, 60, 300, "Ai:TimeoutSeconds")]
    [InlineData("Off", "", "", 90, 0, 300, "Ai:DailyQuotaPerAdmin")]
    [InlineData("Fake", "", "", 90, 60, -1, "Ai:DailyGlobalLimit")]
    public void Configuracao_invalida_nomeia_a_variavel_e_nunca_o_valor(
        string provider, string apiKey, string model, int timeout, int quota, int global, string variable)
    {
        var options = new AiOptions
        {
            Provider = provider,
            ApiKey = apiKey,
            Model = model,
            TimeoutSeconds = timeout,
            DailyQuotaPerAdmin = quota,
            DailyGlobalLimit = global,
        };

        var error = Assert.Single(options.Errors());

        Assert.StartsWith(variable, error, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAi", error, StringComparison.Ordinal);
        Assert.True(apiKey.Length == 0 || !error.Contains(apiKey, StringComparison.Ordinal));
        Assert.True(model.Length == 0 || !error.Contains(model, StringComparison.Ordinal));
    }
}
