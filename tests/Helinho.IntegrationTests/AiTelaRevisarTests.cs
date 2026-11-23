using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #286, fatia 3: revisar as sugestões (aprovar, editar, descartar) em /questoes/ia/{id}. Só o gerador Fake.</summary>
[Collection(PostgresCollection.Name)]
public partial class AiTelaRevisarTests(PostgresFixture postgres)
{
    private const string Xss = "<script>alert(1)</script>";
    private static readonly string[] Four = ["Um", "Dois", "Três", "Quatro"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Token();

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres, configure: services => services.Configure<AiOptions>(o => o.Provider = AiOptions.Fake));
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    /// <summary>Um pedido concluído com <paramref name="count"/> sugestões pendentes (a primeira leva o texto malicioso).</summary>
    private static async Task<(Guid Job, List<Guid> Suggestions)> SeedAsync(HelinhoWebFactory factory, int count = 3)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AiGenerationJob(AiJobKind.Batch, QuizType.Esportivo, AgeBand.Age8To10, count, null, Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-30));
        job.Succeed(new AiJobUsage("Fake", "fake-v1", "gerar-questoes.v1", 10, 20, TimeSpan.FromSeconds(1)), new AiJobCounts(count, count, 0, 0), DateTimeOffset.UtcNow);
        db.AiGenerationJobs.Add(job);
        var items = Enumerable.Range(0, count)
            .Select(i => new AiQuestionSuggestion(job.Id, i, i == 0 ? $"Sugestão {i} {Xss}?" : $"Sugestão sintética número {i}?", Four, 1, "Explicação sintética."))
            .ToList();
        db.AiQuestionSuggestions.AddRange(items);
        await db.SaveChangesAsync(Ct);
        return (job.Id, [.. items.Select(s => s.Id)]);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string url, params (string Key, string Value)[] fields)
    {
        var token = Token().Match(await client.GetStringAsync(page, Ct)).Groups[1].Value;
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).Append(new("__RequestVerificationToken", token));
        return await client.PostAsync(url, new FormUrlEncodedContent(form), Ct);
    }

    private static (string, string)[] Edit(string statement = "Enunciado editado?", string? optionB = "Dois") =>
    [
        ("Input.QuizType", "Cultural"), ("Input.AgeBand", "Age11To12"), ("Input.Statement", statement),
        ("Input.OptionA", "Um"), ("Input.OptionB", optionB ?? ""), ("Input.OptionC", "Três"), ("Input.OptionD", "Quatro"),
        ("Input.Correct", "2"), ("Input.Explanation", "Explicação editada."),
    ];

    private static async Task<(List<Question> Questions, List<AiQuestionSuggestion> Suggestions)> ReadAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Questions.AsNoTracking().Where(q => q.Origin == QuestionOrigin.Ai).ToListAsync(Ct),
            await db.AiQuestionSuggestions.AsNoTracking().OrderBy(s => s.Position).ToListAsync(Ct));
    }

    [Fact]
    public async Task Editar_abre_preenchido_sem_gravar_e_codifica_o_texto_da_IA()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory);

        var raw = await client.GetStringAsync($"/Questoes/Ia/{job}/editar/{ids[0]}", Ct);

        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", raw, StringComparison.Ordinal);
        Assert.Contains("Sugerida pela IA", raw, StringComparison.Ordinal);
        Assert.Contains("value=\"Dois\"", raw, StringComparison.Ordinal);
        Assert.Matches("value=\"1\" checked", raw);
        Assert.Equal(4, Regex.Count(raw, "<label class=\"visually-hidden\" for=\"Input_Option"));
        Assert.Empty((await ReadAsync(factory)).Questions);
        Assert.All((await ReadAsync(factory)).Suggestions, s => Assert.Equal(AiSuggestionStatus.Pending, s.Status));
    }

    [Fact]
    public async Task Editar_com_erro_mostra_o_resumo_mantem_o_digitado_e_nao_salva()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory);
        var url = $"/Questoes/Ia/{job}/editar/{ids[1]}";

        using var response = await PostAsync(client, url, url, Edit("Texto que fica", optionB: ""));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"resumo-erros\"", html, StringComparison.Ordinal);
        Assert.Contains("Informe a alternativa B.", html, StringComparison.Ordinal);
        Assert.Contains("Texto que fica", html, StringComparison.Ordinal);
        Assert.Empty((await ReadAsync(factory)).Questions);
    }
}
