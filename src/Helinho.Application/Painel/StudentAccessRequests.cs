using Helinho.Application.Abstractions;
using Helinho.Application.Email;

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
}
