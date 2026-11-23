namespace Helinho.Infrastructure.Email;

/// <summary>
/// Os e-mails de acesso (ADR-0003 §8 e §11): conjunto fechado, um record por e-mail, com os parâmetros que o modelo usa.
/// Nenhum leva senha. O link não é parâmetro: o renderizador o monta com o id da conta e o token.
/// </summary>
public abstract record EmailModel
{
    private protected EmailModel() { }

    /// <summary>Só o tipo: o token e os nomes nunca vão para um log por descuido.</summary>
    public sealed override string ToString() => GetType().Name;
}

/// <summary>Convite de administrador: nome de quem foi convidado e a unidade que vai administrar.</summary>
public sealed record AdminInviteEmail(string AdminName, string UnitName, Guid UserId, string Token, TimeSpan Validity) : EmailModel;

/// <summary>Redefinição de senha do painel, pedida pelo formulário anônimo.</summary>
public sealed record PasswordResetEmail(Guid UserId, string Token, TimeSpan Validity) : EmailModel;

/// <summary>Aviso "a senha foi alterada". <paramref name="AccountName"/>: nome do administrador ou primeiro nome do aluno.</summary>
public sealed record PasswordChangedEmail(string AccountName) : EmailModel;

/// <summary>Acesso do aluno, para o responsável. Dado mínimo (ADR-0003 §11.4): primeiro nome e matrícula.</summary>
public sealed record StudentAccessEmail(string StudentFirstName, string Enrollment, Guid UserId, string Token, TimeSpan Validity) : EmailModel;

/// <summary>
/// Aviso ao endereço <b>anterior</b> do responsável de que o e-mail foi trocado (ADR-0003 §11.4). Dado mínimo: primeiro nome
/// do aluno e a data da troca em Brasília. Sem o endereço novo, sem matrícula e sem link.
/// </summary>
public sealed record GuardianEmailChangedEmail(string StudentFirstName, DateOnly ChangedOn) : EmailModel;
