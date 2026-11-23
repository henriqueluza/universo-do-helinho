using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #286, fatia 1: acompanhar o pedido de rascunhos de IA em /questoes/ia (ADR-0004 §15). Só o gerador Fake.</summary>
[Collection(PostgresCollection.Name)]
public class AiTelaAcompanharTests(PostgresFixture postgres)
{
    private const string Xss = "<script>alert(1)</script>";
    private static readonly string[] Four = ["Um", "Dois", "Três", "Quatro"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync(string provider = AiOptions.Fake, int quota = 60)
    {
        var factory = new HelinhoWebFactory(postgres, configure: services =>
            services.Configure<AiOptions>(o => { o.Provider = provider; o.DailyQuotaPerAdmin = quota; }));
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<Guid> SeedJobAsync(HelinhoWebFactory factory, Action<AiGenerationJob>? finish = null, TimeSpan? age = null,
        IEnumerable<AiQuestionSuggestion>? suggestions = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AiGenerationJob(AiJobKind.Batch, QuizType.Esportivo, AgeBand.Age8To10, 5, "Esportes coletivos", Guid.NewGuid(),
            DateTimeOffset.UtcNow - (age ?? TimeSpan.FromSeconds(5)));
        finish?.Invoke(job);
        db.AiGenerationJobs.Add(job);
        foreach (var suggestion in suggestions ?? [])
        {
            db.AiQuestionSuggestions.Add(Rebind(suggestion, job.Id));
        }

        await db.SaveChangesAsync(Ct);
        return job.Id;
    }

    private static AiQuestionSuggestion Rebind(AiQuestionSuggestion s, Guid jobId) =>
        new(jobId, s.Position, s.Statement, s.Options, s.CorrectIndex, s.Explanation);

    private static void Succeed(AiGenerationJob job, int valid) =>
        job.Succeed(new AiJobUsage("Fake", "fake-v1", "gerar-questoes.v1", 10, 20, TimeSpan.FromSeconds(1)), new AiJobCounts(valid + 1, valid, 1, 0), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Com_a_IA_desligada_o_pedido_responde_404()
    {
        await using var factory = await NewFactoryAsync(AiOptions.Off);
        using var client = factory.CreateClientAs(RoleNames.Master);

        using var response = await client.GetAsync($"/Questoes/Ia/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonimo_e_desafiado_e_pedido_inexistente_e_404()
    {
        await using var factory = await NewFactoryAsync();
        using var anonymous = factory.CreateAnonymousClient();
        using var admin = factory.CreateClientAs(RoleNames.Admin);

        using var denied = await anonymous.GetAsync($"/Questoes/Ia/{Guid.NewGuid()}", Ct);
        using var missing = await admin.GetAsync($"/Questoes/Ia/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Pedido_falho_mostra_a_mensagem_do_codigo_e_o_link_para_pedir_de_novo()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var id = await SeedJobAsync(factory, j => j.Fail("ai_timeout", DateTimeOffset.UtcNow));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Questoes/Ia/{id}", Ct));

        Assert.Contains("A IA demorou demais. Tente de novo.", html, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http-equiv=\"refresh\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ai_timeout", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/Questoes/Ia?tipo=Esportivo&amp;faixa=Age8To10&amp;qtd=5&amp;assunto=Esportes%20coletivos\"", await client.GetStringAsync($"/Questoes/Ia/{id}", Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pedido_pronto_sem_sugestoes_validas_mostra_o_estado_vazio_com_os_contadores()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var id = await SeedJobAsync(factory, j => Succeed(j, 0));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Questoes/Ia/{id}", Ct));

        Assert.Contains("A IA não trouxe sugestões aproveitáveis.", html, StringComparison.Ordinal);
        Assert.Contains("Geradas: 1 · válidas: 0 · com problema: 1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"rcard", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pedido_demorado_para_de_recarregar_e_deixa_so_o_link_atualizar()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var id = await SeedJobAsync(factory, j => j.Start(DateTimeOffset.UtcNow), age: TimeSpan.FromMinutes(3));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Questoes/Ia/{id}", Ct));

        Assert.Contains("Gerando as sugestões", html, StringComparison.Ordinal);
        Assert.Contains("Está demorando mais que o normal.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http-equiv=\"refresh\"", html, StringComparison.Ordinal);
        Assert.Contains(">Atualizar</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cartoes_mostram_o_selo_da_IA_o_aviso_de_conferencia_e_codificam_o_texto_gerado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var suggestions = new[]
        {
            new AiQuestionSuggestion(Guid.NewGuid(), 0, $"Qual é o enunciado {Xss}?", [Xss, "<img src=x onerror=alert(1)>", "Três", "Quatro"], 2, $"Porque {Xss}."),
            new AiQuestionSuggestion(Guid.NewGuid(), 1, "Qual é a segunda sugestão?", Four, 0, "Explicação."),
        };
        var id = await SeedJobAsync(factory, j => Succeed(j, 2), suggestions: suggestions);

        var raw = await client.GetStringAsync($"/Questoes/Ia/{id}", Ct);
        var html = WebUtility.HtmlDecode(raw);

        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", raw, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(html, "<article class=\"rcard"));
        Assert.Equal(2, Regex.Count(html, "Sugerida pela IA</span>"));
        Assert.Contains("0 de 2 revisadas", html, StringComparison.Ordinal);
        Assert.Contains("Este texto veio de uma IA e pode ter erros.", html, StringComparison.Ordinal);
        Assert.Contains("(alternativa certa)", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", raw);
        Assert.DoesNotMatch(" style=\"(?!--t:)", raw);
    }

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
