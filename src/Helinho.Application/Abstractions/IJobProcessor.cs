namespace Helinho.Application.Abstractions;

/// <summary>
/// Processa a fila persistente (ADR-0003 §6). Quem chama é o laço do trabalhador, um por raia; nos testes de
/// integração o serviço hospedado fica desligado e o teste chama esta porta direto.
/// </summary>
public interface IJobProcessor
{
    /// <summary>
    /// Uma passada: pega um lote da raia (§6.1) e processa item a item. <paramref name="stopping"/> é o sinal de
    /// parada: nenhum item novo começa, e o que sobrou do lote reaparece pelo prazo de posse (§6.4).
    /// <paramref name="abort"/> é o fim da janela de parada do host: cancela o item em curso.
    /// </summary>
    Task<JobBatchResult> ProcessBatchAsync(JobLane lane, CancellationToken stopping, CancellationToken abort);

    /// <summary>
    /// Limpeza (§5.6): apaga, em lotes, as linhas em estado final com <c>FinishedAt</c> mais antigo que a retenção.
    /// <c>Pending</c> e <c>Processing</c> nunca são apagados. Devolve quantas linhas saíram.
    /// </summary>
    Task<int> PurgeFinishedAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken);
}

/// <summary>Raia de um registro de tratador: os tipos que ele atende e a política dele. Cada raia tem captura e laço próprios.</summary>
public sealed record JobLane(IReadOnlyCollection<string> Types, JobPolicy Policy)
{
    /// <summary>
    /// Uma raia por registro. Recusa, na subida, a política em que a posse não cobre o lote inteiro
    /// (<c>BatchSize × ItemTimeout ≤ Lease</c>, §6.1) e o tipo atendido por dois registros, que deixaria o roteamento ambíguo.
    /// </summary>
    public static IReadOnlyList<JobLane> From(IEnumerable<IJobHandler> handlers)
    {
        JobLane[] lanes = [.. handlers.Select(h => new JobLane([.. h.Types], h.Policy))];
        foreach (var (types, policy) in lanes)
        {
            if (types.Count == 0 || policy.BatchSize < 1 || policy.MaxAttempts < 1 || policy.ItemTimeout <= TimeSpan.Zero
                || policy.ItemTimeout * policy.BatchSize > policy.Lease)
            {
                throw new InvalidOperationException(
                    $"Fila: política inválida no tratador de [{string.Join(", ", types)}]: lote × tempo máximo por item precisa caber no prazo de posse.");
            }
        }

        var repeated = lanes.SelectMany(l => l.Types).GroupBy(t => t, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        return repeated.Count == 0
            ? lanes
            : throw new InvalidOperationException($"Fila: mais de um tratador registrado para [{string.Join(", ", repeated)}].");
    }
}

/// <summary><paramref name="PausedUntil"/> vem preenchido quando um tratador devolveu suspender (§6.3): o laço da raia espera até lá.</summary>
public sealed record JobBatchResult(int Captured, DateTimeOffset? PausedUntil);
