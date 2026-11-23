using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Domain.Entities;

namespace Helinho.Application.Painel;

public enum StudentAccessRequest
{
    Queued,
    AlreadyQueued,
    LimitReached,
}

/// <summary>
/// Pedidos de acesso do aluno na fila (ADR-0003 §11.4). Só referências (§5.3): o tratador resolve o destinatário, escolhe
/// o link pelo estado da conta e confere de novo a aprovação na hora de enviar. Nada aqui salva: quem chama grava junto (§5.5).
/// </summary>
public sealed class StudentAccessRequests(IJobQueue queue, IStudentAccessJobs jobs, IStudentAccounts accounts, IClock clock)
{
    public const int HourlyLimit = 3;

    /// <summary>§6.2: depois disso o trabalho não é mais processado.</summary>
    public static readonly TimeSpan ExpiresIn = TimeSpan.FromHours(72);

    /// <summary>§6.2 e §9: o endereço anterior não fica mais de um dia no banco.</summary>
    public static readonly TimeSpan NoticeExpiresIn = TimeSpan.FromHours(24);

    private const int IdempotencyWindowSeconds = 120;

    /// <summary>Pedidos da última hora (os que contam para o limite) e a situação do mais recente.</summary>
    public Task<StudentAccessHistory> HistoryAsync(Guid studentId, CancellationToken cancellationToken) =>
        jobs.ReadAsync(studentId, clock.UtcNow.AddHours(-1), cancellationToken);

    /// <summary>Acrescenta o pedido ao contexto, dentro do limite por hora. Clique duplo cai na mesma chave de idempotência (§5.4).</summary>
    public async Task<StudentAccessRequest> StageAsync(Guid studentId, CancellationToken cancellationToken)
    {
        if ((await HistoryAsync(studentId, cancellationToken)).RecentCount >= HourlyLimit)
        {
            return StudentAccessRequest.LimitReached;
        }

        var key = $"{EmailJobTypes.StudentAccess}:{studentId}:{clock.UtcNow.ToUnixTimeSeconds() / IdempotencyWindowSeconds}";
        var request = new JobRequest(EmailJobTypes.StudentAccess, studentId, new JobPayload(), ExpiresIn, key);
        return await queue.EnqueueAsync(request, cancellationToken) is null ? StudentAccessRequest.AlreadyQueued : StudentAccessRequest.Queued;
    }

    /// <summary>
    /// Gatilho da aprovação (D12): só para conta <b>sem senha</b>. Quem já tem senha e teve a aprovação desmarcada e marcada
    /// de novo não recebe e-mail; se precisar, o administrador usa o botão. Devolve se o pedido entrou no contexto.
    /// </summary>
    public async Task<bool> StageOnApprovalAsync(Guid studentId, Guid userId, CancellationToken cancellationToken) =>
        !await accounts.HasPasswordAsync(userId, cancellationToken)
        && await StageAsync(studentId, cancellationToken) == StudentAccessRequest.Queued;

    /// <summary>
    /// Efeitos de segurança da troca do e-mail do responsável (§9, §11.4; T13 e T16), sem salvar: troca o carimbo da conta
    /// (link já enviado ao endereço anterior deixa de valer), cancela os acessos pendentes e enfileira o aviso ao endereço
    /// anterior. A fila não guarda destinatário (§5.3): ele fica em <see cref="Student.PreviousGuardianEmail"/> até o aviso
    /// terminar. Com aviso ainda por enviar, nada é sobrescrito: o aviso vai para quem tinha o endereço antes da primeira troca.
    /// </summary>
    public async Task StageGuardianEmailChangeAsync(Student student, string previousEmail, CancellationToken cancellationToken)
    {
        var stamp = await accounts.RotateSecurityStampAsync(student.UserId, cancellationToken);
        await queue.CancelPendingAsync(EmailJobTypes.StudentAccess, student.Id, cancellationToken);
        if (await jobs.HasPendingGuardianNoticeAsync(student.Id, cancellationToken))
        {
            return;
        }

        // Uma chave por troca (o resumo do carimbo novo), como o aviso de senha alterada (§5.4).
        var key = $"{EmailJobTypes.GuardianEmailChanged}:{student.Id}:{stamp}";
        var request = new JobRequest(EmailJobTypes.GuardianEmailChanged, student.Id, new JobPayload(), NoticeExpiresIn, key);
        if (await queue.EnqueueAsync(request, cancellationToken) is not null)
        {
            student.KeepPreviousGuardianEmail(previousEmail);
        }
    }
}
