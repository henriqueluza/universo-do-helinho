using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Seeding;

/// <summary>
/// Seed sintético de conteúdo: questões (<see cref="QuestionBank"/>), 1 evento ativo e 1 encerrado, 3 missões diárias e 10 conquistas.
/// Idempotente (chave natural por item) e com datas relativas ao dia da execução (<see cref="IClock"/>).
/// O bônus diário não tem entidade (contrato congelado) e não é semeado: a Issue #30 define seus valores.
/// </summary>
public sealed class ContentSeeder(AppDbContext db, IClock clock) : ISeeder
{
    public const string ActiveEventTitle = "Semana da Floresta (fictícia)";
    public const string EndedEventTitle = "Copa da Escola (fictícia)";

    public static readonly IReadOnlyList<(string Code, string Title)> Badges =
    [
        ("primeiro-passo", "Primeiro Passo"), ("curioso", "Curioso"), ("guardiao-vila", "Guardião da Vila"),
        ("craque", "Craque da Turma"), ("artista", "Pequeno Artista"), ("amigo-de-todos", "Amigo de Todos"),
        ("sequencia-3", "Três Dias Seguidos"), ("sequencia-7", "Semana Completa"),
        ("explorador", "Explorador de Quizzes"), ("mestre-do-saber", "Mestre do Saber"),
    ];

    public int Order => 20;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedQuestionsAsync(cancellationToken);
        await SeedEventsAsync(cancellationToken);
        await SeedQuestsAsync(cancellationToken);
        await SeedBadgesAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedQuestionsAsync(CancellationToken ct)
    {
        var existing = (await db.Questions.Select(q => q.Statement).ToListAsync(ct)).ToHashSet();
        var index = 0;
        foreach (var item in QuestionBank.All)
        {
            var position = index++ % 4; // a alternativa correta varia entre a, b, c e d
            if (existing.Contains(item.Statement))
            {
                continue;
            }

            var options = item.Wrong.Select(w => (w, false)).ToList();
            options.Insert(position, (item.Correct, true));
            var question = new Question(item.Type, item.Band, item.Statement, item.Explanation, options);
            if (item.Approved)
            {
                question.Approve();
            }

            db.Questions.Add(question);
        }
    }

    private async Task SeedEventsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (!await db.LiveEvents.AnyAsync(e => e.Title == ActiveEventTitle, ct))
        {
            db.LiveEvents.Add(new LiveEvent(ActiveEventTitle, "Quizzes ambientais valem mais esta semana", QuizType.Ambiental, now.AddDays(5)));
        }

        if (!await db.LiveEvents.AnyAsync(e => e.Title == EndedEventTitle, ct))
        {
            db.LiveEvents.Add(new LiveEvent(EndedEventTitle, "Campeonato de quizzes esportivos", QuizType.Esportivo, now.AddDays(-10)));
        }
    }

    private async Task SeedQuestsAsync(CancellationToken ct)
    {
        (string Title, int Target, int Xp, QuizType? Type)[] quests =
        [
            ("Responda 3 perguntas ambientais (fictícia)", 3, 30, QuizType.Ambiental),
            ("Responda 5 perguntas de qualquer tipo (fictícia)", 5, 40, null),
            ("Acerte 3 perguntas hoje (fictícia)", 3, 30, null),
        ];
        var titles = (await db.DailyQuests.Select(q => q.Title).ToListAsync(ct)).ToHashSet();
        foreach (var (title, target, xp, type) in quests.Where(q => !titles.Contains(q.Title)))
        {
            db.DailyQuests.Add(new DailyQuest(title, target, xp, type));
        }
    }

    private async Task SeedBadgesAsync(CancellationToken ct)
    {
        var codes = (await db.Badges.Select(b => b.Code).ToListAsync(ct)).ToHashSet();
        foreach (var (code, title) in Badges.Where(b => !codes.Contains(b.Code)))
        {
            db.Badges.Add(new Badge(code, title, $"badge-{code}"));
        }
    }
}
