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
    [GeneratedRegex("name=\"AiJobId\" value=\"([^\"]+)\"")]
    private static partial Regex JobPattern();

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
        Assert.Equal(
            "O limite diário de sugestões da IA foi atingido. Tente mais tarde.",
            Helinho.Web.Pages.Questoes.EditarModel.Message(new SuggestSingleDraftResult(SuggestSingleDraftStatus.QuotaExceeded, Quota: new AiQuotaExceeded(true, 300, 0, null))));
    }

    [Fact]
    public async Task Salvar_com_o_pedido_do_Sugerir_grava_origem_IA_e_sem_ele_ou_de_outra_pessoa_nasce_da_equipe()
    {
        var factory = await StartAsync(draft: Draft("Qual é a pergunta salva com origem IA no teste?"));
        using var ana = factory.CreateClientAs(RoleNames.Admin, name: "Ana");
        using var bia = factory.CreateClientAs(RoleNames.Admin, name: "Bia");
        var suggested = await PostAsync(ana, Sugerir, Form());
        var jobId = JobPattern().Match(suggested.Raw).Groups[1].Value;

        Dictionary<string, string> Save(string statement, string? job)
        {
            var fields = Form(statement);
            fields["Input.OptionA"] = "Um";
            fields["Input.OptionB"] = "Dois";
            fields["Input.OptionC"] = "Três";
            fields["Input.OptionD"] = "Quatro";
            fields["Input.Correct"] = "2";
            fields["Input.Explanation"] = "Explicação.";
            if (job is not null)
            {
                fields["AiJobId"] = job;
            }

            return fields;
        }

        var viaAi = await PostAsync(ana, Url, Save("Qual é a pergunta salva com origem IA no teste?", jobId));
        var avisoIa = WebUtility.HtmlDecode(await ana.GetStringAsync("/Questoes", Ct));
        var semPedido = await PostAsync(ana, Url, Save("Qual é a pergunta da equipe sem pedido?", null));
        var avisoEquipe = WebUtility.HtmlDecode(await ana.GetStringAsync("/Questoes", Ct));
        var deOutraPessoa = await PostAsync(bia, Url, Save("Qual é a pergunta da equipe com pedido alheio?", jobId));

        Assert.All(new[] { viaAi.Status, semPedido.Status, deOutraPessoa.Status }, s => Assert.Equal(HttpStatusCode.Redirect, s));
        Assert.Contains("Questão criada como rascunho, com origem IA.", avisoIa, StringComparison.Ordinal);
        Assert.Contains("Questão criada como rascunho.", avisoEquipe, StringComparison.Ordinal);
        Assert.DoesNotContain("rascunho, com origem IA", avisoEquipe, StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var questions = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions.AsNoTracking().ToListAsync(Ct);
        var ai = questions.Single(q => q.Statement.Contains("origem IA", StringComparison.Ordinal));
        Assert.Equal((QuestionOrigin.Ai, QuestionStatus.Draft, Guid.Parse(jobId)), (ai.Origin, ai.Status, ai.AiJobId.GetValueOrDefault()));
        Assert.Equal(QuestionOrigin.Team, questions.Single(q => q.Statement.Contains("sem pedido", StringComparison.Ordinal)).Origin);
        Assert.Equal(QuestionOrigin.Team, questions.Single(q => q.Statement.Contains("pedido alheio", StringComparison.Ordinal)).Origin);
    }

    [Fact]
    public async Task Ao_editar_questao_existente_nao_ha_Sugerir_e_o_handler_responde_404()
    {
        var factory = await StartAsync(draft: Draft("Qual é a pergunta que não deve ser pedida ao editar?"));
        Guid id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var question = new Question(QuizType.Cultural, AgeBand.Age6To7, "Qual é a questão existente do teste de edição?", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]);
            db.Questions.Add(question);
            await db.SaveChangesAsync(Ct);
            id = question.Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Admin);
        var page = await client.GetStringAsync($"{Url}/{id}", Ct);
        var token = TokenPattern().Match(page).Groups[1].Value;
        var post = await client.PostAsync($"{Url}/{id}?handler=Sugerir", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), Ct);

        Assert.DoesNotContain("ai-help", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(0, shared.Generator.Calls);
    }

    [Fact]
    public async Task Sugerir_sem_antiforgery_e_recusado_e_quem_esta_fora_da_politica_Panel_nao_chega_a_IA()
    {
        var factory = await StartAsync(draft: Draft("Qual é a pergunta que não deve ser pedida sem token?"));
        using var admin = factory.CreateClientAs(RoleNames.Admin);
        using var aluno = factory.CreateClientAs(RoleNames.Student);
        using var anonimo = factory.CreateAnonymousClient();

        using var semToken = await admin.PostAsync(Sugerir, new FormUrlEncodedContent(Form()), Ct);
        using var foraDoPainel = await aluno.PostAsync(Sugerir, new FormUrlEncodedContent(Form()), Ct);
        using var semLogin = await anonimo.PostAsync(Sugerir, new FormUrlEncodedContent(Form()), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, semToken.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, foraDoPainel.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, semLogin.StatusCode);
        Assert.Equal(0, shared.Generator.Calls);
    }
}
