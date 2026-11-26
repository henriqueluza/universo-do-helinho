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

/// <summary>
/// Issue #288: "Sugerir com IA" no criador de questões (ADR-0004 §15). Um host para a classe; o gerador e as opções da IA
/// são do teste (nenhuma chamada real) e mudam de um caso para outro.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class QuestoesSugerirIaTests(PostgresFixture postgres, QuestoesSugerirIaTests.SharedFactory shared)
    : IClassFixture<QuestoesSugerirIaTests.SharedFactory>
{
    private const string Url = "/Questoes/Editar";
    private const string Sugerir = "/Questoes/Editar?handler=Sugerir";
    private static readonly string[] Ausentes = ["ai-help", "btn-ai", "Sugerir", "ai-flag", "Modo de demonstração"];
    private static readonly string[] FourOptions = ["Um", "Dois", "Três", "Quatro"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    public sealed class ScriptedGenerator : IQuestionDraftGenerator
    {
        public QuestionDraft? Draft { get; set; }
        public QuestionDraftFailure? Failure { get; set; }
        public int Calls { get; set; }

        public Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is { } reason
                ? throw new QuestionDraftGenerationException(reason)
                : Task.FromResult(new QuestionDraftBatch(Draft is null ? [] : [Draft], "gerar-questoes.v1", new AiUsage("Stub", "modelo-sintetico", 1, 1, TimeSpan.Zero)));
        }
    }

    public sealed class SharedFactory : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        public ScriptedGenerator Generator { get; } = new();
        public AiOptions Options { get; } = new() { Provider = AiOptions.Fake };

        public async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                _factory = new HelinhoWebFactory(postgres, configure: services =>
                {
                    services.AddSingleton<IQuestionDraftGenerator>(Generator);
                    services.AddSingleton<IOptions<AiOptions>>(Microsoft.Extensions.Options.Options.Create(Options));
                });
                await _factory.EnsureDatabaseAsync();
            }

            return _factory;
        }

        public async ValueTask DisposeAsync() => await (_factory?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    private async Task<HelinhoWebFactory> StartAsync(
        bool enabled = true, QuestionDraft? draft = null, QuestionDraftFailure? failure = null, int quota = 60)
    {
        var factory = await shared.GetAsync(postgres);
        shared.Options.Provider = enabled ? AiOptions.Fake : AiOptions.Off;
        shared.Options.DailyQuotaPerAdmin = quota;
        shared.Generator.Draft = draft;
        shared.Generator.Failure = failure;
        shared.Generator.Calls = 0;
        return factory;
    }

    private static QuestionDraft Draft(string statement) => new(statement, FourOptions, 2, "Porque sim, e a explicação confere.");

    private static Dictionary<string, string> Form(string? statement = null, string? quiz = "Cultural", string? band = "Age8To10") => new()
    {
        ["Input.QuizType"] = quiz ?? "",
        ["Input.AgeBand"] = band ?? "",
        ["Input.Statement"] = statement ?? "",
        ["Input.Explanation"] = "",
        ["Input.OptionA"] = "",
        ["Input.OptionB"] = "",
        ["Input.OptionC"] = "",
        ["Input.OptionD"] = "",
    };

    private static async Task<(HttpStatusCode Status, string Raw, string Html)> PostAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(Url, Ct);
        fields["__RequestVerificationToken"] = TokenPattern().Match(page).Groups[1].Value;
        var response = await client.PostAsync(url, new FormUrlEncodedContent(fields), Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, raw, WebUtility.HtmlDecode(raw));
    }

    private static async Task<int> CountQuestionsAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions.CountAsync(Ct);
    }

    [Fact]
    public async Task Com_a_IA_desligada_o_bloco_nao_aparece_e_o_Sugerir_responde_404()
    {
        var factory = await StartAsync(enabled: false, draft: Draft("Qual é a pergunta que não deve ser pedida?"));
        using var client = factory.CreateClientAs(RoleNames.Admin);

        var page = await client.GetStringAsync(Url, Ct);
        var post = await PostAsync(client, Sugerir, Form());

        Assert.All(Ausentes, s => Assert.DoesNotContain(s, page, StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, post.Status);
        Assert.Equal(0, shared.Generator.Calls);
    }

    [Fact]
    public async Task Com_a_IA_ligada_o_bloco_aparece_com_aviso_de_demonstracao_sem_JS_inline_e_sem_onchange()
    {
        var factory = await StartAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(Url, Ct));

        Assert.Contains("class=\"ai-help\"", page, StringComparison.Ordinal);
        Assert.Contains("Modo de demonstração: sugestões fictícias.", page, StringComparison.Ordinal);
        Assert.Contains("formaction=\"/Questoes/Editar?handler=Sugerir\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"ia-resultado\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("autofocus", page, StringComparison.Ordinal); // sem pedido, o foco fica onde estava
        var enter = page.IndexOf("<button type=\"submit\" class=\"visually-hidden\" tabindex=\"-1\" aria-hidden=\"true\">Salvar</button>", StringComparison.Ordinal);
        Assert.InRange(enter, 0, page.IndexOf("class=\"ai-help\"", StringComparison.Ordinal) - 1); // o botão padrão do Enter (Salvar) existe e vem antes do Sugerir
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", page);
        Assert.DoesNotContain("onchange", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("style=\"(?!--)", page); // só variável CSS do servidor, como nas outras telas
    }

    [Fact]
    public async Task Sugerir_preenche_o_formulario_marca_a_IA_move_o_foco_e_nao_salva_nada()
    {
        var factory = await StartAsync(draft: Draft("Qual é a pergunta sugerida para o teste de preenchimento?"));
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var before = await CountQuestionsAsync(factory);

        var (status, _, html) = await PostAsync(client, Sugerir, Form());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, shared.Generator.Calls);
        Assert.Contains("Qual é a pergunta sugerida para o teste de preenchimento?</textarea>", html, StringComparison.Ordinal);
        Assert.Contains("Porque sim, e a explicação confere.</textarea>", html, StringComparison.Ordinal);
        foreach (var option in FourOptions)
        {
            Assert.Contains($"value=\"{option}\"", html, StringComparison.Ordinal);
        }

        Assert.Matches("name=\"Input.Correct\" id=\"Input_Correct_2\" value=\"2\" checked", html);
        Assert.Contains("Sugerida pela IA · confira antes de salvar", html, StringComparison.Ordinal);
        Assert.Contains("Nada foi salvo ainda", html, StringComparison.Ordinal);
        Assert.Matches("id=\"ia-resultado\"[^>]*role=\"status\"[^>]*aria-live=\"polite\"[^>]*tabindex=\"-1\"[^>]*autofocus", html);
        Assert.Contains("ai-filled", html, StringComparison.Ordinal);
        Assert.Matches("name=\"AiJobId\" value=\"[0-9a-f-]{36}\"", html);
        Assert.Contains("class=\"theme-pick\"", html, StringComparison.Ordinal); // a prévia e o resto da tela seguem inteiros
        Assert.Contains("data-empty=\"Sua pergunta aparece aqui\">Qual é a pergunta sugerida para o teste de preenchimento?</p>", html, StringComparison.Ordinal); // a prévia recebe o enunciado
        Assert.Equal(before, await CountQuestionsAsync(factory)); // "Sugerir" nunca salva
    }

    [Fact]
    public async Task Texto_da_IA_sai_codificado_e_marcacao_na_sugestao_e_descartada_pelo_validador()
    {
        var factory = await StartAsync(draft: new QuestionDraft("Quem disse \"oi\" & 'tchau' no teste de codificação?", ["A&B", "C\"D", "E'F", "G"], 0, "Explicação com & e aspas."));
        using var client = factory.CreateClientAs(RoleNames.Admin);

        var encoded = await PostAsync(client, Sugerir, Form());

        Assert.Contains("&amp; ", encoded.Raw, StringComparison.Ordinal);
        Assert.Contains("&quot;oi&quot;", encoded.Raw, StringComparison.Ordinal);
        Assert.Contains("value=\"A&amp;B\"", encoded.Raw, StringComparison.Ordinal);

        shared.Generator.Draft = new QuestionDraft("Qual é a pergunta <script>alert(1)</script> do teste?", FourOptions, 0, "Explicação.");
        var malicious = await PostAsync(client, Sugerir, Form());

        Assert.DoesNotContain("<script>alert", malicious.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(1)", malicious.Raw, StringComparison.Ordinal);
        Assert.Contains("A sugestão veio com problema e foi descartada.", malicious.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_tipo_ou_faixa_a_IA_nao_e_chamada_e_o_erro_vai_para_o_resumo()
    {
        var factory = await StartAsync(draft: Draft("Qual é a pergunta que não deve ser pedida sem tema?"));
        using var client = factory.CreateClientAs(RoleNames.Admin);

        var (status, _, html) = await PostAsync(client, Sugerir, Form(quiz: null, band: null));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, shared.Generator.Calls);
        Assert.Contains("id=\"resumo-erros\"", html, StringComparison.Ordinal);
        Assert.Contains("Escolha o tipo de quiz para pedir a sugestão.", html, StringComparison.Ordinal);
        Assert.Contains("Escolha a faixa etária para pedir a sugestão.", html, StringComparison.Ordinal);
    }
}
