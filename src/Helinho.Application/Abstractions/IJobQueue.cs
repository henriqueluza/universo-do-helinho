using System.Text.RegularExpressions;

namespace Helinho.Application.Abstractions;

#pragma warning disable CA1711 // nome do ADR-0003 §5.5: é a porta da fila de trabalhos, não uma coleção
/// <summary>Fila persistente genérica (ADR-0003 §5). Guarda só referências: nunca destinatário, corpo, link, token ou texto livre.</summary>
public interface IJobQueue
{
    /// <summary>
    /// Acrescenta o trabalho ao contexto <b>sem salvar</b> (§5.5): quem chama grava junto com a regra de negócio.
    /// Devolve o id do trabalho, ou <c>null</c> quando a chave de idempotência já foi usada (§5.4).
    /// Em corrida entre dois pedidos, o índice único recusa a gravação do segundo.
    /// </summary>
    Task<Guid?> EnqueueAsync(JobRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Marca como cancelados, <b>sem salvar</b>, os trabalhos ainda pendentes do tipo e do sujeito; devolve quantos.
    /// O cancelado libera a chave de idempotência, para o reenvio na mesma janela ser aceito.
    /// </summary>
    Task<int> CancelPendingAsync(string type, Guid subjectId, CancellationToken cancellationToken);
}
#pragma warning restore CA1711

/// <summary>Pedido de enfileiramento. <paramref name="ExpiresIn"/> conta a partir da criação (§6.2).</summary>
public sealed partial record JobRequest(string Type, Guid? SubjectId, JobPayload Payload, TimeSpan ExpiresIn, string? IdempotencyKey = null)
{
    /// <summary>Tipo em minúsculas com ponto, como <c>email.admin-invite</c>; até 60 caracteres.</summary>
    public string Type { get; } = Type is { Length: <= 60 } && TypePattern().IsMatch(Type)
        ? Type : throw new ArgumentException("Tipo de trabalho inválido.", nameof(Type));

    public TimeSpan ExpiresIn { get; } = ExpiresIn > TimeSpan.Zero
        ? ExpiresIn : throw new ArgumentOutOfRangeException(nameof(ExpiresIn), "A validade precisa ser positiva.");

    /// <summary><c>&lt;tipo&gt;:&lt;id&gt;:&lt;janela ou resumo&gt;</c>: sem espaço nem arroba, para não carregar e-mail ou nome.</summary>
    public string? IdempotencyKey { get; } = IdempotencyKey is null || (IdempotencyKey.Length <= 200 && KeyPattern().IsMatch(IdempotencyKey))
        ? IdempotencyKey : throw new ArgumentException("Chave de idempotência inválida.", nameof(IdempotencyKey));

    [GeneratedRegex(@"^[a-z0-9]+(\.[a-z0-9-]+)+\z")]
    private static partial Regex TypePattern();

    [GeneratedRegex(@"^[A-Za-z0-9:._-]+\z")]
    private static partial Regex KeyPattern();
}
