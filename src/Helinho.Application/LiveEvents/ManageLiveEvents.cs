using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.LiveEvents;

/// <summary>Linha da listagem do painel; <see cref="Active"/> é calculado contra o relógio injetado.</summary>
public sealed record LiveEventRow(Guid Id, string Title, string Subtitle, QuizType QuizType, DateTimeOffset EndsAt, bool Active);

/// <summary>Listagem de eventos do painel: ativos primeiro (término mais próximo), depois os encerrados (mais recentes primeiro).</summary>
public static class ListLiveEvents
{
    public static async Task<IReadOnlyList<LiveEventRow>> HandleAsync(
        IAppDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var all = await db.LiveEvents.AsNoTracking().ToListAsync(cancellationToken);
        var rows = all.Select(e => new LiveEventRow(e.Id, e.Title, e.Subtitle, e.QuizType, e.EndsAt, e.EndsAt > now)).ToList();
        return
        [
            .. rows.Where(r => r.Active).OrderBy(r => r.EndsAt),
            .. rows.Where(r => !r.Active).OrderByDescending(r => r.EndsAt),
        ];
    }
}

/// <summary>Criação de evento: o evento aponta para exatamente um tipo de quiz e o término precisa estar no futuro.</summary>
public static class CreateLiveEvent
{
    public const int TitleMax = 120;
    public const int SubtitleMax = 200;

    /// <exception cref="DomainException">Dado inválido (mensagem em português, própria para exibir).</exception>
    public static async Task<Guid> HandleAsync(
        IAppDbContext db, IClock clock, string? title, string? subtitle, QuizType quizType, DateTimeOffset endsAt,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(quizType))
        {
            throw new DomainException("Escolha um tipo de quiz.");
        }

        if (title?.Trim().Length > TitleMax)
        {
            throw new DomainException($"O título deve ter até {TitleMax} caracteres.");
        }

        if (subtitle?.Trim().Length > SubtitleMax)
        {
            throw new DomainException($"O subtítulo deve ter até {SubtitleMax} caracteres.");
        }

        if (endsAt <= clock.UtcNow)
        {
            throw new DomainException("O término deve estar no futuro.");
        }

        var liveEvent = new LiveEvent(title ?? "", subtitle?.Trim() ?? "", quizType, endsAt.ToUniversalTime());
        db.LiveEvents.Add(liveEvent);
        await db.SaveChangesAsync(cancellationToken);
        return liveEvent.Id;
    }
}
