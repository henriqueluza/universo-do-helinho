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
/// Painel Recreio (#136): "Precisa de atenção". "Agora" é 10/nov 12:00 em Brasília: período de 7 dias = 4 a 10/nov,
/// anterior = 28/out a 3/nov; "sem jogar" = sem resposta de 4 a 10/nov. Unidade A: Ana, Bia e Duda (5A), Hugo e Jade (5B);
/// B: Caio, Edu e Fabi (5A, 12 anos), Gui (5B); C: Iara. Os demais têm 7 anos. Sem rascunho nem evento na base.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardAttentionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = At(11, 10, 12);
    private static readonly AttentionAlert NoHistory = new(AttentionKind.LowAccuracy, 0, Trend.NoBaseline, QuizType.Diversidade);
    private static readonly AttentionAlert CultureA = new(AttentionKind.LowAccuracy, 60, "−40 pts", QuizType.Cultural); // só a unidade A: 3 de 5
    private readonly string _database = $"attention_{Guid.NewGuid():N}";
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");
    private readonly Unit _unitC = new("Unidade C");
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

    private AttentionAlert IdleA => new(AttentionKind.InactiveStudents, 2, "Unidade A, turma 5A", UnitId: _unitA.Id, ClassName: "5A");
    private AttentionAlert IdleB => new(AttentionKind.InactiveStudents, 3, "Unidade B, turma 5A", UnitId: _unitB.Id, ClassName: "5A");
    private static AttentionAlert Sports(string detail) => new(AttentionKind.LowAccuracy, 64, detail, QuizType.Esportivo);
    private static AttentionAlert Drafts(int count) => new(AttentionKind.DraftQuestions, count, GetDashboardAttention.DraftsDetail);

    public async ValueTask InitializeAsync()
    {
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        db.Units.AddRange(_unitA, _unitB, _unitC);
        var (young, older) = (new DateOnly(2019, 3, 1), new DateOnly(2014, 3, 1));
        NewStudent(db, _unitA, "Hugo", "5B", young); // nunca respondeu, em outra turma: prova o agrupamento por turma
        NewStudent(db, _unitB, "Edu", "5A", older);
        NewStudent(db, _unitB, "Fabi", "5A", older);
        var jade = NewStudent(db, _unitA, "Jade", "5B", young);
        var gui = NewStudent(db, _unitB, "Gui", "5B", young);

        Add(db, NewStudent(db, _unitA, "Ana", "5A", young), QuizType.Cultural, 1, 0, At(11, 3, 23, 59)); // um minuto antes da janela: sem jogar
        Add(db, NewStudent(db, _unitA, "Bia", "5A", young), QuizType.Cultural, 1, 0, At(11, 11, 0)); // 00:00 de amanhã: fora (exclusivo)
        Add(db, NewStudent(db, _unitA, "Duda", "5A", young), QuizType.Cultural, 3, 2, At(11, 4, 0)); // 00:00 do primeiro dia: jogou; 60%
        Add(db, jade, QuizType.Diversidade, 0, 5, At(11, 10, 9)); // 5 respostas: o mínimo exato
        Add(db, jade, QuizType.Conhecimento, 0, 4, At(11, 10, 9)); // 4 respostas: abaixo do mínimo, não alerta
        Add(db, NewStudent(db, _unitB, "Caio", "5A", older), QuizType.Esportivo, 7, 3, At(10, 30, 10)); // período anterior: 70%
        Add(db, gui, QuizType.Esportivo, 16, 9, At(11, 9, 23, 30)); // 64%: alerta
        Add(db, gui, QuizType.Ambiental, 13, 7, At(11, 8, 8)); // 65% exatos: não alerta
        Add(db, gui, QuizType.Cultural, 10, 0, At(11, 10, 8));
        Add(db, NewStudent(db, _unitC, "Iara", "5A", young), QuizType.Ambiental, 5, 0, At(11, 10, 8)); // unidade sem nada a alertar
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Master_ve_o_agregado_das_unidades_na_ordem_de_gravidade()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));

        Assert.Equal([IdleB, NoHistory, Sports("−6 pts")], r.Alerts); // Cultural no agregado: 13 de 15 = 87%
        Assert.Equal([AttentionTarget.Students, AttentionTarget.Performance, AttentionTarget.Performance], r.Alerts.Select(a => a.Target));
    }

    [Fact]
    public async Task Administrador_so_recebe_alertas_da_propria_unidade_e_nao_escolhe_outra()
    {
        var own = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, null));
        var forced = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, _unitB.Id));
        var semUnidade = await RunAsync(RoleNames.Admin, null, new DashboardQuery(null, 7, _unitB.Id));
        var late = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, null), At(11, 10, 23, 30)); // já é dia 11 em UTC

        Assert.Equal([IdleA, NoHistory, CultureA], own.Alerts);
        Assert.Equal(own.Alerts, forced.Alerts);
        Assert.Equal(own.Alerts, late.Alerts); // "hoje" é o dia de Brasília
        Assert.True(semUnidade.IsEmpty);
    }

    /// <summary>
    /// T1 do ADR-0001: o contexto abre como Master (filtro global de Student sem restringir), então só o escopo explícito
    /// de <c>GetDashboard.ScopedStudents</c> separa as unidades. Sem ele, o administrador receberia os alertas do agregado.
    /// </summary>
    [Fact]
    public async Task Escopo_de_unidade_em_Application_vale_sem_o_filtro_global()
    {
        var (ct, clock, pedeB) = (TestContext.Current.CancellationToken, new FixedClock(Now), new DashboardQuery(null, 7, _unitB.Id));
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));

        var daUnidadeA = await GetDashboardAttention.HandleAsync(db, new FakeUser(RoleNames.Admin, _unitA.Id), clock, pedeB, ct);
        var semUnidade = await GetDashboardAttention.HandleAsync(db, new FakeUser(RoleNames.Admin, null), clock, pedeB, ct);

        Assert.Equal([IdleA, NoHistory, CultureA], daUnidadeA.Alerts);
        Assert.Empty(semUnidade.Alerts);
    }

    [Fact]
    public async Task Master_restringe_pelo_filtro_de_unidade_e_pela_faixa_etaria()
    {
        var b = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, _unitB.Id));
        var young = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age6To7, 7, null));

        Assert.Equal([IdleB, Sports("−6 pts")], b.Alerts); // Ambiental da unidade B: 13 de 20 = 65% exatos, sem alerta
        Assert.Equal([IdleA, NoHistory, Sports(Trend.NoBaseline)], young.Alerts); // os 12 anos saem: turma 5A da B e o período anterior
    }

    [Fact]
    public async Task Sem_nada_para_alertar_devolve_lista_vazia()
    {
        var semAlunos = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age13To14, 7, null));
        var tudoEmDia = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, _unitC.Id)); // Iara jogou e acertou tudo

        Assert.All([semAlunos, tudoEmDia], r => Assert.Equal((true, 0), (r.IsEmpty, r.Alerts.Count)));
    }

    [Fact]
    public async Task Rascunhos_sao_conteudo_global_e_so_alertam_quando_existem()
    {
        await SeedAsync(db => db.Questions.Add(NewDraft()));
        var master = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age13To14, 7, _unitC.Id));
        await SeedAsync(db => db.Questions.Add(NewDraft()));
        var admin = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, null));
        var semUnidade = await RunAsync(RoleNames.Admin, null, new DashboardQuery(null, 7, null));

        Assert.Equal([Drafts(1)], master.Alerts); // ignora unidade e faixa etária
        Assert.Equal(AttentionTarget.Questions, master.Alerts[0].Target);
        Assert.Equal((4, Drafts(2)), (admin.Alerts.Count, admin.Alerts[3]));
        Assert.Equal([Drafts(2)], semUnidade.Alerts);
    }

    [Fact]
    public async Task Evento_alerta_so_dentro_da_antecedencia_e_escolhe_o_que_termina_primeiro()
    {
        var query = new DashboardQuery(null, 7, _unitC.Id);
        await SeedAsync(db => db.LiveEvents.AddRange(
            new LiveEvent("Já acabou", "", QuizType.Ambiental, Now), // termina agora: não é mais "em breve"
            new LiveEvent("Longe", "", QuizType.Ambiental, At(11, 14, 0)))); // 00:00 do quarto dia: fora (exclusivo)
        var none = await RunAsync(RoleNames.Master, null, query);
        await SeedAsync(db => db.LiveEvents.Add(new LiveEvent("Dia da Água", "", QuizType.Esportivo, At(11, 13, 23, 59))));
        var inThreeDays = await RunAsync(RoleNames.Admin, null, query);
        await SeedAsync(db => db.LiveEvents.Add(new LiveEvent("Festa Junina", "", QuizType.Cultural, At(11, 10, 12, 1))));
        var today = await RunAsync(RoleNames.Master, null, query);

        Assert.Empty(none.Alerts);
        Assert.Equal([new(AttentionKind.EventEnding, 3, "até 13/11 às 23:59", QuizType.Esportivo, EventTitle: "Dia da Água")], inThreeDays.Alerts);
        Assert.Equal([new(AttentionKind.EventEnding, 0, "até 10/11 às 12:01", QuizType.Cultural, EventTitle: "Festa Junina")], today.Alerts);
        Assert.Equal(AttentionTarget.Events, today.Alerts[0].Target);
    }

    [Fact]
    public async Task Lista_corta_no_limite_depois_de_ordenar_por_gravidade()
    {
        await SeedAsync(db => db.Questions.Add(NewDraft()));
        await SeedAsync(db => db.LiveEvents.Add(new LiveEvent("Dia da Água", "", QuizType.Esportivo, At(11, 12, 18))));
        var all = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));
        var b = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, _unitB.Id));

        Assert.Equal([IdleB, NoHistory, Sports("−6 pts"), Drafts(1)], all.Alerts); // o quinto (evento) não sai
        AttentionAlert ending = new(AttentionKind.EventEnding, 2, "até 12/11 às 18:00", QuizType.Esportivo, EventTitle: "Dia da Água");
        Assert.Equal([IdleB, Sports("−6 pts"), Drafts(1), ending], b.Alerts);
    }

    [Fact]
    public async Task Agregacao_acontece_no_banco_em_quatro_consultas_sem_dado_cadastral()
    {
        _sql.Clear();
        await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 90, null));

        Assert.Equal(4, _sql.Count);
        Assert.All(_sql[..2], sql => Assert.Contains("GROUP BY", sql, StringComparison.Ordinal));
        Assert.All(_sql[..2], sql => Assert.Contains("\"UnitId\" = @", sql, StringComparison.Ordinal));
        Assert.Contains("count(*)", _sql[2], StringComparison.OrdinalIgnoreCase);
        Assert.All(_sql, sql => Assert.All(
            (string[])["FullName", "Enrollment", "Cpf", "Rg", "Address", "GuardianName", "BirthDate"],
            column => Assert.DoesNotContain($"\"{column}\"", sql, StringComparison.Ordinal)));
    }

    private async Task<DashboardAttention> RunAsync(string role, Guid? unitId, DashboardQuery query, DateTimeOffset? now = null)
    {
        var user = new FakeUser(role, unitId);
        await using var db = NewContext(user);
        return await GetDashboardAttention.HandleAsync(db, user, new FixedClock(now ?? Now), query, TestContext.Current.CancellationToken);
    }

    private async Task SeedAsync(Action<AppDbContext> add)
    {
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        add(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) =>
        BrasiliaTime.ToUtc(new DateTime(2026, month, day, hour, minute, 0));

    private static Question NewDraft() => new(QuizType.Ambiental, AgeBand.Age6To7, "Pergunta sintética?", "Explicação sintética.",
        [("A", true), ("B", false), ("C", false), ("D", false)]);

    private static void Add(AppDbContext db, Student student, QuizType type, int correct, int wrong, DateTimeOffset at)
    {
        var session = new QuizSession(student.Id, type, 10, at);
        db.QuizSessions.Add(session);
        for (var i = 0; i < correct + wrong; i++)
        {
            var question = NewDraft();
            question.Approve(); // as questões respondidas da base estão aprovadas: nenhuma conta como rascunho
            db.Questions.Add(question);
            db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, "a", i < correct, 0, at));
        }
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .LogTo(_sql.Add, [RelationalEventId.CommandExecuted])
            .Options, user);

    private static Student NewStudent(AppDbContext db, Unit unit, string name, string className, DateOnly birth)
    {
        var user = db.Users.Add(new AppUser { UserName = $"attention-{name}" }).Entity; // o Add gera o Id
        var student = new Student(user.Id, unit.Id, $"ATT-{name}", name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1", birth,
            "Responsável Fictício", className, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Students.Add(student);
        return student;
    }
}
