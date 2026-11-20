using Helinho.Application.Abstractions;
using Helinho.Application.Quizzes;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class ContentSeedTests(PostgresFixture postgres)
{
    // Data fixa distinta de hoje: o teste falha se o seed usar DateTime.UtcNow em vez do IClock.
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    [Fact]
    public async Task Seed_de_conteudo_e_historico_tem_contagens_esperadas_e_e_idempotente()
    {
        await using var factory = new HelinhoWebFactory(postgres, clock: new FixedClock(Now));
        var ct = TestContext.Current.CancellationToken;
        for (var run = 0; run < 2; run++) // segunda execução: não duplica nem falha
        {
            await using var seedScope = factory.Services.CreateAsyncScope();
            await seedScope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(ct);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = Now;

        // Questões do seed (identificadas pelo banco do próprio seed): ≥10 aprovadas por tipo, 4 faixas, e rascunhos.
        var seeded = QuestionBank.All.Select(i => i.Statement).ToList();
        var questions = await db.Questions.Where(q => seeded.Contains(q.Statement)).Include(q => q.Options).ToListAsync(ct);
        Assert.Equal(QuestionBank.All.Count, questions.Count);
        foreach (var type in Enum.GetValues<QuizType>())
        {
            var approved = questions.Where(q => q.QuizType == type && q.Status == QuestionStatus.Approved).ToList();
            Assert.True(approved.Count >= 10, $"{type}: {approved.Count} aprovadas");
            Assert.Equal(Enum.GetValues<AgeBand>().Length, approved.Select(q => q.AgeBand).Distinct().Count());
        }

        Assert.Contains(questions, q => q.Status == QuestionStatus.Draft);
        Assert.All(questions, q => Assert.Single(q.Options, o => o.IsCorrect));

        // Eventos, missões e conquistas.
        var active = await db.LiveEvents.SingleAsync(e => e.Title == ContentSeeder.ActiveEventTitle, ct);
        var ended = await db.LiveEvents.SingleAsync(e => e.Title == ContentSeeder.EndedEventTitle, ct);
        Assert.Equal(now.AddDays(5), active.EndsAt);
        Assert.Equal(now.AddDays(-10), ended.EndsAt);
        var quests = await db.DailyQuests.Where(q => q.Title.EndsWith("(fictícia)")).ToListAsync(ct);
        Assert.Equal(3, quests.Count);
        Assert.Equal(1, quests.Count(q => q.QuizType != null));
        Assert.Equal(10, await db.Badges.CountAsync(b => ContentSeeder.Badges.Select(x => x.Code).Contains(b.Code), ct));

        // Histórico: só alunos aprovados do seed, dentro da janela, XP coerente com as respostas e com o nível.
        var enrollments = Enumerable.Range(1, 10).Select(n => $"{2026000 + n}").ToList();
        var students = await db.Students.IgnoreQueryFilters()
            .Where(s => enrollments.Contains(s.Enrollment) && s.GuardianApproved).ToListAsync(ct);
        Assert.Equal(8, students.Count);
        var ids = students.Select(s => s.Id).ToList();
        var sessions = await db.QuizSessions.Where(s => ids.Contains(s.StudentId)).ToListAsync(ct);
        Assert.All(sessions, s => Assert.InRange(s.StartedAt, now.AddDays(-HistorySeeder.HistoryDays - 1), now));
        Assert.Contains(sessions, s => s.StartedAt >= now.AddDays(-HistorySeeder.HistoryDays));
        Assert.All(ids, id => Assert.Contains(sessions, s => s.StudentId == id));
        foreach (var student in students)
        {
            var sessionIds = sessions.Where(s => s.StudentId == student.Id).Select(s => s.Id).ToList();
            var xp = await db.QuizAnswers.Where(a => sessionIds.Contains(a.SessionId)).SumAsync(a => a.XpGained, ct);
            var correct = await db.QuizAnswers.CountAsync(a => sessionIds.Contains(a.SessionId) && a.IsCorrect, ct);
            Assert.Equal(xp, student.Xp);
            Assert.Equal(correct * AnswerQuiz.XpPerCorrect, student.Xp);
            Assert.True(xp > 0);
            // O progresso das missões deriva das respostas de cada dia; HistorySeedTests confere a conta.
            Assert.True(await db.QuestProgress.AnyAsync(p => p.StudentId == student.Id, ct));
            Assert.Equal(5, await db.InventoryItems.CountAsync(i => i.StudentId == student.Id, ct));
            Assert.True(await db.StudentBadges.CountAsync(b => b.StudentId == student.Id, ct) >= 1);
        }

        // Alunos com aprovação pendente não têm histórico.
        var pendingIds = await db.Students.IgnoreQueryFilters()
            .Where(s => enrollments.Contains(s.Enrollment) && !s.GuardianApproved).Select(s => s.Id).ToListAsync(ct);
        Assert.False(await db.QuizSessions.AnyAsync(s => pendingIds.Contains(s.StudentId), ct));
    }
}
