using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Seeding;

/// <summary>
/// Seed sintético de uso: para os alunos aprovados do seed de pessoas, histórico dos últimos 14 dias
/// (sessões e respostas), XP somado das respostas certas (o nível sai de <see cref="LevelPolicy"/>),
/// progresso das missões de hoje, conquistas desbloqueadas e inventário inicial. Determinístico (sem aleatoriedade)
/// e idempotente por aluno; datas relativas ao dia da execução (<see cref="IClock"/>).
/// </summary>
public sealed class HistorySeeder(AppDbContext db, IClock clock) : ISeeder
{
    public const int HistoryDays = 14;
    public const int XpPerCorrectAnswer = 10;
    public const int QuestionsPerSession = 3;

    private static readonly string[] InventoryTypes = ["cabelo", "pele", "roupa", "rosto", "acessorio"];
    private static readonly string[] InventoryNames = ["Clássico", "Cacheado", "Explorador", "Sorridente", "Aventureiro", "Criativo"];

    public int Order => 30;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var enrollments = Enumerable.Range(1, 10).Select(n => $"{2026000 + n}").ToList();
        // ignora-filtro: o seed roda sem usuário/unidade de contexto e atende alunos de todas as unidades.
        var students = await db.Students.IgnoreQueryFilters()
            .Where(s => enrollments.Contains(s.Enrollment) && s.GuardianApproved)
            .OrderBy(s => s.Enrollment).ToListAsync(cancellationToken);
        if (students.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var questions = await db.Questions.Include(q => q.Options)
            .Where(q => q.Status == QuestionStatus.Approved).OrderBy(q => q.Statement).ToListAsync(cancellationToken);
        var quests = await db.DailyQuests.OrderBy(q => q.Title).ToListAsync(cancellationToken);
        var badges = await db.Badges.OrderBy(b => b.Code).ToListAsync(cancellationToken);
        var types = Enum.GetValues<QuizType>();

        for (var i = 0; i < students.Count; i++)
        {
            var student = students[i];
            var n = i + 1;
            var band = student.AgeBandAt(today);

            if (!await db.QuizSessions.AnyAsync(s => s.StudentId == student.Id, cancellationToken))
            {
                for (var d = 1; d <= HistoryDays; d++)
                {
                    if ((n + d) % 4 == 0)
                    {
                        continue; // dias sem jogar
                    }

                    var type = types[(n + d) % types.Length];
                    var pool = questions.Where(q => q.QuizType == type && q.AgeBand == band).Take(QuestionsPerSession).ToList();
                    if (pool.Count == 0)
                    {
                        continue;
                    }

                    var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-d).AddHours(14), TimeSpan.Zero);
                    var session = new QuizSession(student.Id, type, pool.Count, start);
                    db.QuizSessions.Add(session);
                    for (var k = 0; k < pool.Count; k++)
                    {
                        var correct = (n + d + k) % 3 != 0;
                        var option = pool[k].Options.First(o => o.IsCorrect == correct);
                        var xp = correct ? XpPerCorrectAnswer : 0;
                        db.QuizAnswers.Add(new QuizAnswer(session.Id, pool[k].Id, option.Key, correct, xp, start.AddSeconds(20 * (k + 1))));
                        student.AddXp(xp);
                    }

                    session.Complete(start.AddMinutes(2));
                }
            }

            if (!await db.QuestProgress.AnyAsync(p => p.StudentId == student.Id && p.Day == today, cancellationToken))
            {
                for (var q = 0; q < quests.Count; q++)
                {
                    db.QuestProgress.Add(new QuestProgress(student.Id, quests[q].Id, today, (n + q) % (quests[q].Target + 1)));
                }
            }

            if (!await db.StudentBadges.AnyAsync(b => b.StudentId == student.Id, cancellationToken))
            {
                for (var b = 0; b < Math.Min(badges.Count, 1 + n % 5); b++)
                {
                    db.StudentBadges.Add(new StudentBadge(student.Id, badges[b].Id, now.AddDays(-(b + 1))));
                }
            }

            if (!await db.InventoryItems.AnyAsync(x => x.StudentId == student.Id, cancellationToken))
            {
                for (var t = 0; t < InventoryTypes.Length; t++)
                {
                    var choice = (n + t) % InventoryNames.Length;
                    db.InventoryItems.Add(new InventoryItem(
                        student.Id, $"{InventoryTypes[t]}-{choice + 1:00}", InventoryNames[choice], InventoryTypes[t]));
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
