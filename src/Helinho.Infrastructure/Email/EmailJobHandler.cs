using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Application.LiveEvents;
using Helinho.Domain;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// Tratador dos e-mails de acesso (ADR-0003 §5.3 e §11). A fila traz só o tipo e o id: aqui o destinatário é resolvido,
/// as condições do fluxo são conferidas de novo, o token nasce, o modelo é renderizado e a resposta do provedor vira
/// resultado da fila (§4.2). Não salva e não loga: destinatário, token e link não saem daqui. A única mudança que deixa
/// no contexto é apagar o endereço anterior do responsável quando o aviso de troca termina (§9).
/// <paramref name="renderer"/> é nulo quando não há base pública (<c>App:PublicBaseUrl</c>) para montar os links.
/// </summary>
public sealed class EmailJobHandler(
    EmailLane lane, EmailDailyLimit dailyLimit, IEmailSubjects subjects, EmailTemplateRenderer? renderer, IEmailSender sender, IClock clock)
    : IJobHandler, IAbandonedJobHandler
{
    public const string BaseUrlMissing = "base_url_missing";
    public const string SubjectMissing = "subject_missing";
    public const string RecipientMissing = "recipient_missing";
    public const string RecipientInvalid = "recipient_invalid";
    public const string NotEligible = "not_eligible";
    public const string NotApproved = "not_approved";
    public const string TemplateMissing = "template_missing";

    /// <summary>§6.3: erro de conta no provedor pausa o laço por 15 minutos, sem gastar tentativa.</summary>
    public static readonly TimeSpan AccountPause = TimeSpan.FromMinutes(15);

    /// <summary>§6.2: 8 tentativas, espera de 1 a 60 minutos, lote de 10, 10 s por item (o prazo do cliente HTTP) e posse de 2 minutos.</summary>
    public static JobPolicy EmailPolicy { get; } = new(
        MaxAttempts: 8, MinBackoff: TimeSpan.FromMinutes(1), MaxBackoff: TimeSpan.FromMinutes(60),
        BatchSize: 10, ItemTimeout: BrevoEmailSender.RequestTimeout, Lease: TimeSpan.FromMinutes(2));

    /// <summary>Os tipos da raia deste registro (§15): urgentes ou demais.</summary>
    public IReadOnlyCollection<string> Types => lane.Types;

    public JobPolicy Policy => EmailPolicy;

    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
    {
        if (renderer is null)
        {
            // Problema de configuração, não da mensagem: como no erro de conta (§6.3), espera sem gastar tentativa.
            return new JobResult.Suspend(BaseUrlMissing, AccountPause);
        }

        // §15: o teto segura a mensagem antes de ler a conta e de gerar o token, para o link não vencer parado.
        if (await dailyLimit.PauseForAsync(lane, cancellationToken) is { } pause)
        {
            return new JobResult.Suspend(EmailDailyLimit.Code, pause);
        }

        var result = await SendAsync(job, renderer, cancellationToken);
        if (result is JobResult.Succeeded or JobResult.Fail)
        {
            // §9: resultado final. Em nova tentativa ou suspensão o aviso ainda vai sair, e o endereço fica.
            await ForgetPreviousGuardianEmailAsync(job, cancellationToken);
        }

        return result;
    }

    /// <summary>§6.7: a fila encerrou o trabalho (<c>expired</c> ou <c>max_attempts</c>); o aviso não sai mais.</summary>
    public Task OnAbandonedAsync(JobContext job, string reason, CancellationToken cancellationToken) =>
        ForgetPreviousGuardianEmailAsync(job, cancellationToken);

    private Task ForgetPreviousGuardianEmailAsync(JobContext job, CancellationToken cancellationToken) =>
        job is { Type: EmailJobTypes.GuardianEmailChanged, SubjectId: { } studentId }
            ? subjects.StagePreviousGuardianEmailRemovalAsync(studentId, cancellationToken)
            : Task.CompletedTask;

    private async Task<JobResult> SendAsync(JobContext job, EmailTemplateRenderer renderer, CancellationToken cancellationToken)
    {
        var subject = job.SubjectId is { } id ? await FindAsync(job.Type, id, cancellationToken) : null;
        if (subject is null)
        {
            return new JobResult.Fail(SubjectMissing);
        }

        var draft = Draft(job, subject);
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
    private static Outgoing Draft(JobContext job, EmailSubject subject) => (job.Type, subject.Student) switch
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

        // §11.4: sem aprovação não envia; o link e o texto seguem o estado da conta agora (sem senha, primeiro acesso
        // e 72 horas; com senha, troca de senha e 1 hora).
        (EmailJobTypes.StudentAccess, { Approved: false }) => new(Refusal: NotApproved),
        (EmailJobTypes.StudentAccess, { } student) => StudentAccess(subject, student),

        // §9 e §11.4: o aviso vai ao endereço anterior, sem link. O trabalho nasce na gravação da troca: a data é a dele.
        (EmailJobTypes.GuardianEmailChanged, { } student) => new(student.PreviousGuardianEmail,
            Model: _ => new GuardianEmailChangedEmail(student.FirstName, DateOnly.FromDateTime(BrasiliaTime.ToLocal(job.CreatedAt)))),

        _ => new(Refusal: SubjectMissing),
    };

    private static Outgoing StudentAccess(EmailSubject subject, EmailStudent student)
    {
        var (provider, validity) = subject.HasPassword
            ? (LinkDeAcesso.ProvedorRedefinicao, LinkDeAcesso.ValidadeRedefinicao)
            : (LinkDeAcesso.ProvedorConvite, LinkDeAcesso.ValidadeConvite);
        return new(student.GuardianEmail, provider,
            token => new StudentAccessEmail(student.FirstName, student.Enrollment, subject.UserId, token, validity, subject.HasPassword));
    }

    /// <summary>Tabela do §4.2: o que o provedor respondeu vira o que a fila faz com o trabalho.</summary>
    private JobResult Translate(EmailSendResult sent) => sent.Status switch
    {
        // §12: o id é guardado como o webhook o procura (sem < >, até o tamanho da coluna), pelo mesmo normalizador.
        EmailSendStatus.Sent => new JobResult.Succeeded(EmailFeedback.NormalizeMessageId(sent.MessageId)),
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
