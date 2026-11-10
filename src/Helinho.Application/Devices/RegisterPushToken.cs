using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Devices;

/// <summary>
/// Caso de uso do POST /devices/push-token (P9). Só registra o aparelho do aluno do token; não envia nada.
/// Idempotente por (usuário, token). Um token pertence a um único usuário: se era de outro aluno (tablet
/// compartilhado), o registro antigo é removido e o token passa para o atual. O token nunca é logado.
/// </summary>
public static class RegisterPushToken
{
    internal const int MaxTokenLength = 512; // limite da coluna

    private static readonly IReadOnlyList<string> Platforms = ["android", "ios"];

    /// <summary>Devolve false se o aluno não existe; lança <see cref="InvalidPushTokenException"/> se o corpo é inválido.</summary>
    public static async Task<bool> HandleAsync(
        IAppDbContext db, ICurrentStudent current, IClock clock, PushTokenRequest? request, CancellationToken cancellationToken)
    {
        if (request is not { Token: { } token, Platform: { } platform }
            || string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength || token != token.Trim()
            || !Platforms.Contains(platform))
        {
            throw new InvalidPushTokenException();
        }

        // ignora-filtro: a API do jogo não tem unidade de painel; o escopo é o próprio aluno do token.
        var userId = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Id == current.StudentId).Select(s => (Guid?)s.UserId).SingleOrDefaultAsync(cancellationToken);
        if (userId is null)
        {
            return false;
        }

        // Serializa por token: sem isso dois registros paralelos do mesmo token (alunos diferentes, ou retry
        // do mesmo aluno) leem o estado antigo e gravam duas linhas. O índice do banco é só (UserId, Token).
        await using var transaction = await db.BeginLockedTransactionAsync("push-token:" + token, cancellationToken);
        var existing = await db.PushTokens.Where(t => t.Token == token).ToListAsync(cancellationToken);
        if (existing.Count == 1 && existing[0].UserId == userId && existing[0].Platform == platform)
        {
            return true;
        }

        db.PushTokens.RemoveRange(existing);
        db.PushTokens.Add(new PushToken(userId.Value, token, platform, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

/// <summary>Corpo do POST /devices/push-token fora das regras; o endpoint traduz para 400.</summary>
public sealed class InvalidPushTokenException : Exception;
