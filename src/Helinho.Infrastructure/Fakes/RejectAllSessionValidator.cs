using Helinho.Application.Abstractions;

namespace Helinho.Infrastructure.Fakes;

/// <summary>Padrão até a frente de identidade: rejeita todo token (a API responde 401 token_expired).</summary>
public sealed class RejectAllSessionValidator : IApiSessionValidator
{
    public Task<ValidatedSession?> ValidateAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult<ValidatedSession?>(null);
}
