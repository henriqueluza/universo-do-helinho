using System.Net;
using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1859 // consulta pela porta IAppDbContext, como o caso de uso

namespace Helinho.IntegrationTests;

/// <summary>Listagem de questões do painel (#14): filtros combináveis, paginação e estado vazio (dados sintéticos).</summary>
[Collection(PostgresCollection.Name)]
public class QuestionListTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _database = $"questoes_{Guid.NewGuid():N}";

    private sealed class FakeUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid? UserId => null;

        public Guid? UnitId => null;

        public bool IsInRole(string r) => r == RoleNames.Master;
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        await db.Database.MigrateAsync(ct);
        var approved = NewQuestion(QuizType.Ambiental, AgeBand.Age6To7, "Aprovada ambiental 6-7?");
        approved.Approve();
        var approvedOlder = NewQuestion(QuizType.Ambiental, AgeBand.Age11To12, "Aprovada ambiental 11-12?");
        approvedOlder.Approve();
        db.Questions.AddRange(
            approved,
            approvedOlder,
            NewQuestion(QuizType.Ambiental, AgeBand.Age6To7, "Rascunho ambiental 6-7?"),
            NewQuestion(QuizType.Esportivo, AgeBand.Age6To7, new string('x', 200)));
        await db.SaveChangesAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Sem_filtros_lista_todas_com_enunciado_resumido()
    {
        var result = await RunAsync(new QuestionListQuery(null, null, null, 1));

        Assert.Equal(4, result.Total);
        Assert.All(result.Items, i => Assert.True(i.Statement.Length <= 90));
        Assert.Contains(result.Items, i => i.Statement.EndsWith('…'));
    }

    [Fact]
    public async Task Filtros_se_combinam()
    {
        var porTipo = await RunAsync(new QuestionListQuery(QuizType.Ambiental, null, null, 1));
        var tipoFaixa = await RunAsync(new QuestionListQuery(QuizType.Ambiental, AgeBand.Age6To7, null, 1));
        var todos = await RunAsync(new QuestionListQuery(QuizType.Ambiental, AgeBand.Age6To7, QuestionStatus.Approved, 1));
        var porStatus = await RunAsync(new QuestionListQuery(null, null, QuestionStatus.Draft, 1));

        Assert.Equal(3, porTipo.Total);
        Assert.Equal(2, tipoFaixa.Total);
        var only = Assert.Single(todos.Items);
        Assert.Equal("Aprovada ambiental 6-7?", only.Statement);
        Assert.Equal(2, porStatus.Total);
    }

    [Fact]
    public async Task Paginacao_limita_a_pagina_e_ajusta_pagina_fora_do_intervalo()
    {
        await using var db = NewContext();
        db.Questions.AddRange(Enumerable.Range(0, ListQuestions.PageSize).Select(i => NewQuestion(QuizType.Cultural, AgeBand.Age13To14, $"Cultural {i:00}?")));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = await RunAsync(new QuestionListQuery(null, null, null, 1));
        var beyond = await RunAsync(new QuestionListQuery(null, null, null, 99));

        Assert.Equal(24, first.Total);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(ListQuestions.PageSize, first.Items.Count);
        Assert.Equal(2, beyond.Page);
        Assert.Equal(4, beyond.Items.Count);
    }

    [Fact]
    public async Task Pagina_mostra_enunciado_tipo_faixa_e_status_das_questoes()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await ReplaceQuestionsAsync(factory, approved: true);
        using var client = factory.CreateClientAs(RoleNames.Master);
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/Questoes", ct);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<table", html, StringComparison.Ordinal);
        Assert.DoesNotContain("questoes-vazio", html, StringComparison.Ordinal);
        Assert.Contains("Pergunta ambiental aprovada?", html, StringComparison.Ordinal);
        Assert.Contains("Pergunta esportiva rascunho?", html, StringComparison.Ordinal);
        Assert.Contains(">Ambiental</span>", html, StringComparison.Ordinal);
        Assert.Contains("6–7 anos", html, StringComparison.Ordinal);
        Assert.Contains("11–12 anos", html, StringComparison.Ordinal);
        Assert.Contains(">Aprovada</span>", html, StringComparison.Ordinal);
        Assert.Contains(">Rascunho</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pagina_com_filtro_sem_resultado_mostra_estado_vazio_sem_tabela()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await ReplaceQuestionsAsync(factory, approved: true);
        using var client = factory.CreateClientAs(RoleNames.Master);
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/Questoes?tipo=Diversidade", ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"questoes-vazio\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pagina_com_entrada_invalida_responde_200_e_renderiza_a_lista()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await ReplaceQuestionsAsync(factory, approved: true);
        using var client = factory.CreateClientAs(RoleNames.Master);
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/Questoes?tipo=Inexistente&pagina=-3", ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<table", html, StringComparison.Ordinal);
        Assert.Contains("Pergunta ambiental aprovada?", html, StringComparison.Ordinal);
    }

    private static async Task ReplaceQuestionsAsync(HelinhoWebFactory factory, bool approved)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.QuizAnswers.ExecuteDeleteAsync(ct);
        await db.Questions.ExecuteDeleteAsync(ct);
        var first = NewQuestion(QuizType.Ambiental, AgeBand.Age6To7, "Pergunta ambiental aprovada?");
        if (approved)
        {
            first.Approve();
        }

        db.Questions.AddRange(first, NewQuestion(QuizType.Esportivo, AgeBand.Age11To12, "Pergunta esportiva rascunho?"));
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Filtro_sem_resultado_devolve_lista_vazia_na_primeira_pagina()
    {
        var result = await RunAsync(new QuestionListQuery(QuizType.Diversidade, null, null, 1));

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
    }

    private static Question NewQuestion(QuizType type, AgeBand band, string statement) =>
        new(type, band, statement, "Explicação sintética.", [("A", false), ("B", true), ("C", false), ("D", false)]);

    private async Task<QuestionListResult> RunAsync(QuestionListQuery query)
    {
        await using var context = NewContext();
        IAppDbContext db = context;
        return await ListQuestions.HandleAsync(db, query, TestContext.Current.CancellationToken);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new FakeUser());
}
