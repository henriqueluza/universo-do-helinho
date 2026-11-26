namespace Helinho.Application.Email;

/// <summary>Tipos de trabalho de e-mail na fila (ADR-0003, seção 5.1).</summary>
public static class EmailJobTypes
{
    public const string AdminInvite = "email.admin-invite";
    public const string PasswordReset = "email.password-reset";
    public const string PasswordChanged = "email.password-changed";
    public const string StudentAccess = "email.student-access";
    public const string GuardianEmailChanged = "email.guardian-email-changed";
}
