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
}

/// <summary>Raia de um registro de tratador: os tipos que ele atende e a política dele. Cada raia tem captura e laço próprios.</summary>
public sealed record JobLane(IReadOnlyCollection<string> Types, JobPolicy Policy);

/// <summary><paramref name="PausedUntil"/> vem preenchido quando um tratador devolveu suspender (§6.3): o laço da raia espera até lá.</summary>
public sealed record JobBatchResult(int Captured, DateTimeOffset? PausedUntil);
