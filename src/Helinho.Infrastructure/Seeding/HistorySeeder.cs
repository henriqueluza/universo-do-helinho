using System.Globalization;
using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Application.Quizzes;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Seeding;

/// <summary>
/// Seed sintético de uso dos alunos aprovados do seed de pessoas: sessões e respostas dos últimos
/// <see cref="HistoryDays"/> dias civis de Brasília (até a hora da execução, <see cref="IClock"/>), progresso das missões
/// derivado das respostas, conquistas e inventário inicial. O XP soma só as respostas certas, pela regra do jogo
/// (<see cref="AnswerQuiz.XpPerCorrect"/>); o nível sai de <see cref="LevelPolicy"/>.
/// Determinístico: todo sorteio vem de <see cref="RandomSeed"/>, do aluno, do dia e do turno. Idempotente: por aluno,
/// só grava jogadas fora do intervalo que ele já tem no banco; rodar de novo no mesmo instante não grava nada, e rodar
/// dias depois completa os dias que faltam (e os anteriores, em banco semeado pela versão de 14 dias).
/// </summary>
public sealed class HistorySeeder(AppDbContext db, IClock clock) : ISeeder
{
    public const int HistoryDays = 70;
    public const int RandomSeed = 135;

    /// <summary>Teto do tempo entre duas respostas; o piso é 15 segundos.</summary>
    public const int MaxAnswerSeconds = 150;

    private const double SouthUnitFactor = 0.6; // a Unidade Sul (matrículas pares) joga menos
    private const double WeekendFactor = 0.4;
    private const double AbandonChance = 0.15;
    private const double HardQuestionFactor = 0.45; // a última questão de cada tipo e faixa (ordem de enunciado) confunde

    /// <summary>Turnos no horário de Brasília: primeira hora, horas de janela e chance de jogar num dia útil.</summary>
    private static readonly (int Hour, int Hours, double Chance)[] Shifts = [(7, 4, 0.30), (13, 4, 0.50), (19, 3, 0.25)];

    private static readonly QuizType[] Types = Enum.GetValues<QuizType>();

    /// <summary>Acerto base por tipo, na ordem de <see cref="QuizType"/>: Ambiental, Conhecimento, Esportivo, Cultural e Diversidade.</summary>
    private static readonly double[] Accuracy = [0.92, 0.80, 0.86, 0.72, 0.66];

    /// <summary>Alunos (final da matrícula) que pararam de jogar: não têm jogada nos últimos N dias.</summary>
    private static readonly Dictionary<int, int> StoppedForDays = new() { [7] = 12, [10] = 25 };

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

        var ids = students.Select(s => s.Id).ToList();
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(BrasiliaTime.ToLocal(now));
        var questions = await db.Questions.Include(q => q.Options)
            .Where(q => q.Status == QuestionStatus.Approved).ToListAsync(cancellationToken);
        // Ordem em memória (ordinal): a do banco depende da collation e mudaria quais questões cada sessão recebe.
        var pools = questions.OrderBy(q => q.Statement, StringComparer.Ordinal).ToLookup(q => (q.QuizType, q.AgeBand));
        var quests = await db.DailyQuests.ToListAsync(cancellationToken);
        var badges = await db.Badges.OrderBy(b => b.Code).ToListAsync(cancellationToken);

        // Intervalo que cada aluno já tem no banco (inícios de sessão e respostas, do seed ou do jogo): nada é gravado dentro dele.
        var covered = await db.QuizSessions.Select(s => new { s.StudentId, At = s.StartedAt })
            .Concat(db.QuizAnswers.Join(db.QuizSessions, a => a.SessionId, s => s.Id, (a, s) => new { s.StudentId, At = a.AnsweredAt }))
            .Where(x => ids.Contains(x.StudentId)).GroupBy(x => x.StudentId)
            .Select(g => new { g.Key, First = g.Min(x => x.At), Last = g.Max(x => x.At) })
            .ToDictionaryAsync(g => g.Key, g => (g.First, g.Last), cancellationToken);
        // Sessões que ficaram abertas, com as perguntas já respondidas; por tipo vale a mais recente, como no jogo.
        var openSessions = await db.QuizSessions.Where(s => ids.Contains(s.StudentId) && s.CompletedAt == null).OrderBy(s => s.StartedAt)
            .Select(s => new { Session = s, Answered = db.QuizAnswers.Where(a => a.SessionId == s.Id).Select(a => a.QuestionId).ToList() })
            .ToListAsync(cancellationToken);
        var progress = await db.QuestProgress.Where(p => ids.Contains(p.StudentId) && p.Day >= today.AddDays(-HistoryDays))
            .ToDictionaryAsync(p => (p.StudentId, p.QuestId, p.Day), cancellationToken);
        var withBadges = await db.StudentBadges.Where(b => ids.Contains(b.StudentId)).Select(b => b.StudentId).Distinct().ToListAsync(cancellationToken);
        var withItems = await db.InventoryItems.Where(i => ids.Contains(i.StudentId)).Select(i => i.StudentId).Distinct().ToListAsync(cancellationToken);

