using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Identidade;

/// <summary>
/// Decora a porta de definir senha: quando a senha é gravada, o aviso "sua senha foi alterada" entra na fila na mesma
/// transação (ADR-0003 §5.5 e §11.3). Ou a senha muda e o aviso fica enfileirado, ou nada muda.
/// O trabalho leva só o id da conta: o destinatário (o responsável, no caso do aluno) é resolvido pelo tratador de e-mail.
/// </summary>
public sealed class PasswordChangeNotifyingGateway(IPasswordLinkGateway inner, AppDbContext db, IJobQueue queue) : IPasswordLinkGateway
{
    /// <summary>§6.2: o aviso de senha alterada vale 24 horas na fila.</summary>
    public static TimeSpan ValidadeDoTrabalho { get; } = TimeSpan.FromHours(24);

    public Task<bool> IsValidAsync(Guid userId, string token, CancellationToken cancellationToken) =>
        inner.IsValidAsync(userId, token, cancellationToken);

    public async Task<DefinirSenhaResult> SetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await inner.SetPasswordAsync(userId, token, newPassword, cancellationToken);
        if (result.Status == DefinirSenhaStatus.Definida)
        {
            // Um aviso por troca (§5.4): a chave leva o resumo do carimbo que a troca acabou de gravar.
            var stamp = await db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync(cancellationToken)
                ?? throw new InvalidOperationException("Conta sem carimbo de segurança depois da troca de senha.");
            var key = $"{EmailJobTypes.PasswordChanged}:{userId:D}:{SecurityStampDigest.Of(stamp)}";
            await queue.EnqueueAsync(new JobRequest(EmailJobTypes.PasswordChanged, userId, new JobPayload(), ValidadeDoTrabalho, key), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
