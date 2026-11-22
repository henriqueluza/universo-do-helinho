using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Application.Quizzes;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #135: histórico sintético para os gráficos do Painel. Relógio fixo, banco próprio por fábrica, só dados do seed.</summary>
[Collection(PostgresCollection.Name)]
public class HistorySeedTests(PostgresFixture postgres)
{
    // Quinta-feira, 15:30 em Brasília: hoje já tem manhã e ainda não tem noite.
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 18, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private sealed record Row(
        string Enrollment, Guid StudentId, int StudentXp, string Unit, Guid SessionId, QuizType Type, DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt, string Statement, bool IsCorrect, DateTimeOffset AnsweredAt)
    {
        public DateTime Local => BrasiliaTime.ToLocal(AnsweredAt);
        public DateOnly Day => DateOnly.FromDateTime(Local);
        public string Key => $"{Enrollment} {Type} {StartedAt:o} {Statement} {IsCorrect} {AnsweredAt:o}";
    }

    /// <summary>Todas as respostas do banco, com sessão, aluno e unidade (ignora-filtro: o teste não tem unidade de contexto).</summary>
    private static async Task<List<Row>> AnswersAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await (from a in db.QuizAnswers
                      join s in db.QuizSessions on a.SessionId equals s.Id
                      join st in db.Students.IgnoreQueryFilters() on s.StudentId equals st.Id
                      join u in db.Units on st.UnitId equals u.Id
                      join q in db.Questions on a.QuestionId equals q.Id
                      orderby st.Enrollment, a.AnsweredAt
                      select new Row(
                          st.Enrollment, st.Id, st.Xp, u.Name, s.Id, s.QuizType, s.StartedAt, s.CompletedAt, q.Statement, a.IsCorrect, a.AnsweredAt))
            .ToListAsync(Ct);
    }

    /// <summary>Retrato do histórico sem os ids (que mudam de banco para banco): respostas, XP, missões, selos e itens.</summary>
    private static async Task<List<string>> SnapshotAsync(HelinhoWebFactory factory)
    {
        var rows = await AnswersAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var progress = await (from p in db.QuestProgress
                              join st in db.Students.IgnoreQueryFilters() on p.StudentId equals st.Id
                              join q in db.DailyQuests on p.QuestId equals q.Id
                              orderby st.Enrollment, p.Day, q.Title
                              select $"{st.Enrollment} {p.Day} {q.Title} {p.Current}").ToListAsync(Ct);
        var totals = $"{await db.QuizSessions.CountAsync(Ct)} sessões, {await db.StudentBadges.CountAsync(Ct)} selos, {await db.InventoryItems.CountAsync(Ct)} itens";
        var ends = rows.Select(r => $"{r.Enrollment} {r.StudentXp} XP, {r.StartedAt:o} concluída em {r.CompletedAt:o}").Distinct();
        return [.. rows.Select(r => r.Key), .. ends, .. progress, totals];
    }

    [Fact]
    public async Task Segunda_execucao_nao_muda_nada_e_outro_banco_com_o_mesmo_relogio_recebe_o_mesmo_historico()
    {
        await using var factory = new HelinhoWebFactory(postgres, clock: new FakeClock());
        await factory.EnsureDatabaseAsync();
        var first = await SnapshotAsync(factory);

        await factory.EnsureDatabaseAsync();

        Assert.Equal(first, await SnapshotAsync(factory)); // nem sessão, nem resposta, nem XP, nem missão a mais
        await using var other = new HelinhoWebFactory(postgres, clock: new FakeClock());
        await other.EnsureDatabaseAsync();
        Assert.Equal(first, await SnapshotAsync(other)); // determinístico: nenhum sorteio fora da semente
    }

    [Fact]
    public async Task Historico_cobre_semanas_turnos_tipos_unidades_sessoes_e_alunos_parados()
    {
        await using var factory = new HelinhoWebFactory(postgres, clock: new FakeClock());
        await factory.EnsureDatabaseAsync();
        var rows = await AnswersAsync(factory);

        // Mais de 60 dias, nunca no futuro: o período de 30 dias e o anterior têm respostas; fim de semana joga menos.
        Assert.All(rows, r => Assert.InRange(r.AnsweredAt, Now.AddDays(-HistorySeeder.HistoryDays - 1), Now));
        Assert.True(rows.Min(r => r.Day) <= Today.AddDays(-60));
        Assert.True(rows.Select(r => r.Day.DayNumber / 7).Distinct().Count() >= 9);
        Assert.Contains(rows, r => r.Day > Today.AddDays(-30));
        Assert.Contains(rows, r => r.Day <= Today.AddDays(-30) && r.Day > Today.AddDays(-60));
        Assert.Contains(rows, r => r.Day == Today);
        var weekend = rows.Count(r => r.Day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.True((rows.Count - weekend) / 5.0 > weekend / 2.0 * 1.5, $"{rows.Count - weekend} em dias úteis, {weekend} no fim de semana");

        // Três turnos de Brasília, cinco tipos com acerto diferente, duas unidades com volumes diferentes.
        var shifts = rows.GroupBy(r => r.Local.Hour switch { >= 6 and < 12 => "manhã", >= 12 and < 18 => "tarde", _ => "noite" });
        Assert.Equal(3, shifts.Count(g => g.Count() >= 100));
        var accuracy = rows.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Average(r => r.IsCorrect ? 100.0 : 0));
        Assert.Equal(Enum.GetValues<QuizType>().Length, accuracy.Count);
        Assert.True(accuracy[QuizType.Ambiental] - accuracy[QuizType.Diversidade] >= 10, string.Join(", ", accuracy));
        var units = rows.GroupBy(r => r.Unit).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(2, units.Count);
        Assert.True(units[PeopleSeeder.NorthUnitName] > units[PeopleSeeder.SouthUnitName] * 1.2, string.Join(", ", units));

        // "Questões que mais confundem": no período de 30 dias, alguma com 5 ou mais respostas e menos da metade certa.
        Assert.Contains(
            rows.Where(r => r.Day > Today.AddDays(-30)).GroupBy(r => r.Statement),
            g => g.Count() >= 5 && g.Count(r => r.IsCorrect) * 2 < g.Count());

        // Sessões: concluídas na última resposta, com durações variadas (as retomadas passam de 60 minutos);
        // abertas com respostas faltando, no máximo uma por aluno e tipo, como o jogo deixa.
        var sessions = rows.GroupBy(r => (r.SessionId, r.Enrollment, r.Type, r.StartedAt, r.CompletedAt)).ToList();
        var done = sessions.Where(s => s.Key.CompletedAt is not null).ToList();
        var open = sessions.Where(s => s.Key.CompletedAt is null).ToList();
        Assert.All(sessions, s => Assert.True(s.Min(r => r.AnsweredAt) > s.Key.StartedAt));
        Assert.All(done, s => Assert.Equal((3, s.Key.CompletedAt), (s.Count(), (DateTimeOffset?)s.Max(r => r.AnsweredAt))));
        var minutes = done.Select(s => (int)(s.Key.CompletedAt!.Value - s.Key.StartedAt).TotalMinutes).Distinct().ToList();
        Assert.True(minutes.Count(m => m <= 60) >= 5, string.Join(", ", minutes));
        Assert.Contains(minutes, m => m > 60);
        Assert.InRange(open.Count, 3, 20);
        Assert.All(open, s => Assert.InRange(s.Count(), 1, 2));
        Assert.Equal(open.Count, open.Select(s => (s.Key.Enrollment, s.Key.Type)).Distinct().Count());

        // Os 8 alunos aprovados jogaram; 2 pararam há mais de 7 dias e os outros jogaram nos últimos 7.
        var last = rows.GroupBy(r => r.Enrollment).ToDictionary(g => g.Key, g => g.Max(r => r.Day));
        Assert.Equal(8, last.Count);
        Assert.Equal(["2026007", "2026010"], last.Where(l => l.Value < Today.AddDays(-7)).Select(l => l.Key).Order());

        // XP e missões seguem a regra do jogo: 10 por resposta certa; cada resposta soma 1 às missões do dia, até a meta.
        Assert.All(rows.GroupBy(r => r.StudentId), g => Assert.Equal(g.First().StudentXp, g.Count(r => r.IsCorrect) * AnswerQuiz.XpPerCorrect));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quests = await db.DailyQuests.ToDictionaryAsync(q => q.Id, Ct);
        var progress = await db.QuestProgress.ToListAsync(Ct);
        Assert.Equal(quests.Values.Sum(q => rows.Where(r => q.QuizType is null || q.QuizType == r.Type).Select(r => (r.StudentId, r.Day)).Distinct().Count()), progress.Count);
        Assert.All(progress, p =>
        {
            var quest = quests[p.QuestId];
            var answered = rows.Count(r => r.StudentId == p.StudentId && r.Day == p.Day && (quest.QuizType is null || quest.QuizType == r.Type));
            Assert.Equal(Math.Min(quest.Target, answered), p.Current);
        });
        Assert.Contains(progress, p => p.Current >= quests[p.QuestId].Target && p.Day > Today.AddDays(-30));
        Assert.Contains(progress, p => p.Current >= quests[p.QuestId].Target && p.Day <= Today.AddDays(-30));
    }

    [Fact]
    public async Task Aluno_que_ja_tem_respostas_so_recebe_jogadas_fora_do_intervalo_e_o_seed_completa_os_dias_seguintes()
    {
        var clock = new FakeClock();
        await using var factory = new HelinhoWebFactory(postgres, clock: clock);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Pessoas e conteúdo primeiro; depois duas sessões "jogadas" pelo aluno 01, há 30 e há 10 dias (respostas erradas, 0 XP).
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(Ct);
            foreach (var seeder in scope.ServiceProvider.GetServices<ISeeder>().Where(s => s is not HistorySeeder).OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(Ct);
            }

            var student = await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Enrollment == "2026001", Ct);
            var question = await db.Questions.Include(q => q.Options).FirstAsync(q => q.Status == QuestionStatus.Approved, Ct);
            foreach (var start in new[] { Now.AddDays(-30), Now.AddDays(-10) })
            {
                var session = new QuizSession(student.Id, question.QuizType, 3, start);
                db.QuizSessions.Add(session);
                db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, question.Options.First(o => !o.IsCorrect).Key, false, 0, start.AddMinutes(1)));
            }

            await db.SaveChangesAsync(Ct);
        }

        await factory.EnsureDatabaseAsync();
        var before = await AnswersAsync(factory);
        var mine = before.Where(r => r.Enrollment == "2026001").ToList();
        var (from, to) = (Now.AddDays(-30), Now.AddDays(-10).AddMinutes(1));
        Assert.Contains(mine, r => r.AnsweredAt < from); // completou para trás
        Assert.Contains(mine, r => r.AnsweredAt > to); // e para a frente
        Assert.Equal(2, mine.Count(r => r.AnsweredAt >= from && r.AnsweredAt <= to)); // dentro do intervalo, só as duas jogadas

        clock.UtcNow = Now.AddDays(7); // um deploy uma semana depois
        await factory.EnsureDatabaseAsync();

        var after = await AnswersAsync(factory);
        var known = before.Select(r => r.Key).ToHashSet();
        var added = after.Where(r => !known.Contains(r.Key)).ToList();
        Assert.NotEmpty(added);
        Assert.Empty(known.Except(after.Select(r => r.Key))); // nada do que existia mudou
        Assert.All(added, r => Assert.True(r.AnsweredAt > before.Where(b => b.Enrollment == r.Enrollment).Max(b => b.AnsweredAt), r.Key));
        Assert.All(after.GroupBy(r => r.StudentId), g => Assert.Equal(g.First().StudentXp, g.Count(r => r.IsCorrect) * AnswerQuiz.XpPerCorrect));
        // Sessão que estava aberta foi retomada, e não sobra mais de uma aberta por aluno e tipo.
        Assert.Contains(after, r => r.CompletedAt is not null && before.Any(b => b.SessionId == r.SessionId && b.CompletedAt is null));
        var open = after.Where(r => r.CompletedAt is null).Select(r => (r.SessionId, r.Enrollment, r.Type)).Distinct().ToList();
        Assert.Equal(open.Count, open.Select(o => (o.Enrollment, o.Type)).Distinct().Count());
    }
}
