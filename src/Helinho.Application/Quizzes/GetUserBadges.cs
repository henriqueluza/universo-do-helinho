using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Quizzes;

public enum BadgesStatus { Ok, InvalidPage, Forbidden }

public sealed record BadgesResult(BadgesStatus Status, BadgesResponse? Badges = null);

/// <summary>
/// Caso de uso do GET /user/badges?page=N (P11). Tamanho de página fixo no servidor (24: 182 conquistas = 8 páginas, como no contrato).
/// Página ausente = 1; menor que 1 é inválida (400); além do fim devolve lista vazia. Regras de desbloqueio estão fora de escopo.
/// </summary>
public static class GetUserBadges
{
    public const int PageSize = 24;
    public const string InvalidPageCode = "invalid_page";

    public static ErrorResponse CreateInvalidPageError() =>
        new(InvalidPageCode, "page deve ser um inteiro a partir de 1.");

    public static async Task<BadgesResult> HandleAsync(
        IAppDbContext db, ICurrentStudent current, int? page, CancellationToken cancellationToken)
    {
        var number = page ?? 1;
        if (number < 1)
        {
            return new BadgesResult(BadgesStatus.InvalidPage);
        }

        var studentId = current.StudentId;

        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        if (!await db.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == studentId, cancellationToken))
        {
            return new BadgesResult(BadgesStatus.Forbidden);
        }

        var total = await db.Badges.CountAsync(cancellationToken);
        var totalPages = (total + PageSize - 1) / PageSize;

        var items = await db.Badges
            .OrderBy(b => b.Code)
            .Skip((number - 1) * PageSize)
            .Take(PageSize)
            .Select(b => new BadgeItem(
                b.Code, b.Title, db.StudentBadges.Any(sb => sb.StudentId == studentId && sb.BadgeId == b.Id), b.Visual))
            .ToListAsync(cancellationToken);

        return new BadgesResult(BadgesStatus.Ok, new BadgesResponse(items, number, totalPages));
    }
}
