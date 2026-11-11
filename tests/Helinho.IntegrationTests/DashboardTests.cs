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

/// <summary>Dashboard do painel (#10): números por faixa etária e período, escopo de unidade e estado vazio (dados sintéticos).</summary>
[Collection(PostgresCollection.Name)]
public class DashboardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly string _database = $"dashboard_{Guid.NewGuid():N}";
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");

    private sealed class FakeUser(string role, Guid? unitId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid? UserId => null;

        public Guid? UnitId => unitId;

        public bool IsInRole(string r) => r == role;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(ct);
        var users = Enumerable.Range(0, 3).Select(i => new AppUser { UserName = $"dash{i}", UnitId = i < 2 ? _unitA.Id : _unitB.Id }).ToArray();
        db.Users.AddRange(users);
        db.Units.AddRange(_unitA, _unitB);
        // A: Ana (7 anos) e Bia (12 anos); B: Caio (7 anos).
        var ana = NewStudent(users[0].Id, _unitA.Id, "Ana", new DateOnly(2019, 3, 1));
        var bia = NewStudent(users[1].Id, _unitA.Id, "Bia", new DateOnly(2014, 3, 1));
        var caio = NewStudent(users[2].Id, _unitB.Id, "Caio", new DateOnly(2019, 3, 1));
        db.Students.AddRange(ana, bia, caio);
        await db.SaveChangesAsync(ct);

        await AddAnswersAsync(db, ana, QuizType.Ambiental, [(true, 1), (false, 2)], ct);
        await AddAnswersAsync(db, bia, QuizType.Esportivo, [(true, 3)], ct);
        await AddAnswersAsync(db, bia, QuizType.Ambiental, [(true, 60)], ct); // fora do período de 30 dias
        await AddAnswersAsync(db, caio, QuizType.Ambiental, [(true, 1), (true, 2), (true, 3)], ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Master_ve_todas_as_unidades_e_acerto_por_tipo()
    {
        var result = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, null));

        Assert.Equal(3, result.ActiveStudents);
        Assert.Equal(6, result.Answers);
        Assert.Equal(5, result.AccuracyByType.Count);
        Assert.Equal(80, result.AccuracyByType.Single(a => a.QuizType == QuizType.Ambiental).AccuracyPercent);
        Assert.Equal(100, result.AccuracyByType.Single(a => a.QuizType == QuizType.Esportivo).AccuracyPercent);
        Assert.Null(result.AccuracyByType.Single(a => a.QuizType == QuizType.Cultural).AccuracyPercent);
        Assert.Equal(2, result.Units.Count);
    }

    [Fact]
    public async Task Filtro_de_faixa_etaria_muda_os_numeros()
    {
        var all = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, null));
        var young = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age6To7, 30, null));
        var older = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age11To12, 30, null));
        var none = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age13To14, 30, null));

        Assert.Equal((3, 6), (all.ActiveStudents, all.Answers));
        Assert.Equal((2, 5), (young.ActiveStudents, young.Answers));
        Assert.Equal((1, 1), (older.ActiveStudents, older.Answers));
        Assert.Equal((0, 0), (none.ActiveStudents, none.Answers));
    }

    [Fact]
    public async Task Filtro_de_periodo_muda_os_numeros()
    {
        var week = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));
        var quarter = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 90, null));

        Assert.Equal(6, week.Answers);
        Assert.Equal(7, quarter.Answers);
        Assert.Equal(GetDashboard.DefaultDays, GetDashboard.NormalizeDays(3));
    }

    [Fact]
    public async Task Admin_ve_so_a_propria_unidade_e_ignora_filtro_de_unidade()
    {
        var own = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 30, null));
        var forced = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 30, _unitB.Id));

        Assert.Equal((2, 3), (own.ActiveStudents, own.Answers));
        Assert.Equal((2, 3), (forced.ActiveStudents, forced.Answers));
        Assert.Empty(own.Units);
        Assert.DoesNotContain(own.Recent, r => r.StudentName == "Caio");
    }

    [Fact]
    public async Task Master_filtra_por_unidade()
    {
        var b = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, _unitB.Id));

        Assert.Equal((1, 3), (b.ActiveStudents, b.Answers));
        Assert.All(b.Recent, r => Assert.Equal("Caio", r.StudentName));
    }

    [Fact]
    public async Task Atividade_recente_vem_da_mais_nova_para_a_mais_antiga()
    {
        var result = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, null));

        Assert.Equal(result.Recent.OrderByDescending(r => r.AnsweredAt), result.Recent);
    }

    [Fact]
    public async Task Pagina_sem_respostas_mostra_estado_vazio_e_nao_erro()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        Guid emptyUnit;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = new Unit("Unidade Vazia");
            db.Units.Add(unit);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            emptyUnit = unit.Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Master);
        var response = await client.GetAsync($"/Dashboard?unidade={emptyUnit}", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nenhuma resposta neste período.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pagina_com_dados_mostra_cartoes_dos_cinco_tipos()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await client.GetAsync("/Dashboard?dias=90", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alunos ativos", html, StringComparison.Ordinal);
        foreach (var label in new[] { "Ambiental", "Conhecimento", "Esportivo", "Cultural", "Diversidade" })
        {
            Assert.Contains(label, html, StringComparison.Ordinal);
        }
    }

    private async Task<DashboardResult> RunAsync(string role, Guid? unitId, DashboardQuery query)
    {
        var user = new FakeUser(role, unitId);
        await using var context = NewContext(user);
        IAppDbContext db = context;
        return await GetDashboard.HandleAsync(db, user, new FixedClock(), query, TestContext.Current.CancellationToken);
    }

    private static async Task AddAnswersAsync(AppDbContext db, Student student, QuizType type, (bool Correct, int DaysAgo)[] answers, CancellationToken ct)
    {
        var session = new QuizSession(student.Id, type, 10, Now.AddDays(-answers.Max(a => a.DaysAgo)));
        db.QuizSessions.Add(session);
        var questions = answers.Select(_ => new Question(type, AgeBand.Age6To7, "Pergunta sintética?", "Explicação sintética.",
            [("A", true), ("B", false), ("C", false), ("D", false)])).ToList();
        db.Questions.AddRange(questions);
        db.QuizAnswers.AddRange(answers.Zip(questions, (a, q) => new QuizAnswer(session.Id, q.Id, "a", a.Correct, 0, Now.AddDays(-a.DaysAgo))));
        await db.SaveChangesAsync(ct);
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, user);

    private static Student NewStudent(Guid userId, Guid unitId, string name, DateOnly birth) =>
        new(userId, unitId, $"DASH-{name}", name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1", birth, "Responsável Fictício", "5A", Today);
}
