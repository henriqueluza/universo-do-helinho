using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>Issues #288 e #295: confirmação, falhas, cota e o salvar com origem IA do Sugerir (ADR-0004 §9.3 e §15). Arquivo da segunda entrega (Issue #303).</summary>
public partial class QuestoesSugerirIaTests
{
    [Fact]
    public async Task Com_texto_ja_digitado_pede_confirmacao_e_so_troca_com_ela()
    {
        var factory = await StartAsync(draft: Draft("Qual é a pergunta que troca o texto digitado?"));
        using var client = factory.CreateClientAs(RoleNames.Admin);

        var ask = await PostAsync(client, Sugerir, Form("Meu enunciado digitado à mão"));

        Assert.Equal(0, shared.Generator.Calls);
        Assert.Contains("Meu enunciado digitado à mão</textarea>", ask.Html, StringComparison.Ordinal);
        Assert.Contains("Você já escreveu neste formulário.", ask.Html, StringComparison.Ordinal);
        Assert.Contains("name=\"Substituir\" value=\"true\"", ask.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("ai-filled", ask.Html, StringComparison.Ordinal);

        var fields = Form("Meu enunciado digitado à mão");
        fields["Substituir"] = "true";
        var replaced = await PostAsync(client, Sugerir, fields);

        Assert.Equal(1, shared.Generator.Calls);
        Assert.Contains("Qual é a pergunta que troca o texto digitado?</textarea>", replaced.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Meu enunciado digitado", replaced.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(QuestionDraftFailure.Timeout, "A IA demorou para responder. Tente de novo.")]
    [InlineData(QuestionDraftFailure.ProviderUnavailable, "A IA está indisponível agora. Tente mais tarde.")]
    [InlineData(QuestionDraftFailure.RateLimited, "A IA está indisponível agora. Tente mais tarde.")]
    [InlineData(QuestionDraftFailure.Refused, "A IA não gerou uma sugestão desta vez. Tente de novo.")]
    [InlineData(QuestionDraftFailure.InvalidOutput, "A sugestão veio com problema e foi descartada. Tente de novo.")]
    [InlineData(QuestionDraftFailure.Misconfigured, "A IA não está configurada. Avise o responsável técnico.")]
    public async Task Falha_vira_mensagem_clara_por_codigo_e_o_que_a_pessoa_escreveu_continua(QuestionDraftFailure failure, string message)
    {
        var factory = await StartAsync(failure: failure);
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var fields = Form("Enunciado que a pessoa digitou e não pode sumir");
        fields["Substituir"] = "true";

        var (status, _, html) = await PostAsync(client, Sugerir, fields);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(message, html, StringComparison.Ordinal);
        Assert.Contains("O que você escreveu continua aqui.", html, StringComparison.Ordinal);
        Assert.Contains("Enunciado que a pessoa digitou e não pode sumir</textarea>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ai-filled", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cota_esgotada_mostra_a_mensagem_e_nao_chama_a_IA()
    {
        var factory = await StartAsync(draft: Draft("Qual é a pergunta da cota do teste?"), quota: 1);
        using var client = factory.CreateClientAs(RoleNames.Admin, name: "Pessoa da cota"); // o id sai do nome: pessoa própria, saldo próprio

        var first = await PostAsync(client, Sugerir, Form());
        var (_, _, html) = await PostAsync(client, Sugerir, Form());

        Assert.Contains("ai-filled", first.Html, StringComparison.Ordinal);
        Assert.Equal(1, shared.Generator.Calls);
        Assert.Contains("Você chegou ao limite de sugestões da IA por hoje. Tente mais tarde.", html, StringComparison.Ordinal);
    }
}
