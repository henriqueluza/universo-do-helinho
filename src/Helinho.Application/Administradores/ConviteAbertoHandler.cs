using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Administradores;

public enum ConviteStatus
{
    Done,
    /// <summary>Conta inexistente, que não é de administrador, que já tem senha ou está inativa: não há convite em aberto.</summary>
    NotInvited,
    RateLimited,
    /// <summary>A janela de 2 minutos já tem um convite que saiu (chave de idempotência, ADR-0003 §5.4).</summary>
    JustSent,
    /// <summary>O trabalhador pegou o convite pendente no meio da operação; nada foi gravado.</summary>
    SendingNow,
}

/// <summary>
/// Casos de uso sobre o convite em aberto (conta de administrador ainda sem senha, ADR-0003 §11.2): reenviar e cancelar.
/// Os dois rodam sob o lock da unidade, o mesmo da criação, e gravam tudo numa transação só.
/// </summary>
public sealed class ConviteAbertoHandler(IAdminAccountGateway accounts, IAdminInviteJobs invites, IJobQueue queue, IClock clock)
{
    /// <summary>§11.6: convites por conta por hora, contados na fila; o da criação conta.</summary>
    public const int LimitePorHora = 3;

    public async Task<AdminAccount?> AbertoAsync(Guid accountId, CancellationToken cancellationToken) =>
        await accounts.FindAdminAsync(accountId, cancellationToken) is { HasPassword: false } account
        && CriarAdministradorHandler.IsActive(account, clock.UtcNow) ? account : null;

    public async Task<ConviteStatus> ReenviarAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (await AbertoAsync(accountId, cancellationToken) is not { } account)
        {
            return ConviteStatus.NotInvited;
        }

        await using var unitLock = await accounts.BeginUnitLockAsync(account.UnitId ?? account.Id, cancellationToken);
        var now = clock.UtcNow;
        if (await invites.CountSinceAsync(accountId, now.AddHours(-1), cancellationToken) >= LimitePorHora)
        {
            return ConviteStatus.RateLimited;
        }

        // §10.2: trocar o carimbo invalida o link anterior. Falso = a conta sumiu ou ganhou senha no meio do caminho.
        if (!await accounts.RotateSecurityStampAsync(accountId, cancellationToken))
        {
            return ConviteStatus.NotInvited;
        }

        await queue.CancelPendingAsync(EmailJobTypes.AdminInvite, accountId, cancellationToken);
        if (await queue.EnqueueAsync(ConviteAdministrador.Trabalho(accountId, now), cancellationToken) is null)
        {
            return ConviteStatus.JustSent; // sem commit: a troca do carimbo é desfeita e o link que acabou de sair continua valendo
        }

        return await CommitAsync(unitLock, cancellationToken);
    }

    /// <summary>Apaga a conta que nunca teve senha (D11): sem isso, um e-mail digitado errado prende a unidade.</summary>
    public async Task<ConviteStatus> CancelarAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (await AbertoAsync(accountId, cancellationToken) is not { } account)
        {
            return ConviteStatus.NotInvited;
        }

        await using var unitLock = await accounts.BeginUnitLockAsync(account.UnitId ?? account.Id, cancellationToken);
        if (!await accounts.DeleteInvitedAdminAsync(accountId, cancellationToken))
        {
            return ConviteStatus.NotInvited;
        }

        await queue.CancelPendingAsync(EmailJobTypes.AdminInvite, accountId, cancellationToken);
        return await CommitAsync(unitLock, cancellationToken);
    }

    private static async Task<ConviteStatus> CommitAsync(IAdminUnitLock unitLock, CancellationToken cancellationToken)
    {
        try
        {
            await unitLock.CommitAsync(cancellationToken);
            return ConviteStatus.Done;
        }
        catch (DbUpdateConcurrencyException)
        {
            // IJobQueue.CancelPendingAsync: o trabalhador pegou o convite antes da gravação. A transação é desfeita inteira.
            return ConviteStatus.SendingNow;
        }
    }
}
