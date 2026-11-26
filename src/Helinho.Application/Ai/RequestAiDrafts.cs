using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helinho.Application.Ai;

/// <summary>
/// <see cref="AlreadyRequested"/>: clique duplo, o pedido já existia e <see cref="RequestAiDraftsResult.JobId"/> leva a ele.
/// <see cref="Disabled"/>: IA desligada. <see cref="Forbidden"/>: quem chamou não é Master nem Admin autenticado.
/// </summary>
public enum RequestAiDraftsStatus
{
    Accepted,
    AlreadyRequested,
    Disabled,
    Forbidden,
    QuotaExceeded,
}

public sealed record RequestAiDraftsResult(RequestAiDraftsStatus Status, Guid? JobId = null, AiQuotaExceeded? Quota = null);

/// <summary>
/// Pede um lote de rascunhos (ADR-0004 §10.2 e §13): valida, confere a cota e grava o pedido e o trabalho da fila
/// numa gravação só. Nada é gerado aqui; quem chama o provedor é o tratador do trabalho.
/// </summary>
public sealed class RequestAiDrafts(IAppDbContext db, IJobQueue queue, IClock clock, ICurrentUser user, IOptions<AiOptions> options)
{
    public const string JobType = "ai.question-drafts";
    public const string PayloadJobId = "aiJobId";

    /// <summary>Depois disso a fila encerra o trabalho com <c>expired</c> e a leitura trata o pedido como vencido.</summary>
    public static readonly TimeSpan ExpiresIn = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromMinutes(2); // janela do clique duplo (ADR-0003 §5.4)

    /// <exception cref="DomainException">Tipo, faixa, quantidade ou assunto inválidos (mensagem em português, sem repetir o que foi digitado).</exception>
    public async Task<RequestAiDraftsResult> HandleAsync(
        QuizType quizType, AgeBand ageBand, int count, string? subject, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsEnabled)
        {
            return new RequestAiDraftsResult(RequestAiDraftsStatus.Disabled);
        }

        if (AiAccess.Reviewer(user) is not { } userId)
        {
            return new RequestAiDraftsResult(RequestAiDraftsStatus.Forbidden);
        }

        // A fábrica da porta valida e saneia: o assunto guardado é o mesmo que irá ao gerador.
        var cleanSubject = QuestionDraftRequest.Create(quizType, ageBand, count, subject, []).Subject;
        var now = clock.UtcNow.ToUniversalTime();
        var window = now.Ticks / IdempotencyWindow.Ticks;
        var windowStart = new DateTimeOffset(window * IdempotencyWindow.Ticks, TimeSpan.Zero);
        async Task<RequestAiDraftsResult?> SameRequestAsync() => await db.AiGenerationJobs.AsNoTracking()
            .Where(j => j.Kind == AiJobKind.Batch && j.RequestedByUserId == userId && j.QuizType == quizType && j.AgeBand == ageBand
                && j.RequestedCount == count && j.Subject == cleanSubject && j.CreatedAt >= windowStart)
            .OrderBy(j => j.CreatedAt).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(cancellationToken) is { } id
            ? new RequestAiDraftsResult(RequestAiDraftsStatus.AlreadyRequested, id) : null;

        // Antes da cota: o clique duplo leva ao pedido já criado em vez de esbarrar no saldo que ele mesmo gastou.
        if (await SameRequestAsync() is { } existing)
        {
            return existing;
        }

        if (await AiQuota.CheckAsync(db, settings, userId, count, now, cancellationToken) is { } exceeded)
        {
            return new RequestAiDraftsResult(RequestAiDraftsStatus.QuotaExceeded, Quota: exceeded);
        }

        var job = new AiGenerationJob(AiJobKind.Batch, quizType, ageBand, count, cleanSubject, userId, now);
        // O assunto é texto livre: fica no pedido. A fila leva só o id dele, e a chave leva só um resumo.
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{JobType}:{userId:N}:{quizType}-{ageBand}-{count}-{Digest(cleanSubject)}:{window}");
        var request = new JobRequest(JobType, userId, new JobPayload().WithId(PayloadJobId, job.Id), ExpiresIn, key);
        if (await queue.EnqueueAsync(request, cancellationToken) is null)
        {
            // Outro clique gravou depois da conferência acima: a chave já foi usada e nada foi acrescentado ao contexto.
            return await SameRequestAsync() ?? throw new InvalidOperationException("Chave de pedido já usada na fila, sem o pedido.");
        }

        db.AiGenerationJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (!cancellationToken.IsCancellationRequested)
        {
            // Dois cliques ao mesmo tempo: o índice único da chave recusou esta gravação inteira (pedido e trabalho).
            if (await SameRequestAsync() is { } winner)
            {
                return winner;
            }

            throw;
        }

        return new RequestAiDraftsResult(RequestAiDraftsStatus.Accepted, job.Id);
    }

    private static string Digest(string? subject) =>
        subject is null ? "0" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(subject)))[..16];
}
