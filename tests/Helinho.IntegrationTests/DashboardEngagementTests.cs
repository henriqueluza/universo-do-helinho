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
/// Painel Recreio (#132): tempo médio por sessão, sessões por aluno, sem jogar há 7+ dias e missões concluídas.
/// "Agora" é 10/nov 12:00 em Brasília: período de 7 dias = 4 a 10/nov; anterior = 28/out a 3/nov.
/// Unidade A: Ana (7 anos) e Bia (12); unidade B: Caio (7) e Dani (13, nunca jogou). Cada sessão tem uma resposta no início.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardEngagementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly Trend NoBaseline = new(TrendDirection.None, Trend.NoBaseline);
    private static readonly Trend Flat = new(TrendDirection.Flat, Trend.Stable);
    private static readonly Indicator NoAverage = new(null, null, new Trend(TrendDirection.None, GetDashboardEngagement.NoCompletedSessions));
    private static readonly Indicator NoSessions = new(null, null, new Trend(TrendDirection.None, GetDashboardEngagement.NoSessions));
    private readonly string _database = $"engagement_{Guid.NewGuid():N}";
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
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(ct);
        var users = Enumerable.Range(0, 4).Select(i => new AppUser { UserName = $"engagement{i}" }).ToArray();
        db.Users.AddRange(users);
        db.Units.AddRange(_unitA, _unitB);
        var ana = NewStudent(users[0].Id, _unitA.Id, "Ana", new DateOnly(2019, 3, 1));
        var bia = NewStudent(users[1].Id, _unitA.Id, "Bia", new DateOnly(2014, 3, 1));
        var caio = NewStudent(users[2].Id, _unitB.Id, "Caio", new DateOnly(2019, 3, 1));
        var dani = NewStudent(users[3].Id, _unitB.Id, "Dani", new DateOnly(2013, 3, 1));
        db.Students.AddRange(ana, bia, caio, dani);

        Session(db, ana, At(11, 10, 9), 10);
        Session(db, ana, At(11, 9, 21), 20); // concluída 21:20 do dia 9 de Brasília (00:20 UTC do dia 10)
        Session(db, ana, At(11, 9, 8), null); // abandonada: conta como iniciada, fica fora do tempo médio
        Session(db, ana, At(11, 3, 23, 50), 20); // iniciada no período anterior, concluída 00:10 do dia 4 (período atual)
        Session(db, ana, At(10, 29, 10), 30);
        Session(db, bia, At(10, 30, 10), 10 * 24 * 60); // retomada dez dias depois (9/nov): longa demais, fora do tempo médio
        Session(db, bia, At(10, 31, 10), 10);
        Session(db, caio, At(11, 9, 23, 30), 30); // 02:30 UTC do dia 10: iniciada no dia 9 de Brasília, concluída 00:00 do dia 10
        Session(db, caio, At(11, 1, 8), 40);

        var (quest, harder) = (new DailyQuest("Missão sintética", 3, 20, null), new DailyQuest("Missão longa sintética", 5, 20, null));
        db.DailyQuests.AddRange(quest, harder);
        db.QuestProgress.AddRange(
            new QuestProgress(ana.Id, quest.Id, new DateOnly(2026, 11, 10), 3),
            new QuestProgress(ana.Id, harder.Id, new DateOnly(2026, 11, 10), 3), // 3 de 5: não concluída
            new QuestProgress(ana.Id, quest.Id, new DateOnly(2026, 11, 8), 3),
            new QuestProgress(ana.Id, quest.Id, new DateOnly(2026, 11, 7), 2), // 2 de 3: não concluída
            new QuestProgress(caio.Id, quest.Id, new DateOnly(2026, 11, 9), 3),
            new QuestProgress(ana.Id, quest.Id, new DateOnly(2026, 10, 29), 3),
            new QuestProgress(caio.Id, quest.Id, new DateOnly(2026, 11, 1), 3),
            new QuestProgress(ana.Id, quest.Id, new DateOnly(2026, 10, 27), 3)); // antes do período anterior: só nos 30 dias
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
        // concluídas no período: 10 + 20 + 20 + 30 = 80 min ÷ 4; antes: 30 + 10 + 40 = 80 ÷ 3 = 26,7
        Assert.Equal(new Indicator(20, 27, new Trend(TrendDirection.Down, "−7 min")), r.AverageSessionMinutes);
        // iniciadas: 4 sessões ÷ 2 alunos = 2,0; antes: 5 ÷ 3 = 1,7
        Assert.Equal(new Indicator(2, 2, new Trend(TrendDirection.Up, "+0,3")), r.SessionsPerStudent);
        Assert.Equal(new Indicator(2, 1, new Trend(TrendDirection.Up, "+1")), r.InactiveStudents); // Bia e Dani; na véspera, só Dani
        Assert.Equal(new Indicator(3, 2, new Trend(TrendDirection.Up, "+50%")), r.CompletedQuests);
    }

    [Fact]
    public async Task Serie_diaria_tem_sete_pontos_no_dia_civil_de_Brasilia()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, null)); // sempre os 7 últimos dias

        Assert.Equal(Enumerable.Range(4, 7).Select(d => new DateOnly(2026, 11, d)), r.LastDays.Select(d => d.Day));
        Assert.Equal([20, null, null, null, null, 20, 20], r.LastDays.Select(d => d.AverageSessionMinutes));
        Assert.Equal([null, null, null, null, null, 1.5m, 1.0m], r.LastDays.Select(d => d.SessionsPerStudent)); // dia 9: 3 sessões ÷ 2 alunos
        Assert.Equal([1, 1, 1, 2, 3, 2, 2], r.LastDays.Select(d => d.InactiveStudents));
        Assert.Equal([0, 0, 0, 0, 1, 1, 1], r.LastDays.Select(d => d.CompletedQuests));
    }

    [Fact]
    public async Task Admin_so_ve_a_propria_unidade_em_todos_os_indicadores_e_nao_escolhe_outra()
    {
        var own = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, null));
        var forced = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, _unitB.Id));
        var semUnidade = await RunAsync(RoleNames.Admin, null, new DashboardQuery(null, 7, _unitB.Id));

        foreach (var r in new[] { own, forced })
        {
            Assert.Equal(new Indicator(17, 20, new Trend(TrendDirection.Down, "−3 min")), r.AverageSessionMinutes); // 50 ÷ 3; a aberta não entra
            Assert.Equal(new Indicator(3, 2, new Trend(TrendDirection.Up, "+1,0")), r.SessionsPerStudent); // 3 ÷ 1 contra 4 ÷ 2
            Assert.Equal(new Indicator(1, 0, new Trend(TrendDirection.Up, "+1")), r.InactiveStudents);
            Assert.Equal(new Indicator(2, 1, new Trend(TrendDirection.Up, "+100%")), r.CompletedQuests);
            Assert.Equal([0, 0, 0, 1, 1, 1, 1], r.LastDays.Select(d => d.InactiveStudents)); // Caio e Dani nunca entram
            Assert.Equal([0, 0, 0, 0, 1, 0, 1], r.LastDays.Select(d => d.CompletedQuests)); // a do dia 9 é do Caio
            Assert.Equal([null, null, null, null, null, 2.0m, 1.0m], r.LastDays.Select(d => d.SessionsPerStudent));
        }

        Assert.Equal((NoAverage, NoSessions, true), (semUnidade.AverageSessionMinutes, semUnidade.SessionsPerStudent, semUnidade.IsEmpty));
        Assert.Equal((new Indicator(0, 0, Flat), new Indicator(0, 0, NoBaseline)), (semUnidade.InactiveStudents, semUnidade.CompletedQuests));
    }

    [Fact]
    public async Task Master_restringe_todos_os_indicadores_pelo_filtro_de_unidade()
    {
        var b = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, _unitB.Id));

        Assert.Equal(new Indicator(30, 40, new Trend(TrendDirection.Down, "−10 min")), b.AverageSessionMinutes);
        Assert.Equal(new Indicator(1, 1, Flat), b.SessionsPerStudent);
        Assert.Equal(new Indicator(1, 1, Flat), b.InactiveStudents); // Dani nos dois momentos
        Assert.Equal(new Indicator(1, 1, Flat), b.CompletedQuests);
        Assert.Equal([1, 1, 1, 1, 2, 1, 1], b.LastDays.Select(d => d.InactiveStudents));
    }

    [Fact]
    public async Task Sessao_aberta_ou_longa_demais_nao_entra_no_tempo_medio_e_nao_divide_por_zero()
    {
        var bia = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age11To12, 7, null));

        // a única sessão que a Bia concluiu no período durou dez dias: sem média, em vez de 14.400 min
        Assert.Equal(new Indicator(null, 10, new Trend(TrendDirection.None, GetDashboardEngagement.NoCompletedSessions)), bia.AverageSessionMinutes);
        Assert.Equal(new Indicator(null, 2, new Trend(TrendDirection.None, GetDashboardEngagement.NoSessions)), bia.SessionsPerStudent);
        Assert.Equal(new Indicator(1, 0, new Trend(TrendDirection.Up, "+1")), bia.InactiveStudents);
        Assert.All(bia.LastDays, d => Assert.Null(d.AverageSessionMinutes));
    }

    [Fact]
    public async Task Periodo_anterior_zerado_diz_que_nao_ha_base_de_comparacao()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, null));

        Assert.Equal(new Indicator(23, null, NoBaseline), r.AverageSessionMinutes); // 160 min ÷ 7 concluídas
        Assert.Equal(new Indicator(3, null, NoBaseline), r.SessionsPerStudent); // 9 sessões ÷ 3 alunos
        Assert.Equal(new Indicator(2, 4, new Trend(TrendDirection.Down, "−2")), r.InactiveStudents); // na véspera ninguém tinha jogado
        Assert.Equal(new Indicator(6, 0, NoBaseline), r.CompletedQuests);
    }

    [Fact]
    public async Task Periodo_sem_dados_devolve_vazio_bem_definido()
    {
        var none = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age13To14, 7, null)); // só a Dani

        Assert.Equal((NoAverage, NoSessions, true), (none.AverageSessionMinutes, none.SessionsPerStudent, none.IsEmpty));
        Assert.Equal((new Indicator(1, 1, Flat), new Indicator(0, 0, NoBaseline)), (none.InactiveStudents, none.CompletedQuests));
        Assert.Equal(7, none.LastDays.Count);
        Assert.All(none.LastDays, d => Assert.Equal(new DailyEngagement(d.Day, null, null, 1, 0), d));
    }

    [Fact]
    public async Task Agregacao_acontece_no_banco_em_seis_consultas_sem_dado_cadastral()
    {
        _sql.Clear();
        await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(AgeBand.Age6To7, 90, null));

        Assert.Equal(6, _sql.Count);
        Assert.All(_sql, sql => Assert.Contains("count(", sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, _sql.Count(sql => sql.Contains("GROUP BY", StringComparison.Ordinal))); // a sexta é o total de alunos do escopo
        Assert.All(_sql, sql => Assert.Contains("\"UnitId\" = @", sql, StringComparison.Ordinal));
        Assert.All(_sql, sql => Assert.DoesNotContain("FullName", sql, StringComparison.Ordinal));
    }

    private async Task<DashboardEngagement> RunAsync(string role, Guid? unitId, DashboardQuery query)
    {
        var user = new FakeUser(role, unitId);
        await using var db = NewContext(user);
        return await GetDashboardEngagement.HandleAsync(db, user, new FixedClock(), query, TestContext.Current.CancellationToken);
    }

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) =>
        BrasiliaTime.ToUtc(new DateTime(2026, month, day, hour, minute, 0));

    private static void Session(AppDbContext db, Student student, DateTimeOffset startedAt, int? minutes)
    {
        var session = new QuizSession(student.Id, QuizType.Ambiental, 10, startedAt);
        if (minutes is { } m)
        {
            session.Complete(startedAt.AddMinutes(m));
        }

        var question = new Question(QuizType.Ambiental, AgeBand.Age6To7, "Pergunta sintética?", "Explicação sintética.",
            [("A", true), ("B", false), ("C", false), ("D", false)]);
        db.QuizSessions.Add(session);
        db.Questions.Add(question);
        db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, "a", true, 0, startedAt));
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .LogTo(_sql.Add, [RelationalEventId.CommandExecuted])
            .Options, user);

    private static Student NewStudent(Guid userId, Guid unitId, string name, DateOnly birth) =>
        new(userId, unitId, $"ENG-{name}", name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1", birth,
            "Responsável Fictício", "5A", DateOnly.FromDateTime(Now.UtcDateTime));
}
