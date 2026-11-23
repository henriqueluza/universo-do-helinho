using Helinho.Application.Abstractions;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public enum SendStudentAccessResult
{
    Queued,
    NotFound,
    NotApproved,
    MissingGuardianEmail,
    LimitReached,
}

/// <summary>O que a ficha do aluno mostra: o que falta para enviar, o que o botão vai enviar e a situação do último pedido.</summary>
public sealed record StudentAccessState(bool Approved, bool HasGuardianEmail, bool HasPassword, StudentAccessHistory History)
{
    public bool LimitReached => History.RecentCount >= StudentAccessRequests.HourlyLimit;

    public bool CanSend => Approved && HasGuardianEmail && !LimitReached;
}

/// <summary>Botão "Enviar acesso" (ADR-0003 §11.4): enfileira o e-mail de acesso do aluno para o responsável.</summary>
public static class SendStudentAccess
{
    public static async Task<SendStudentAccessResult> HandleAsync(
        IAppDbContext db, ICurrentUser user, StudentAccessRequests requests, Guid studentId, CancellationToken cancellationToken)
    {
        var student = await FindAsync(db, user, studentId, cancellationToken);
        if (student is not { GuardianApproved: true, HasGuardianEmail: true })
        {
            return student is null ? SendStudentAccessResult.NotFound
                : student.GuardianApproved ? SendStudentAccessResult.MissingGuardianEmail : SendStudentAccessResult.NotApproved;
        }

        var staged = await requests.StageAsync(studentId, cancellationToken);
        if (staged == StudentAccessRequest.LimitReached)
        {
            return SendStudentAccessResult.LimitReached;
        }

        if (staged == StudentAccessRequest.Queued)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Clique duplo em corrida: o índice único da chave de idempotência recusou esta linha porque a outra
                // requisição gravou a dela. Só é "na fila" se o pedido existe mesmo; qualquer outra falha sobe.
                if ((await requests.HistoryAsync(studentId, cancellationToken)).LastStatus != DeliveryStatus.Queued)
                {
                    throw;
                }
            }
        }

        return SendStudentAccessResult.Queued;
    }

    /// <summary>Estado para a ficha; <c>null</c> se o aluno não existe na unidade de quem pede (a tela responde 404).</summary>
    public static async Task<StudentAccessState?> StateAsync(
        IAppDbContext db, ICurrentUser user, StudentAccessRequests requests, IStudentAccounts accounts, Guid studentId, CancellationToken cancellationToken) =>
        await FindAsync(db, user, studentId, cancellationToken) is { } student
            ? new StudentAccessState(
                student.GuardianApproved, student.HasGuardianEmail,
                await accounts.HasPasswordAsync(student.UserId, cancellationToken), await requests.HistoryAsync(studentId, cancellationToken))
            : null;

    // T13: só o administrador, e só aluno da unidade dele. O filtro global de Student já restringe;
    // a comparação explícita não depende dele. Outra unidade responde igual a um id que não existe.
    private static async Task<Found?> FindAsync(IAppDbContext db, ICurrentUser user, Guid studentId, CancellationToken cancellationToken) =>
        user.UnitId is { } unitId && user.IsInRole(RoleNames.Admin)
            ? await db.Students.Where(s => s.Id == studentId && s.UnitId == unitId)
                .Select(s => new Found(s.UserId, s.GuardianApproved, s.GuardianEmail != null))
                .SingleOrDefaultAsync(cancellationToken)
            : null;

    private sealed record Found(Guid UserId, bool GuardianApproved, bool HasGuardianEmail);
}
