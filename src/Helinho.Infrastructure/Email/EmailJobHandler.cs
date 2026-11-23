using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Domain;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// Tratador dos e-mails de acesso (ADR-0003 §5.3 e §11). A fila traz só o tipo e o id: aqui o destinatário é resolvido,
/// as condições do fluxo são conferidas de novo, o token nasce, o modelo é renderizado e a resposta do provedor vira
/// resultado da fila (§4.2). Não grava nada e não loga: destinatário, token e link não saem daqui.
/// </summary>
public sealed class EmailJobHandler(IEmailSubjects subjects, EmailTemplateRenderer renderer, IEmailSender sender, IClock clock) : IJobHandler
{
    public const string SubjectMissing = "subject_missing";
    public const string RecipientMissing = "recipient_missing";
    public const string RecipientInvalid = "recipient_invalid";
    public const string NotEligible = "not_eligible";
    public const string NotApproved = "not_approved";
    public const string TemplateMissing = "template_missing";

    // QueueJobs.ExternalId é varchar(200): um id maior quebraria a gravação final e o e-mail sairia de novo.
    private const int MaxExternalIdLength = 200;

    /// <summary>§6.3: erro de conta no provedor pausa o laço por 15 minutos, sem gastar tentativa.</summary>
    public static readonly TimeSpan AccountPause = TimeSpan.FromMinutes(15);

    /// <summary>§6.2: 8 tentativas, espera de 1 a 60 minutos, lote de 10, 10 s por item (o prazo do cliente HTTP) e posse de 2 minutos.</summary>
    public static JobPolicy EmailPolicy { get; } = new(
        MaxAttempts: 8, MinBackoff: TimeSpan.FromMinutes(1), MaxBackoff: TimeSpan.FromMinutes(60),
        BatchSize: 10, ItemTimeout: BrevoEmailSender.RequestTimeout, Lease: TimeSpan.FromMinutes(2));

    public IReadOnlyCollection<string> Types { get; } =
    [
        EmailJobTypes.AdminInvite, EmailJobTypes.PasswordReset, EmailJobTypes.PasswordChanged,
        EmailJobTypes.StudentAccess, EmailJobTypes.GuardianEmailChanged,
    ];

    public JobPolicy Policy => EmailPolicy;

    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
    {
        var subject = job.SubjectId is { } id ? await FindAsync(job.Type, id, cancellationToken) : null;
        if (subject is null)
        {
            return new JobResult.Fail(SubjectMissing);
        }

        var draft = Draft(job.Type, subject);
        var refusal = draft.Refusal
            ?? (string.IsNullOrWhiteSpace(draft.Recipient) ? RecipientMissing : null)
            ?? (EmailAddress.IsValid(draft.Recipient) ? null : RecipientInvalid)
            ?? (draft.Model is null ? TemplateMissing : null);
        if (refusal is not null || draft.Recipient is null || draft.Model is null)
        {
            return new JobResult.Fail(refusal ?? TemplateMissing);
        }

        // O token nasce depois de todas as conferências e logo antes do envio: nunca fica em repouso (§5.3),
        // e a validade conta do envio de verdade. Vai no formato que a página de definir senha lê (§10.3).
        var token = draft.TokenProvider is { } provider
            ? LinkDeAcesso.TokenParaUrl(await subjects.GenerateLinkTokenAsync(subject.UserId, provider, cancellationToken))
            : "";
        var email = renderer.Render(draft.Model(token));

        // §4.1 e §5.4: a etiqueta é o tipo, nunca dado pessoal, e o id do trabalho é a chave de idempotência no provedor.
        var sent = await sender.SendAsync(
            new EmailMessage(draft.Recipient, email.Subject, email.Html, email.Text, job.Type, job.Id), cancellationToken);
        return Translate(sent);
    }

    private async Task<EmailSubject?> FindAsync(string type, Guid id, CancellationToken cancellationToken) => type switch
    {
        EmailJobTypes.AdminInvite or EmailJobTypes.PasswordReset => await subjects.FindByUserAsync(id, cancellationToken),
        EmailJobTypes.StudentAccess or EmailJobTypes.GuardianEmailChanged => await subjects.FindByStudentAsync(id, cancellationToken),
        // Quem enfileira o aviso é a página de definir senha, que só conhece a conta; o id do aluno também é aceito.
        EmailJobTypes.PasswordChanged => await subjects.FindByUserAsync(id, cancellationToken)
            ?? await subjects.FindByStudentAsync(id, cancellationToken),
        _ => null,
    };

