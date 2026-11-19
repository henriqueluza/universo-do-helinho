using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Helinho.IntegrationTests;

/// <summary>
/// Painel Recreio (#131): indicadores com variação, respostas por dia e acertos por tipo. "Agora" é 10/nov 12:00 em
/// Brasília: período de 7 dias = 4 a 10/nov; anterior = 28/out a 3/nov. Unidade A: Ana (7 anos) e Bia (12); B: Caio (7).
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardTrendsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly Trend NoBaseline = new(TrendDirection.None, Trend.NoBaseline);
    private static readonly Trend Flat = new(TrendDirection.Flat, Trend.Stable);
    private readonly string _database = $"trends_{Guid.NewGuid():N}";
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");
    private readonly List<string> _sql = [];

    private sealed class FakeUser(string role, Guid? unitId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public Guid? UnitId => unitId;
        public bool IsInRole(string r) => r == role;
    }
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(ct);
        var users = Enumerable.Range(0, 3).Select(i => new AppUser { UserName = $"trend{i}" }).ToArray();
        db.Users.AddRange(users);
        db.Units.AddRange(_unitA, _unitB);
        var ana = NewStudent(users[0].Id, _unitA.Id, "Ana", new DateOnly(2019, 3, 1));
        var bia = NewStudent(users[1].Id, _unitA.Id, "Bia", new DateOnly(2014, 3, 1));
        var caio = NewStudent(users[2].Id, _unitB.Id, "Caio", new DateOnly(2019, 3, 1));
        db.Students.AddRange(ana, bia, caio);

        Add(db, ana, QuizType.Ambiental,
            (true, At(11, 10, 9)), (false, At(11, 10, 10)),
            (true, At(11, 4, 0)), // 00:00 do primeiro dia: dentro (limite inferior inclusivo)
            (false, At(11, 3, 23, 59)), // um minuto antes: período anterior
            (true, At(10, 27, 23, 59)), // antes do período anterior: só entra nos 30 dias
            (true, At(11, 11, 0))); // 00:00 de amanhã: fora (limite superior exclusivo)
        Add(db, bia, QuizType.Esportivo, (true, At(11, 9, 23, 30))); // 02:30 UTC do dia 10: conta no dia 9 de Brasília
        Add(db, bia, QuizType.Cultural, (true, At(10, 28, 0))); // 00:00 do primeiro dia do período anterior: dentro
        Add(db, caio, QuizType.Ambiental,
            (true, At(11, 10, 8)), (true, At(11, 8, 8)), (false, At(11, 8, 9)), (true, At(10, 30, 10)), (true, At(10, 30, 11)));
        await db.SaveChangesAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Master_ve_o_agregado_das_unidades_com_variacao_sobre_o_periodo_anterior()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));

        Assert.Equal((new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 10), false), (r.From, r.To, r.IsEmpty));
        Assert.Equal(new Indicator(7, 4, new Trend(TrendDirection.Up, "+75%")), r.Answers);
        Assert.Equal(new Indicator(5, 3, new Trend(TrendDirection.Up, "+67%")), r.CorrectAnswers);
        Assert.Equal(new Indicator(71, 75, new Trend(TrendDirection.Down, "−4 pts")), r.AccuracyPercent);
        Assert.Equal(new Indicator(3, 3, Flat), r.ActiveStudents);
    }

    [Fact]
    public async Task Respostas_por_dia_usam_o_dia_civil_de_Brasilia_e_alinham_o_periodo_anterior()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));
        var late = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null), At(11, 9, 23, 30)); // já é dia 10 em UTC

        Assert.Equal((new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9)), (late.From, late.To)); // "hoje" é o dia de Brasília
        Assert.Equal(new Indicator(5, 4, new Trend(TrendDirection.Up, "+25%")), late.Answers);
        Assert.Equal([1, 0, 0, 0, 2, 1, 3], r.AnswersByDay.Select(d => d.Answers));
        Assert.Equal([1, 0, 0, 0, 1, 1, 2], r.AnswersByDay.Select(d => d.Correct));
        Assert.Equal([1, 0, 2, 0, 0, 0, 1], r.AnswersByDay.Select(d => d.PreviousAnswers));
        Assert.Equal(new DailyAnswers(new(2026, 11, 4), 1, 1, new(2026, 10, 28), 1), r.AnswersByDay[0]);
        Assert.Equal(new DailyAnswers(new(2026, 11, 10), 3, 2, new(2026, 11, 3), 1), r.AnswersByDay[6]);
        Assert.Equal((67, null), (r.AnswersByDay[6].AccuracyPercent, r.AnswersByDay[1].AccuracyPercent));
    }

    [Fact]
    public async Task Acertos_por_tipo_trazem_os_cinco_tipos_so_com_o_periodo_atual()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));

        Assert.Equal(Enum.GetValues<QuizType>(), r.AccuracyByType.Select(a => a.QuizType));
        Assert.Equal(new QuizTypeAccuracy(QuizType.Ambiental, 6, 67), r.AccuracyByType[0]);
        Assert.Equal(new QuizTypeAccuracy(QuizType.Esportivo, 1, 100), r.AccuracyByType[2]);
        Assert.Equal(new QuizTypeAccuracy(QuizType.Cultural, 0, null), r.AccuracyByType[3]); // só respondeu no período anterior
    }

    [Fact]
    public async Task Admin_so_ve_a_propria_unidade_em_todos_os_blocos_e_nao_escolhe_outra()
    {
        var own = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, null));
        var forced = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, _unitB.Id));
        var semUnidade = await RunAsync(RoleNames.Admin, null, new DashboardQuery(null, 7, _unitB.Id));

        foreach (var r in new[] { own, forced })
        {
            Assert.Equal(new Indicator(4, 2, new Trend(TrendDirection.Up, "+100%")), r.Answers);
            Assert.Equal(new Indicator(75, 50, new Trend(TrendDirection.Up, "+25 pts")), r.AccuracyPercent);
            Assert.Equal(new Indicator(2, 2, Flat), r.ActiveStudents);
            Assert.Equal([1, 0, 0, 0, 0, 1, 2], r.AnswersByDay.Select(d => d.Answers)); // o dia 8 é só do Caio (unidade B)
            Assert.Equal([1, 0, 0, 0, 0, 0, 1], r.AnswersByDay.Select(d => d.PreviousAnswers));
            Assert.Equal(new QuizTypeAccuracy(QuizType.Ambiental, 3, 67), r.AccuracyByType[0]);
        }

        Assert.Equal((true, 0), (semUnidade.IsEmpty, semUnidade.ActiveStudents.Value));
    }

    [Fact]
    public async Task Master_restringe_todos_os_blocos_pelo_filtro_de_unidade()
    {
        var b = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, _unitB.Id));

        Assert.Equal(new Indicator(3, 2, new Trend(TrendDirection.Up, "+50%")), b.Answers);
        Assert.Equal(new Indicator(67, 100, new Trend(TrendDirection.Down, "−33 pts")), b.AccuracyPercent);
        Assert.Equal(new Indicator(1, 1, Flat), b.ActiveStudents);
        Assert.Equal([0, 0, 0, 0, 2, 0, 1], b.AnswersByDay.Select(d => d.Answers));
        Assert.Equal(new QuizTypeAccuracy(QuizType.Ambiental, 3, 67), b.AccuracyByType[0]);
    }

    [Fact]
    public async Task Periodo_anterior_zerado_nao_divide_por_zero_e_diz_que_nao_ha_base()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, null));

        Assert.Equal(new Indicator(12, 0, NoBaseline), r.Answers);
        Assert.Equal(new Indicator(75, null, NoBaseline), r.AccuracyPercent);
        Assert.Equal(new Indicator(3, 0, NoBaseline), r.ActiveStudents);
        Assert.Equal(30, r.AnswersByDay.Count);
        Assert.All(r.AnswersByDay, d => Assert.Equal(0, d.PreviousAnswers));
    }

    [Fact]
    public async Task Sem_respostas_devolve_vazio_bem_definido_e_a_faixa_etaria_filtra()
    {
        var none = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age13To14, 7, null));
        var young = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age6To7, 7, null));

        Assert.Equal(new Indicator(0, 0, NoBaseline), none.Answers);
        Assert.Equal(new Indicator(null, null, new Trend(TrendDirection.None, Trend.NoData)), none.AccuracyPercent);
        Assert.Equal((true, 7), (none.IsEmpty, none.AnswersByDay.Count));
        Assert.All(none.AnswersByDay, d => Assert.Equal((0, 0, 0), (d.Answers, d.Correct, d.PreviousAnswers)));
        Assert.All(none.AccuracyByType, a => Assert.Equal((0, null), (a.Answers, a.AccuracyPercent)));
        Assert.Equal((6, 2), (young.Answers.Value, young.ActiveStudents.Value)); // Ana e Caio; Bia (12 anos) fica de fora
    }

    [Fact]
    public async Task Agregacao_acontece_no_banco_em_duas_consultas()
    {
        _sql.Clear();
        await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(AgeBand.Age6To7, 90, null));

        Assert.Equal(2, _sql.Count);
        Assert.All(_sql, sql => Assert.Contains("GROUP BY", sql, StringComparison.Ordinal));
        Assert.Contains("AT TIME ZONE", _sql[0], StringComparison.Ordinal);
        Assert.Contains("count(DISTINCT", _sql[1], StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DashboardTrends> RunAsync(string role, Guid? unitId, DashboardQuery query, DateTimeOffset? now = null)
    {
        var user = new FakeUser(role, unitId);
        await using var db = NewContext(user);
        return await GetDashboardTrends.HandleAsync(db, user, new FixedClock(now ?? Now), query, TestContext.Current.CancellationToken);
    }

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) =>
        BrasiliaTime.ToUtc(new DateTime(2026, month, day, hour, minute, 0));

    private static void Add(AppDbContext db, Student student, QuizType type, params (bool Correct, DateTimeOffset At)[] answers)
    {
        var session = new QuizSession(student.Id, type, 10, answers.Min(a => a.At));
        db.QuizSessions.Add(session);
        foreach (var (correct, at) in answers)
        {
            var question = new Question(type, AgeBand.Age6To7, "Pergunta sintética?", "Explicação sintética.",
                [("A", true), ("B", false), ("C", false), ("D", false)]);
            db.Questions.Add(question);
            db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, "a", correct, 0, at));
        }
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .LogTo(_sql.Add, [RelationalEventId.CommandExecuted])
            .Options, user);

    private static Student NewStudent(Guid userId, Guid unitId, string name, DateOnly birth) =>
        new(userId, unitId, $"TREND-{name}", name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1", birth,
            "Responsável Fictício", "5A", DateOnly.FromDateTime(Now.UtcDateTime));
}
