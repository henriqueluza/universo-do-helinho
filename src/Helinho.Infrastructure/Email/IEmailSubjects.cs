namespace Helinho.Infrastructure.Email;

/// <summary>
/// O que o tratador de e-mail lê na hora de enviar (ADR-0003 §5.3): a fila guarda só o id, e o destinatário, o estado
/// da conta e o token nascem daqui. É porta para o tratador ser testado sem Identity nem banco. A única escrita é
/// <see cref="StagePreviousGuardianEmailRemovalAsync"/>, que não salva.
/// </summary>
public interface IEmailSubjects
{
    /// <summary>A conta com esse id de usuário; <see cref="EmailSubject.Student"/> vem preenchido quando é conta de aluno.</summary>
    Task<EmailSubject?> FindByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>O aluno com esse id e a conta dele; nulo se faltar um dos dois.</summary>
    Task<EmailSubject?> FindByStudentAsync(Guid studentId, CancellationToken cancellationToken);

    /// <summary>Token do Identity para o link de definir senha (§10.1), pelo provedor de convite ou de redefinição.</summary>
    Task<string> GenerateLinkTokenAsync(Guid userId, string provider, CancellationToken cancellationToken);

    /// <summary>
    /// Apaga <c>Student.PreviousGuardianEmail</c> quando o aviso de troca termina (§9). Não salva: a mudança fica no
    /// contexto e o processador a grava junto com a conclusão do trabalho (§6.6).
    /// </summary>
    Task StagePreviousGuardianEmailRemovalAsync(Guid studentId, CancellationToken cancellationToken);
}

/// <summary>A conta como o e-mail precisa dela. <paramref name="Name"/> é o nome de usuário; <paramref name="IsPanelAccount"/>, master ou administrador.</summary>
public sealed record EmailSubject(
    Guid UserId, string Name, string? Email, string? UnitName, bool HasPassword, bool IsPanelAccount, EmailStudent? Student = null)
{
    /// <summary>Só o tipo: nome e endereço nunca vão para um log por descuido.</summary>
    public override string ToString() => nameof(EmailSubject);
}

/// <summary>Dado mínimo do aluno (§11.4): primeiro nome, matrícula, os endereços do responsável e a aprovação.</summary>
public sealed record EmailStudent(string FirstName, string Enrollment, string? GuardianEmail, string? PreviousGuardianEmail, bool Approved)
{
    public override string ToString() => nameof(EmailStudent);
}
