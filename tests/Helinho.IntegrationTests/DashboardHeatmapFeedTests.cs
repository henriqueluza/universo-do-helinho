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
/// Painel Recreio (#134): mapa de calor dia × turno e "Acabou de acontecer". "Agora" é terça, 10/nov/2026, 20:00 em
/// Brasília: período de 7 dias = quarta 4 a terça 10/nov; janela do feed = segunda 9/nov 20:00 a terça 10/nov 20:00.
/// Unidade A: Ana (7 anos) e Bia (12); unidade B: Caio (7). Dados sintéticos.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardHeatmapFeedTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const DayShift Morning = DayShift.Morning, Afternoon = DayShift.Afternoon, Night = DayShift.Night;
    private static readonly DateTimeOffset Now = At(10, 20);
    private static readonly DashboardQuery Week = new(null, 7, null);
    private static readonly string[] Forbidden =
        ["Souza", "Lima", "Ferreira", "Pereira", "MAT-", "11.111.111-1", "222.222.222-22", "Rua Sintética", "Responsável", "@", "5A"];
    private readonly string _database = $"calor_{Guid.NewGuid():N}";
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");
    private readonly List<string> _sql = [];

    private sealed record FakeUser(string Role, Guid? UnitId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public bool IsInRole(string role) => role == Role;
    }
    private sealed record FixedClock(DateTimeOffset UtcNow) : IClock;

    public async ValueTask InitializeAsync()
    {
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var users = Enumerable.Range(0, 3).Select(i => new AppUser { UserName = $"calor{i}", Email = $"calor{i}@exemplo.test" }).ToArray();
        db.Users.AddRange(users);
        db.Units.AddRange(_unitA, _unitB);
        var ana = NewStudent(users[0].Id, _unitA.Id, "Ana Souza Lima", new DateOnly(2019, 3, 1));
        var bia = NewStudent(users[1].Id, _unitA.Id, "Bia Ferreira", new DateOnly(2014, 3, 1));
        var caio = NewStudent(users[2].Id, _unitB.Id, "Caio Pereira", new DateOnly(2019, 3, 1));
        db.Students.AddRange(ana, bia, caio);
        var (passo, explorador) = (new Badge("calor-passo", "Primeiro Passo", ""), new Badge("calor-explorador", "Explorador", ""));
        db.Badges.AddRange(passo, explorador);

        Add(db, ana, QuizType.Ambiental, (true, At(10, 0)), (true, At(10, 5, 59)), (false, At(10, 6)), (true, At(10, 11, 59)), (false, At(10, 12)))
            .Complete(At(10, 12)); // 3 de 5; fronteiras de turno na terça: 00:00 e 05:59 noite, 06:00 e 11:59 manhã, 12:00 tarde
        Add(db, ana, QuizType.Esportivo, (true, At(7, 17, 59)), (false, At(7, 18))).Complete(At(7, 18)); // sábado: tarde e noite; concluída fora da janela
        Add(db, ana, QuizType.Diversidade, (true, At(9, 20))).Complete(At(9, 20)); // concluída no primeiro instante da janela: dentro
        Add(db, ana, QuizType.Conhecimento, (true, At(9, 23, 30))); // 02:30 UTC de terça: segunda à noite; sessão aberta
        Add(db, bia, QuizType.Cultural, (true, At(10, 6, 30)), (true, At(10, 7)), (false, At(5, 8)), (true, At(5, 9))).Complete(At(10, 7));
        Add(db, bia, QuizType.Conhecimento).Complete(At(10, 18)); // encerrada sem nenhuma resposta: não vira evento
        Add(db, caio, QuizType.Ambiental, (true, At(4, 0)), (true, At(7, 18, 30)), (true, At(7, 19)), (false, At(7, 20)), (true, At(7, 21)))
            .Complete(At(10, 19)); // 4 de 5; 4/nov 00:00 é o primeiro instante do período: dentro
        Add(db, caio, QuizType.Conhecimento, (true, At(3, 23, 59)), (true, At(11, 0))); // fora do período pelos dois lados
        db.StudentBadges.AddRange(
            new StudentBadge(bia.Id, passo.Id, At(10, 7)), // mesmo instante do quiz da Bia: o quiz vem antes
            new StudentBadge(caio.Id, explorador.Id, At(10, 19, 30)),
            new StudentBadge(ana.Id, passo.Id, At(9, 19, 59)), // um minuto antes da janela
            new StudentBadge(ana.Id, explorador.Id, At(10, 20, 1))); // depois de "agora"
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Mapa_do_Master_tem_as_21_celulas_com_dia_e_turno_de_Brasilia_e_o_pico()
    {
        var r = await HeatAsync(RoleNames.Master, null, Week);

        Assert.Equal((new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 10), false, 21), (r.From, r.To, r.IsEmpty, r.Cells.Count));
        Assert.Equal((DayOfWeek.Monday, Morning), (r.Cells[0].Day, r.Cells[0].Shift)); // uma linha por turno, de segunda a domingo
        Assert.Equal((DayOfWeek.Sunday, Night), (r.Cells[20].Day, r.Cells[20].Shift));
        Assert.Equal(21, r.Cells.Select(c => (c.Day, c.Shift)).Distinct().Count());
        Assert.Equal(new HeatmapCell(DayOfWeek.Monday, Night, 2, 0.4), Cell(r, DayOfWeek.Monday, Night)); // 20:00 e 23:30 de segunda
        Assert.Equal(new HeatmapCell(DayOfWeek.Tuesday, Night, 2, 0.4), Cell(r, DayOfWeek.Tuesday, Night)); // 00:00 e 05:59
        Assert.Equal(new HeatmapCell(DayOfWeek.Tuesday, Morning, 4, 0.8), Cell(r, DayOfWeek.Tuesday, Morning)); // 06:00, 06:30, 07:00, 11:59
        Assert.Equal(new HeatmapCell(DayOfWeek.Tuesday, Afternoon, 1, 0.2), Cell(r, DayOfWeek.Tuesday, Afternoon)); // 12:00
        Assert.Equal(new HeatmapCell(DayOfWeek.Saturday, Afternoon, 1, 0.2), Cell(r, DayOfWeek.Saturday, Afternoon)); // 17:59
        Assert.Equal(new HeatmapCell(DayOfWeek.Wednesday, Night, 1, 0.2), Cell(r, DayOfWeek.Wednesday, Night)); // 4/nov 00:00; 11/nov 00:00 fora
        Assert.Equal(new HeatmapCell(DayOfWeek.Saturday, Night, 5, 1), r.Peak); // 18:00 em diante
        Assert.Equal((18, 13), (r.Cells.Sum(c => c.Answers), r.Cells.Count(c => c is { Answers: 0, Intensity: 0 })));
    }

    [Fact]
    public async Task Mapa_usa_o_dia_de_Brasilia_quando_ja_virou_o_dia_em_UTC()
    {
        var r = await HeatAsync(RoleNames.Master, null, Week, At(9, 23, 30)); // 02:30 UTC de terça; em Brasília ainda é segunda

        Assert.Equal((new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9), 12), (r.From, r.To, r.Cells.Sum(c => c.Answers)));
        Assert.Equal((2, 1, 0), // entra 3/nov 23:59 (terça à noite); a terça 10/nov fica de fora
            (Cell(r, DayOfWeek.Monday, Night).Answers, Cell(r, DayOfWeek.Tuesday, Night).Answers, Cell(r, DayOfWeek.Tuesday, Morning).Answers));
    }

    [Fact]
    public async Task Mapa_separa_unidades_e_respeita_os_filtros()
    {
        var a = await HeatAsync(RoleNames.Admin, _unitA.Id, Week with { UnitId = _unitB.Id }); // administrador não escolhe outra unidade
        var b = await HeatAsync(RoleNames.Master, null, Week with { UnitId = _unitB.Id });
        var bia = await HeatAsync(RoleNames.Master, null, Week with { AgeBand = AgeBand.Age11To12 });

        Assert.Equal((13, new HeatmapCell(DayOfWeek.Tuesday, Morning, 4, 1)), (a.Cells.Sum(c => c.Answers), a.Peak));
        Assert.Equal(new HeatmapCell(DayOfWeek.Saturday, Night, 1, 0.25), Cell(a, DayOfWeek.Saturday, Night)); // as outras 4 são do Caio
        Assert.Equal(0, Cell(a, DayOfWeek.Wednesday, Night).Answers);
        Assert.Equal((5, new HeatmapCell(DayOfWeek.Saturday, Night, 4, 1)), (b.Cells.Sum(c => c.Answers), b.Peak));
        Assert.Equal(new HeatmapCell(DayOfWeek.Wednesday, Night, 1, 0.25), Cell(b, DayOfWeek.Wednesday, Night));
        Assert.Equal((4, 1.0), (bia.Cells.Sum(c => c.Answers), Cell(bia, DayOfWeek.Thursday, Morning).Intensity));
        Assert.Equal(new HeatmapCell(DayOfWeek.Tuesday, Morning, 2, 1), bia.Peak); // empate com quinta de manhã: vale a primeira da grade
    }

    [Fact]
    public async Task Feed_do_Master_traz_os_cinco_eventos_mais_recentes_em_ordem()
    {
        var r = await FeedAsync(RoleNames.Master, null, Week);

        Assert.Equal(
        [
            new FeedEvent(FeedEventKind.BadgeUnlocked, "Caio", "Unidade B", "ganhou o selo Explorador", null, At(10, 19, 30)),
            new FeedEvent(FeedEventKind.QuizCompleted, "Caio", "Unidade B", "acertou 4 de 5 no quiz", QuizType.Ambiental, At(10, 19)),
            new FeedEvent(FeedEventKind.QuizCompleted, "Ana", "Unidade A", "acertou 3 de 5 no quiz", QuizType.Ambiental, At(10, 12)),
            new FeedEvent(FeedEventKind.QuizCompleted, "Bia", "Unidade A", "acertou 3 de 4 no quiz", QuizType.Cultural, At(10, 7)),
            new FeedEvent(FeedEventKind.BadgeUnlocked, "Bia", "Unidade A", "ganhou o selo Primeiro Passo", null, At(10, 7)),
        ], r.Events); // o sexto (quiz da Ana, 9/nov 20:00) cai pelo limite
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public async Task Feed_separa_unidades_respeita_os_filtros_e_a_janela_de_24_horas()
    {
        var a = await FeedAsync(RoleNames.Admin, _unitA.Id, Week with { UnitId = _unitB.Id }); // administrador não escolhe outra unidade
        var b = await FeedAsync(RoleNames.Master, null, Week with { UnitId = _unitB.Id });
        var young = await FeedAsync(RoleNames.Master, null, Week with { AgeBand = AgeBand.Age6To7 });
        var later = await FeedAsync(RoleNames.Master, null, Week, At(11, 19, 1)); // na janela seguinte ficam só os selos de 20:01 (Ana) e 19:30 (Caio)

        Assert.All(a.Events, e => Assert.Equal("Unidade A", e.UnitName));
        Assert.Equal(["acertou 3 de 5 no quiz", "acertou 3 de 4 no quiz", "ganhou o selo Primeiro Passo", "acertou 1 de 1 no quiz"], a.Events.Select(e => e.Text));
        Assert.Equal(new FeedEvent(FeedEventKind.QuizCompleted, "Ana", "Unidade A", "acertou 1 de 1 no quiz", QuizType.Diversidade, At(9, 20)), a.Events[3]);
        Assert.Equal(["Caio", "Caio"], b.Events.Select(e => e.FirstName));
        Assert.Equal(["Caio", "Caio", "Ana", "Ana"], young.Events.Select(e => e.FirstName)); // Bia (12 anos) fica de fora
        Assert.Equal(["Ana", "Caio"], later.Events.Select(e => e.FirstName));
    }

    /// <summary>T4 do ADR-0001: do aluno saem só o primeiro nome e a unidade.</summary>
    [Fact]
    public async Task Feed_nao_devolve_sobrenome_matricula_documento_endereco_responsavel_nem_email()
    {
        var r = await FeedAsync(RoleNames.Master, null, Week);
        var text = string.Join('|', r.Events); // o ToString do record imprime todas as propriedades

        Assert.Equal(["At", "FirstName", "Kind", "QuizType", "Text", "UnitName"], typeof(FeedEvent).GetProperties().Select(p => p.Name).Order());
        Assert.All(Forbidden, value => Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase));
        Assert.All(r.Events, e => Assert.DoesNotContain(' ', e.FirstName));
    }

    /// <summary>T1 do ADR-0001: contexto aberto como Master (filtro global sem restringir); só o escopo de Application separa as unidades.</summary>
    [Fact]
    public async Task Escopo_de_unidade_em_Application_vale_sem_o_filtro_global()
    {
        var (ct, clock, pedeB) = (TestContext.Current.CancellationToken, new FixedClock(Now), Week with { UnitId = _unitB.Id });
        var (adminA, semUnidade) = (new FakeUser(RoleNames.Admin, _unitA.Id), new FakeUser(RoleNames.Admin, null));
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));

        var heat = await GetDashboardHeatmap.HandleAsync(db, adminA, clock, pedeB, ct);
        var feed = await GetDashboardFeed.HandleAsync(db, adminA, clock, pedeB, ct);
        var heatSemUnidade = await GetDashboardHeatmap.HandleAsync(db, semUnidade, clock, pedeB, ct);
        var feedSemUnidade = await GetDashboardFeed.HandleAsync(db, semUnidade, clock, pedeB, ct);

        Assert.Equal((13, new HeatmapCell(DayOfWeek.Tuesday, Morning, 4, 1)), (heat.Cells.Sum(c => c.Answers), heat.Peak));
        Assert.Equal(["Ana", "Bia", "Bia", "Ana"], feed.Events.Select(e => e.FirstName));
        Assert.All(feed.Events, e => Assert.Equal("Unidade A", e.UnitName));
        Assert.Equal((true, true), (heatSemUnidade.IsEmpty, feedSemUnidade.IsEmpty));
    }

    [Fact]
    public async Task Sem_dados_o_mapa_devolve_21_zeros_sem_pico_e_o_feed_devolve_lista_vazia()
    {
        var teens = Week with { AgeBand = AgeBand.Age13To14 };
        var (heat, feed) = (await HeatAsync(RoleNames.Master, null, teens), await FeedAsync(RoleNames.Master, null, teens));
        var semUnidade = await FeedAsync(RoleNames.Admin, null, Week);

        Assert.Equal((true, null, 21), (heat.IsEmpty, heat.Peak, heat.Cells.Count));
        Assert.All(heat.Cells, c => Assert.Equal((0, 0d), (c.Answers, c.Intensity)));
        Assert.Equal((true, 0, 0), (feed.IsEmpty, feed.Events.Count, semUnidade.Events.Count));
    }

    [Fact]
    public async Task Mapa_agrega_no_banco_e_o_feed_limita_no_banco_sem_consulta_por_item()
    {
        _sql.Clear();
        await HeatAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(AgeBand.Age6To7, 90, null));
        var heat = Assert.Single(_sql);
        _sql.Clear();
        await FeedAsync(RoleNames.Master, null, Week);

        Assert.Contains("GROUP BY", heat, StringComparison.Ordinal);
        Assert.Contains("AT TIME ZONE 'America/Sao_Paulo'", heat, StringComparison.Ordinal);
        Assert.Equal(2, _sql.Count); // uma de quizzes e uma de selos, para qualquer número de itens
        Assert.All(_sql, sql => Assert.Contains("LIMIT", sql, StringComparison.Ordinal));
    }

    private Task<DashboardHeatmap> HeatAsync(string role, Guid? unitId, DashboardQuery query, DateTimeOffset? now = null) =>
        RunAsync(GetDashboardHeatmap.HandleAsync, new FakeUser(role, unitId), query, now ?? Now);

    private Task<DashboardFeed> FeedAsync(string role, Guid? unitId, DashboardQuery query, DateTimeOffset? now = null) =>
        RunAsync(GetDashboardFeed.HandleAsync, new FakeUser(role, unitId), query, now ?? Now);

    private async Task<T> RunAsync<T>(
        Func<IAppDbContext, ICurrentUser, IClock, DashboardQuery, CancellationToken, Task<T>> handle, FakeUser user, DashboardQuery query, DateTimeOffset now)
    {
        await using var db = NewContext(user);
        return await handle(db, user, new FixedClock(now), query, TestContext.Current.CancellationToken);
    }

    private static HeatmapCell Cell(DashboardHeatmap map, DayOfWeek day, DayShift shift) =>
        Assert.Single(map.Cells, c => c.Day == day && c.Shift == shift);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => BrasiliaTime.ToUtc(new DateTime(2026, 11, day, hour, minute, 0));

    private static QuizSession Add(AppDbContext db, Student student, QuizType type, params (bool Correct, DateTimeOffset At)[] answers)
    {
        var session = new QuizSession(student.Id, type, 10, At(1, 0));
        db.QuizSessions.Add(session);
        foreach (var (correct, at) in answers)
        {
            var question = new Question(type, AgeBand.Age6To7, "Pergunta sintética?", "Explicação sintética.",
                [("A", true), ("B", false), ("C", false), ("D", false)]);
            db.Questions.Add(question);
            db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, "a", correct, 0, at));
        }

        return session;
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .LogTo(_sql.Add, [RelationalEventId.CommandExecuted])
            .Options, user);

    private static Student NewStudent(Guid userId, Guid unitId, string name, DateOnly birth) =>
        new(userId, unitId, $"MAT-{name[..3]}", name, "11.111.111-1", "222.222.222-22", "Rua Sintética, 1", birth,
            "Responsável Fictício", "5A", DateOnly.FromDateTime(Now.UtcDateTime));
}