    /// <summary>Destinatário, provedor do token e modelo de cada tipo, com as condições conferidas de novo na hora de enviar (§5.3).</summary>
    private static Outgoing Draft(string type, EmailSubject subject) => (type, subject.Student) switch
    {
        // O convite de 72 horas só vale para conta sem senha (§10.1); o modelo cita a unidade que a pessoa vai administrar.
        (EmailJobTypes.AdminInvite, _) => subject is { IsPanelAccount: true, HasPassword: false, UnitName: { } unit }
            ? new(subject.Email, LinkDeAcesso.ProvedorConvite,
                token => new AdminInviteEmail(subject.Name, unit, subject.UserId, token, LinkDeAcesso.ValidadeConvite))
            : new(Refusal: NotEligible),

        // §11.1: só master e administrador pedem redefinição.
        (EmailJobTypes.PasswordReset, _) => subject.IsPanelAccount
            ? new(subject.Email, LinkDeAcesso.ProvedorRedefinicao,
                token => new PasswordResetEmail(subject.UserId, token, LinkDeAcesso.ValidadeRedefinicao))
            : new(Refusal: NotEligible),

        // §11.3: no caso do aluno, o aviso vai para o responsável.
        (EmailJobTypes.PasswordChanged, { } student) => new(student.GuardianEmail, Model: _ => new PasswordChangedEmail(student.FirstName)),
        (EmailJobTypes.PasswordChanged, null) => new(subject.Email, Model: _ => new PasswordChangedEmail(subject.Name)),

        // §11.4: sem aprovação não envia; o link segue o estado da conta agora (sem senha, 72 horas; com senha, 1 hora).
        (EmailJobTypes.StudentAccess, { Approved: false }) => new(Refusal: NotApproved),
        (EmailJobTypes.StudentAccess, { } student) => StudentAccess(subject, student),

        // Encaixe da Issue 11 do plano: o quinto modelo entra aqui, como Model. Sem ele, o tipo falha com template_missing.
        (EmailJobTypes.GuardianEmailChanged, { } student) => new(student.PreviousGuardianEmail),

        _ => new(Refusal: SubjectMissing),
    };

    private static Outgoing StudentAccess(EmailSubject subject, EmailStudent student)
    {
        var (provider, validity) = subject.HasPassword
            ? (LinkDeAcesso.ProvedorRedefinicao, LinkDeAcesso.ValidadeRedefinicao)
            : (LinkDeAcesso.ProvedorConvite, LinkDeAcesso.ValidadeConvite);
        return new(student.GuardianEmail, provider,
            token => new StudentAccessEmail(student.FirstName, student.Enrollment, subject.UserId, token, validity));
    }

    /// <summary>Tabela do §4.2: o que o provedor respondeu vira o que a fila faz com o trabalho.</summary>
    private JobResult Translate(EmailSendResult sent) => sent.Status switch
    {
        EmailSendStatus.Sent => new JobResult.Succeeded(sent.MessageId is { Length: <= MaxExternalIdLength } messageId ? messageId : null),
        EmailSendStatus.PermanentFailure => new JobResult.Fail(Code(sent.ErrorCode, "send_rejected")),
        EmailSendStatus.ProviderSuspended => new JobResult.Suspend(Code(sent.ErrorCode, "provider_suspended"), AccountPause),
        _ => new JobResult.Retry(Code(sent.ErrorCode, "send_failed"), sent.RetryAfter is { } wait ? clock.UtcNow + wait : null),
    };

    /// <summary>O código do provedor só passa se couber no vocabulário fechado da fila; o que não couber vira o genérico.</summary>
    private static string Code(string? code, string fallback)
    {
        try
        {
            return JobCode.Require(code ?? fallback, nameof(code));
        }
        catch (ArgumentException)
        {
            return fallback;
        }
    }

    /// <summary>Rascunho do envio. <paramref name="Refusal"/> é o código fechado de por que o e-mail não sai.</summary>
    private sealed record Outgoing(
        string? Recipient = null, string? TokenProvider = null, Func<string, EmailModel>? Model = null, string? Refusal = null);
}
