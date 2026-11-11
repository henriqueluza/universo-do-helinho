using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public sealed record QuestRow(Guid Id, string Title, int Target, int RewardXp, QuizType? QuizType);

/// <summary>Missões diárias no painel: listar e editar (criar/excluir fica fora de escopo).</summary>
public static class ManageDailyQuests
{
    public static async Task<IReadOnlyList<QuestRow>> ListAsync(IAppDbContext db, CancellationToken cancellationToken) =>
        await db.DailyQuests
            .OrderBy(q => q.Title)
            .Select(q => new QuestRow(q.Id, q.Title, q.Target, q.RewardXp, q.QuizType))
            .ToListAsync(cancellationToken);

    public static async Task<QuestRow?> FindAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.DailyQuests
            .Where(q => q.Id == id)
            .Select(q => new QuestRow(q.Id, q.Title, q.Target, q.RewardXp, q.QuizType))
            .SingleOrDefaultAsync(cancellationToken);

    /// <returns>Falso se a missão não existe; <see cref="DomainException"/> se os valores forem inválidos.</returns>
    public static async Task<bool> UpdateAsync(
        IAppDbContext db, Guid id, string title, int target, int rewardXp, QuizType? quizType, CancellationToken cancellationToken)
    {
        var quest = await db.DailyQuests.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quest is null)
        {
            return false;
        }

        quest.Update(title, target, rewardXp, quizType);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
