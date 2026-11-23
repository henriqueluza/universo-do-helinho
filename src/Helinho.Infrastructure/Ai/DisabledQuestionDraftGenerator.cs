using Helinho.Application.Ai;

namespace Helinho.Infrastructure.Ai;

/// <summary>
/// O que a porta resolve com a IA desligada (ADR-0004 §14): recusa toda chamada, sem montar requisição nem tocar a
/// rede. Um caso de uso que esqueça de checar <see cref="AiOptions.IsEnabled"/> falha fechado, em vez de gerar.
/// </summary>
public sealed class DisabledQuestionDraftGenerator : IQuestionDraftGenerator
{
    public Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken) =>
        Task.FromException<QuestionDraftBatch>(new QuestionDraftGenerationException(QuestionDraftFailure.Disabled));
}