        foreach (var student in students)
        {
            var n = int.Parse(student.Enrollment, CultureInfo.InvariantCulture) - 2026000;
            var band = student.AgeBandAt(today);
            var open = openSessions.Where(o => o.Session.StudentId == student.Id).GroupBy(o => o.Session.QuizType)
                .ToDictionary(g => g.Key, g => (g.Last().Session, Answered: g.Last().Answered.ToHashSet()));
            for (var d = HistoryDays; d >= 0; d--)
            {
                var day = today.AddDays(-d);
                var factor = (n % 2 == 0 ? SouthUnitFactor : 1) * (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? WeekendFactor : 1);
                for (var s = 0; s < Shifts.Length; s++)
                {
                    // A semente só depende do aluno, do dia e do turno: a jogada planejada para um dia é a mesma em toda execução.
                    var shift = Shifts[s];
                    var roll = new Random(unchecked((RandomSeed * 73856093) ^ (n * 19349663) ^ (day.DayNumber * 83492791) ^ (s * 2654435)));
                    var at = BrasiliaTime.ToUtc(day.ToDateTime(new TimeOnly(shift.Hour, 0)).AddMinutes(roll.Next(shift.Hours * 60)));
                    var type = Types[roll.Next(Types.Length)];
                    var pool = pools[(type, band)].Take(GetQuizProgress.DefaultTotal).ToList();
                    var plays = roll.NextDouble() < shift.Chance * factor && d > StoppedForDays.GetValueOrDefault(n, -1);
                    var fits = at.AddSeconds(MaxAnswerSeconds * pool.Count) <= now; // jogada inteira no passado
                    var known = covered.TryGetValue(student.Id, out var range);
                    if (plays && fits && pool.Count > 0 && !(known && at >= range.First && at <= range.Last))
                    {
                        // Antes do que o aluno já tem, a jogada não enxerga as sessões abertas de hoje.
                        Play(student, type, pool, at, roll, known && at < range.First ? null : open, quests, progress);
                    }
                }
            }

            var newBadges = withBadges.Contains(student.Id) ? 0 : Math.Min(badges.Count, 1 + n % 5);
            for (var b = 0; b < newBadges; b++)
            {
                db.StudentBadges.Add(new StudentBadge(student.Id, badges[b].Id, now.AddDays(-(b + 1))));
            }

            for (var t = 0; t < InventoryTypes.Length && !withItems.Contains(student.Id); t++)
            {
                var choice = (n + t) % InventoryNames.Length;
                db.InventoryItems.Add(new InventoryItem(
                    student.Id, $"{InventoryTypes[t]}-{choice + 1:00}", InventoryNames[choice], InventoryTypes[t]));
            }
        }

        await db.SaveChangesAsync(cancellationToken); // uma gravação só: ou entra o lote inteiro, ou nada
    }

    /// <summary>
    /// Uma jogada, como o jogo faria: retoma a sessão aberta do tipo (ou abre outra, se não há ou se ela já esgotou),
    /// responde as questões que faltam e conclui na última resposta. Sessão nova pode ser abandonada no meio e fica
    /// aberta até a próxima jogada do tipo. Sem <paramref name="open"/>, não retoma nem abandona.
    /// </summary>
    private void Play(
        Student student, QuizType type, List<Question> pool, DateTimeOffset at, Random roll,
        Dictionary<QuizType, (QuizSession Session, HashSet<Guid> Answered)>? open, List<DailyQuest> quests,
        Dictionary<(Guid, Guid, DateOnly), QuestProgress> progress)
    {
        var day = DateOnly.FromDateTime(BrasiliaTime.ToLocal(at));
        (QuizSession? Session, HashSet<Guid> Answered) current = open is not null && open.Remove(type, out var resumed) ? resumed : (null, []);
        var (session, answered) = current;
        var left = pool.Where(q => !answered.Contains(q.Id)).Take((session?.Total ?? pool.Count) - answered.Count).ToList();
        var isNew = session is null || left.Count == 0;
        if (session is null || left.Count == 0)
        {
            session?.Complete(at); // esgotada: o jogo a encerra e abre outra
            session = new QuizSession(student.Id, type, pool.Count, at);
            db.QuizSessions.Add(session);
            (answered, left) = ([], pool);
        }

        var stop = isNew && open is not null && left.Count > 1 && roll.NextDouble() < AbandonChance ? roll.Next(1, left.Count) : left.Count;
        foreach (var question in left.Take(stop))
        {
            at = at.AddSeconds(roll.Next(15, MaxAnswerSeconds + 1));
            var correct = roll.NextDouble() < Accuracy[(int)type] * (question == pool[^1] ? HardQuestionFactor : 1);
            var xp = correct ? AnswerQuiz.XpPerCorrect : 0;
            var option = question.Options.OrderBy(o => o.Key).First(o => o.IsCorrect == correct);
            db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, option.Key, correct, xp, at));
            answered.Add(question.Id);
            student.AddXp(xp);

            // Mesma regra de AnswerQuiz: cada resposta soma 1 às missões genéricas ou do tipo, até a meta, no dia de Brasília.
            foreach (var quest in quests.Where(q => q.QuizType is null || q.QuizType == type))
            {
                if (!progress.TryGetValue((student.Id, quest.Id, day), out var row))
                {
                    row = new QuestProgress(student.Id, quest.Id, day, 0);
                    db.QuestProgress.Add(row);
                    progress[(student.Id, quest.Id, day)] = row;
                }

                row.Advance(quest.Target);
            }
        }

        if (stop == left.Count)
        {
            session.Complete(at);
        }
        else if (open is not null)
        {
            open[type] = (session, answered);
        }
    }
}
