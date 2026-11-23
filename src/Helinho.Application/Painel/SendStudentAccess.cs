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

/// <summary>Botão "Enviar acesso" (ADR-0003 §11.4): enfileira o e-mail de acesso do aluno para o responsável.</summary>
public static class SendStudentAccess
{
    public static async Task<SendStudentAccessResult> HandleAsync(
        IAppDbContext db, ICurrentUser user, StudentAccessRequests requests, Guid studentId, CancellationToken cancellationToken)
    {
        // T13: só o administrador, e só aluno da unidade dele. O filtro global de Student já restringe;
        // a comparação explícita não depende dele. Outra unidade responde igual a um id que não existe.
        if (user.UnitId is not { } unitId || !user.IsInRole(RoleNames.Admin))
        {
            return SendStudentAccessResult.NotFound;
        }

        var student = await db.Students.Where(s => s.Id == studentId && s.UnitId == unitId)
            .Select(s => new { s.GuardianApproved, HasGuardianEmail = s.GuardianEmail != null })
            .SingleOrDefaultAsync(cancellationToken);
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
}
