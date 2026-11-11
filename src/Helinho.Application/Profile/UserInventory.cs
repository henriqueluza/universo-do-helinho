using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Entity = Helinho.Domain.Entities.InventoryItem;

namespace Helinho.Application.Profile;

/// <summary>
/// Casos de uso do GET e POST /user/inventory. O POST substitui a seleção inteira do aluno do token e responde com a
/// seleção gravada (P10). Regras (PROPOSTA): no máximo um item por <c>type</c>, só os cinco tipos do catálogo fixo (P12),
/// <c>id</c> e <c>name</c> obrigatórios e dentro do limite da coluna.
/// </summary>
public static class UserInventory
{
    public static readonly IReadOnlyList<string> KnownTypes = ["cabelo", "pele", "roupa", "rosto", "acessorio"];

    private const int MaxIdLength = 60;
    private const int MaxNameLength = 120;

    public static async Task<InventoryResponse?> GetAsync(
        IAppDbContext db, ICurrentStudent current, CancellationToken cancellationToken) =>
        await StudentExistsAsync(db, current.StudentId, cancellationToken)
            ? await ReadAsync(db, current.StudentId, cancellationToken)
            : null;

    /// <summary>Devolve null se o aluno não existe; lança <see cref="InvalidInventoryException"/> se a seleção é inválida.</summary>
    public static async Task<InventoryResponse?> ReplaceAsync(
        IAppDbContext db, ICurrentStudent current, InventoryRequest? request, CancellationToken cancellationToken)
    {
        var selected = Validate(request);
        var studentId = current.StudentId;
        if (!await StudentExistsAsync(db, studentId, cancellationToken))
        {
            return null;
        }

        db.InventoryItems.RemoveRange(await db.InventoryItems.Where(i => i.StudentId == studentId).ToListAsync(cancellationToken));
        db.InventoryItems.AddRange(selected.Select(i => new Entity(studentId, i.Id, i.Name, i.Type)));
        await db.SaveChangesAsync(cancellationToken);
        return await ReadAsync(db, studentId, cancellationToken);
    }

    private static IReadOnlyList<InventoryItem> Validate(InventoryRequest? request)
    {
        var selected = request?.Selected;
        if (selected is null || selected.Count > KnownTypes.Count)
        {
            throw new InvalidInventoryException();
        }

        foreach (var item in selected)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > MaxIdLength
                || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > MaxNameLength
                || item.Type is null || !KnownTypes.Contains(item.Type))
            {
                throw new InvalidInventoryException();
            }
        }

        return selected.Select(i => i.Type).Distinct().Count() == selected.Count
            ? selected
            : throw new InvalidInventoryException();
    }

    private static Task<bool> StudentExistsAsync(IAppDbContext db, Guid studentId, CancellationToken ct) =>
        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        db.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == studentId, ct);

    private static async Task<InventoryResponse> ReadAsync(IAppDbContext db, Guid studentId, CancellationToken ct) =>
        new(await db.InventoryItems.Where(i => i.StudentId == studentId).OrderBy(i => i.Type)
            .Select(i => new InventoryItem(i.ItemId, i.Name, i.Type)).ToListAsync(ct));
}

/// <summary>Seleção de inventário fora das regras do contrato; o endpoint traduz para 400.</summary>
public sealed class InvalidInventoryException : Exception;
