using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Persistence;

/// <summary>
/// Apaga <c>Student.PreviousGuardianEmail</c> quando o aviso de troca termina (ADR-0003 §9). É chamado pelo tratador de
/// e-mail em dois pontos: ao devolver o resultado final de <c>email.guardian-email-changed</c> (sucesso ou falha) e no
/// gancho <c>IAbandonedJobHandler</c> (<c>expired</c> e <c>max_attempts</c>, §6.7). <b>Não salva</b>: a mudança fica no
/// contexto e o processador a grava junto com a conclusão do trabalho (§6.6).
/// </summary>
public sealed class PreviousGuardianEmailCleaner(AppDbContext db)
{
    /// <param name="studentId">O <c>SubjectId</c> do trabalho.</param>
    public async Task StageAsync(Guid studentId, CancellationToken cancellationToken)
    {
        // No trabalhador o escopo do item não tem usuário; a consulta é só por id, o do próprio trabalho.
        // ignora-filtro: sem usuário, o filtro de unidade de Student não devolveria aluno nenhum.
        var student = await db.Students.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.Id == studentId, cancellationToken);
        student?.ClearPreviousGuardianEmail();
    }
}
