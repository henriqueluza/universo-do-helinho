using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public enum FeedEventKind { QuizCompleted, BadgeUnlocked }

/// <summary>
/// Um item de "Acabou de acontecer". Privacidade (T4 do ADR-0001; os alunos são crianças): do aluno saem só o primeiro
/// nome e o nome da unidade. Nunca nome completo, matrícula, CPF, RG, endereço, responsável ou identificador.
/// <c>QuizType</c> é nulo em selo (selo não tem tipo de quiz); <c>At</c> é o instante em UTC.
/// </summary>
public sealed record FeedEvent(FeedEventKind Kind, string FirstName, string UnitName, string Text, QuizType? QuizType, DateTimeOffset At);

public sealed record DashboardFeed(IReadOnlyList<FeedEvent> Events)
{
    /// <summary>A tela mostra "Nenhuma jogada nas últimas 24 horas".</summary>
    public bool IsEmpty => Events.Count == 0;
}

/// <summary>
/// Bloco "Acabou de acontecer", só com o que tem data e hora gravadas:
/// quiz concluído ("acertou X de Y no quiz") = <c>QuizSession.CompletedAt</c>, com X e Y contados nas respostas da sessão
/// (sessão encerrada sem resposta não entra); selo ("ganhou o selo T") = <c>StudentBadge.UnlockedAt</c> + <c>Badge.Title</c>.
/// Ficam de fora por falta de dado: missão concluída e nível alcançado (sem hora gravada) e história lida (sem registro).
/// Mesmo escopo de unidade e filtro de faixa etária do <see cref="GetDashboard"/>; o período do filtro não se aplica
/// (a janela é fixa). Ordem: mais recente primeiro; no mesmo instante, quiz antes de selo e depois o identificador.
/// Duas consultas, cada uma com o limite aplicado no banco.
/// </summary>
public static class GetDashboardFeed
{
    /// <summary>PROPOSTA (a validar com a contratante): janela de 24 horas corridas até agora, como o estado vazio da preview.</summary>
    public const int WindowHours = 24;

    /// <summary>PROPOSTA (a validar com a contratante): cinco itens, como a preview.</summary>
    public const int Limit = 5;

    public static async Task<DashboardFeed> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, DashboardQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var since = now.AddHours(-WindowHours);
        var students = from s in GetDashboard.ScopedStudents(db, user, query, DateOnly.FromDateTime(BrasiliaTime.ToLocal(now)))
                       join u in db.Units on s.UnitId equals u.Id
                       select new { s.Id, s.FullName, UnitName = u.Name };

        var quizzes = await (from qs in db.QuizSessions
                             where qs.CompletedAt >= since && qs.CompletedAt <= now
                             join s in students on qs.StudentId equals s.Id
                             let total = db.QuizAnswers.Count(a => a.SessionId == qs.Id)
                             let correct = db.QuizAnswers.Count(a => a.SessionId == qs.Id && a.IsCorrect)
                             where total > 0
                             orderby qs.CompletedAt descending, qs.Id
                             // CompletedAt!: o filtro da janela já exclui sessão sem fim.
                             select new { qs.Id, s.FullName, s.UnitName, qs.QuizType, At = qs.CompletedAt!.Value, Total = total, Correct = correct })
            .Take(Limit).ToListAsync(cancellationToken);
        var badges = await (from sb in db.StudentBadges
                            where sb.UnlockedAt >= since && sb.UnlockedAt <= now
                            join s in students on sb.StudentId equals s.Id
                            join b in db.Badges on sb.BadgeId equals b.Id
                            orderby sb.UnlockedAt descending, sb.Id
                            select new { sb.Id, s.FullName, s.UnitName, b.Title, At = sb.UnlockedAt })
            .Take(Limit).ToListAsync(cancellationToken);

        var events = quizzes
            .Select(q => (q.Id, Event: new FeedEvent(
                FeedEventKind.QuizCompleted, FirstName(q.FullName), q.UnitName, $"acertou {q.Correct} de {q.Total} no quiz", q.QuizType, q.At)))
            .Concat(badges.Select(b => (b.Id, Event: new FeedEvent(
                FeedEventKind.BadgeUnlocked, FirstName(b.FullName), b.UnitName, $"ganhou o selo {b.Title}", null, b.At))))
            .OrderByDescending(e => e.Event.At).ThenBy(e => e.Event.Kind).ThenBy(e => e.Id)
            .Take(Limit).Select(e => e.Event).ToList();

        return new DashboardFeed(events);
    }

    /// <summary>PROPOSTA (a validar com a contratante): o aluno é identificado só pelo primeiro nome (até o primeiro espaço)
    /// e pela unidade. O nome completo é lido para o corte e descartado aqui; não sai deste caso de uso.</summary>
    private static string FirstName(string fullName) =>
        fullName.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
}
