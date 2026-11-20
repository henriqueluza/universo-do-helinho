using System.Text.Json;
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
/// Painel Recreio (#133): "questões que mais confundem" e "unidades que mais jogam". "Agora" é 10/nov 12:00 em
/// Brasília: período de 7 dias = 4 a 10/nov. Unidade A: Ana (7 anos) e Bia (12); B: Caio (7). Outras quatro unidades
/// (duas com o mesmo nome) só respondem à questão "fácil". Acertos/respostas em 7 dias: Q1 Ana 1/5 + Caio 4/5 ·
/// Q2 Ana 2/5 + Bia 0/1 · Q3 Caio 0/4 · empate (duas questões) Bia 3/6 em cada · Q6 Caio 5/5 · Q7 Caio 3/5 ·
/// fácil, todas certas: C 19 (1 aluno), C 10 + 9 (2 alunos), D 2, E 1.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardRankingsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private readonly string _database = $"rankings_{Guid.NewGuid():N}";
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");
    private readonly Question _q1 = NewQuestion(QuizType.Ambiental, AgeBand.Age8To10, "Qual é o maior bioma?");
    private readonly Question _q2 = NewQuestion(QuizType.Cultural, AgeBand.Age11To12, "Qual é <b>a capital</b> & \"sede\"?");
    private readonly Question _q3 = NewQuestion(QuizType.Esportivo, AgeBand.Age6To7, "Quantos jogadores?");
    private readonly Question _q6 = NewQuestion(QuizType.Conhecimento, AgeBand.Age6To7, "Qual planeta?");
    private readonly Question _q7 = NewQuestion(QuizType.Diversidade, AgeBand.Age8To10, "O que é a sigla?");
    private readonly Question _easy = NewQuestion(QuizType.Ambiental, AgeBand.Age6To7, "Questão fácil?");

    // Mesma taxa e mesmas respostas (ou mesmo nome de unidade e mesmas respostas): decide o id, na ordem do uuid do banco.
    private readonly Question[] _tied = [.. Enumerable.Range(0, 2)
        .Select(_ => NewQuestion(QuizType.Diversidade, AgeBand.Age11To12, "Empate?")).OrderBy(q => q.Id.ToString(), StringComparer.Ordinal)];
    private readonly Unit[] _unitsC = [.. Enumerable.Range(0, 2).Select(_ => new Unit("Unidade C")).OrderBy(u => u.Id.ToString(), StringComparer.Ordinal)];

    // 5/9 = 55,6% e 9/16 = 56,25%: os dois mostram 56%. Respondidas em 1/set por uma aluna de 13 anos (só nos 90 dias).
    private readonly Question[] _rounded = [NewQuestion(QuizType.Cultural, AgeBand.Age13To14, "Taxa exata menor?"), NewQuestion(QuizType.Cultural, AgeBand.Age13To14, "Mais respostas?")];
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
        (Unit Unit, int Answers)[] easy = [(_unitsC[0], 19), (_unitsC[1], 10), (_unitsC[1], 9), (new("Unidade D"), 2), (new("Unidade E"), 1)];
        db.Units.AddRange([_unitA, _unitB, .. easy.Select(e => e.Unit).Distinct()]);
        db.Questions.AddRange([_q1, _q2, _q3, _q6, _q7, _easy, .. _tied, .. _rounded]);
        var ana = NewStudent(db, _unitA, "Ana", new DateOnly(2019, 3, 1));
        var bia = NewStudent(db, _unitA, "Bia", new DateOnly(2014, 3, 1));
        var caio = NewStudent(db, _unitB, "Caio", new DateOnly(2019, 3, 1));

        Add(db, ana, _q1, At(11, 10, 9), 1, 5);
        Add(db, ana, _q1, At(11, 11, 0), 0, 1); // 00:00 de amanhã: fora (limite superior exclusivo)
        Add(db, caio, _q1, At(11, 8, 9), 4, 5);
        Add(db, ana, _q2, At(11, 9, 23, 30), 2, 5); // 02:30 UTC do dia 10: ainda é dia 9 em Brasília
        Add(db, bia, _q2, At(11, 10, 10), 0, 1);
        Add(db, caio, _q3, At(11, 4, 0), 0, 4); // 00:00 do primeiro dia: dentro (limite inferior inclusivo)
        Add(db, caio, _q3, At(11, 3, 23, 59), 0, 1); // um minuto antes: só entra nos 30 dias
        Add(db, bia, _tied[0], At(11, 7, 9), 3, 6);
        Add(db, bia, _tied[1], At(11, 7, 10), 3, 6);
        Add(db, caio, _q6, At(11, 6, 9), 5, 5);
        Add(db, caio, _q7, At(11, 5, 9), 3, 5);
        var duda = NewStudent(db, _unitA, "Duda", new DateOnly(2013, 3, 1));
        Add(db, duda, _rounded[0], At(9, 1, 9), 5, 9);
        Add(db, duda, _rounded[1], At(9, 1, 9), 9, 16);
        for (var i = 0; i < easy.Length; i++)
        {
            Add(db, NewStudent(db, easy[i].Unit, $"Extra{i}", new DateOnly(2019, 3, 1)), _easy, At(11, 9, 9), easy[i].Answers, easy[i].Answers);
        }

        await db.SaveChangesAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Master_ve_as_questoes_do_menor_acerto_para_o_maior_com_desempate_e_limite()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));

        // Q3 tem 0% mas só 4 respostas (abaixo do mínimo); Q6 e a fácil (100%) ficam além das 5 linhas.
        Assert.Equal([Row(_q2, 6, 33), Row(_q1, 10, 50), Row(_tied[0], 6, 50), Row(_tied[1], 6, 50), Row(_q7, 5, 60)], r.ConfusingQuestions);
        Assert.Equal("Qual é <b>a capital</b> & \"sede\"?", r.ConfusingQuestions[0].Statement); // texto cru: a tela faz o encode
    }

    [Fact]
    public async Task Master_ve_as_unidades_por_respostas_com_fracao_da_primeira_desempate_e_limite()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));

        UnitActivity[] expected =
        [
            new("Unidade A", 23, 2, 1), new("Unidade B", 19, 1, 0.83), // empate em 19 com as duas "Unidade C": o nome decide
            new("Unidade C", 19, 1, 0.83), new("Unidade C", 19, 2, 0.83), // mesmo nome e mesmas respostas: o id decide
            new("Unidade D", 2, 1, 0.09), // "Unidade E" (1 resposta) passa do limite
        ];
        Assert.Equal(expected, r.TopUnits);
        var json = JsonSerializer.Serialize(r);
        Assert.All(["Sintétic", "RANK-", "000.000.000-00", "Rua", "Responsável"], pii => Assert.DoesNotContain(pii, json, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Administrador_so_conta_a_propria_unidade_e_nunca_recebe_o_ranking_de_unidades()
    {
        _sql.Clear();
        var own = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, null));
        var sqlOfOwn = _sql.ToList();
        var forced = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, _unitB.Id));
        var semUnidade = await RunAsync(RoleNames.Admin, null, new DashboardQuery(null, 7, _unitB.Id));
        var outroPapel = await RunAsync("Professor", _unitB.Id, new DashboardQuery(null, 7, null));

        foreach (var r in new[] { own, forced })
        {
            // Sem o Caio (unidade B): Q1 cai de 10 respostas/50% para 5/20%; Q6 e Q7 somem.
            Assert.Equal([Row(_q1, 5, 20), Row(_q2, 6, 33), Row(_tied[0], 6, 50), Row(_tied[1], 6, 50)], r.ConfusingQuestions);
        }

        Assert.Empty(semUnidade.ConfusingQuestions);
        Assert.All([own, forced, semUnidade, outroPapel], r => Assert.Null(r.TopUnits));
        var only = Assert.Single(sqlOfOwn); // negado antes de consultar: nenhuma consulta toca a tabela de unidades
        Assert.DoesNotContain("\"Units\"", only, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_restringe_as_questoes_pelo_filtro_de_unidade_e_o_ranking_de_unidades_continua_comparando_todas()
    {
        var all = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null));
        var b = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, _unitB.Id));

        Assert.Equal([Row(_q7, 5, 60), Row(_q1, 5, 80), Row(_q6, 5, 100)], b.ConfusingQuestions);
        Assert.Equal(all.TopUnits, b.TopUnits);
    }

    [Fact]
    public async Task Periodo_e_faixa_etaria_valem_para_os_dois_rankings()
    {
        var month = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 30, null));
        var older = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age11To12, 7, null));

        // Em 30 dias Q3 chega a 5 respostas (0%) e entra; Q7 passa do limite. A unidade B ganha a resposta do dia 3.
        Assert.Equal([Row(_q3, 5, 0), Row(_q2, 6, 33), Row(_q1, 10, 50), Row(_tied[0], 6, 50), Row(_tied[1], 6, 50)], month.ConfusingQuestions);
        Assert.Equal([("Unidade A", 23, 1.0), ("Unidade B", 20, 0.87)], month.TopUnits?.Take(2).Select(u => (u.UnitName, u.Answers, u.Share)));
        Assert.Equal([Row(_tied[0], 6, 50), Row(_tied[1], 6, 50)], older.ConfusingQuestions); // só a Bia; Q2 fica com 1 resposta
        Assert.Equal([new UnitActivity("Unidade A", 13, 1, 1)], older.TopUnits);

        // 23:30 do dia 9 em Brasília (já é dia 10 em UTC): o período é 3 a 9/nov, então o dia 3 entra e o dia 10 sai.
        var late = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null), At(11, 9, 23, 30));
        Assert.Equal([Row(_q3, 5, 0), Row(_q2, 5, 40), Row(_tied[0], 6, 50), Row(_tied[1], 6, 50), Row(_q7, 5, 60)], late.ConfusingQuestions);
        Assert.Equal(
            [("Unidade B", 20, 1.0), ("Unidade C", 19, 0.95), ("Unidade C", 19, 0.95), ("Unidade A", 17, 0.85), ("Unidade D", 2, 0.1)],
            late.TopUnits?.Select(u => (u.UnitName, u.Answers, u.Share)));
    }

    [Fact]
    public async Task Ordem_das_questoes_usa_a_taxa_exata_e_nao_o_percentual_arredondado()
    {
        var r = await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age13To14, 90, null));

        // Pelo percentual mostrado seria empate, e o desempate (mais respostas) inverteria a ordem.
        Assert.Equal([Row(_rounded[0], 9, 56), Row(_rounded[1], 16, 56)], r.ConfusingQuestions);
    }

    [Fact]
    public async Task Periodo_sem_dados_devolve_listas_vazias_e_o_administrador_continua_sem_o_ranking_de_unidades()
    {
        var later = new DateTimeOffset(2027, 3, 1, 15, 0, 0, TimeSpan.Zero);
        var master = await RunAsync(RoleNames.Master, null, new DashboardQuery(null, 7, null), later);
        var admin = await RunAsync(RoleNames.Admin, _unitA.Id, new DashboardQuery(null, 7, null), later);

        Assert.Empty(master.ConfusingQuestions);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<UnitActivity>>(master.TopUnits)); // vazio, não nulo
        Assert.Empty(admin.ConfusingQuestions);
        Assert.Null(admin.TopUnits);
    }

    [Fact]
    public async Task Agregacao_ordenacao_e_limite_acontecem_no_banco_em_uma_consulta_por_bloco()
    {
        _sql.Clear();
        await RunAsync(RoleNames.Master, null, new DashboardQuery(AgeBand.Age6To7, 90, _unitA.Id));

        Assert.Equal(2, _sql.Count);
        Assert.All(_sql, sql => Assert.All(["GROUP BY", "ORDER BY", "LIMIT"], part => Assert.Contains(part, sql, StringComparison.Ordinal)));
        Assert.Contains("HAVING", _sql[0], StringComparison.Ordinal);
        Assert.Contains("count(DISTINCT", _sql[1], StringComparison.OrdinalIgnoreCase);
        string[] personal = ["FullName", "Enrollment", "Cpf", "Rg", "Address", "GuardianName"]; // nenhuma coluna cadastral é lida
        Assert.All(_sql, sql => Assert.All(personal, column => Assert.DoesNotContain($"\"{column}\"", sql, StringComparison.Ordinal)));
    }

    private async Task<DashboardRankings> RunAsync(string role, Guid? unitId, DashboardQuery query, DateTimeOffset? now = null)
    {
        var user = new FakeUser(role, unitId);
        await using var db = NewContext(user);
        return await GetDashboardRankings.HandleAsync(db, user, new FixedClock(now ?? Now), query, TestContext.Current.CancellationToken);
    }

    private static ConfusingQuestion Row(Question q, int answers, int percent) => new(q.Id, q.Statement, q.QuizType, q.AgeBand, answers, percent);

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) =>
        BrasiliaTime.ToUtc(new DateTime(2026, month, day, hour, minute, 0));

    private static Question NewQuestion(QuizType type, AgeBand band, string statement) =>
        new(type, band, statement, "Explicação sintética.", [("A", true), ("B", false), ("C", false), ("D", false)]);

    /// <summary>Uma sessão por resposta: a mesma questão pode ser respondida várias vezes pelo mesmo aluno.</summary>
    private static void Add(AppDbContext db, Student student, Question question, DateTimeOffset at, int correct, int total)
    {
        for (var i = 0; i < total; i++)
        {
            var session = new QuizSession(student.Id, question.QuizType, 10, at);
            db.QuizSessions.Add(session);
            db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, "a", i < correct, 0, at));
        }
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .LogTo(_sql.Add, [RelationalEventId.CommandExecuted])
            .Options, user);

    private static Student NewStudent(AppDbContext db, Unit unit, string name, DateOnly birth)
    {
        var user = new AppUser { UserName = $"rank{Guid.NewGuid():N}" };
        db.Users.Add(user);
        var student = new Student(user.Id, unit.Id, $"RANK-{name}", $"Sintético {name}", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1", birth,
            "Responsável Fictício", "5A", DateOnly.FromDateTime(Now.UtcDateTime));
        db.Students.Add(student);
        return student;
    }
}
