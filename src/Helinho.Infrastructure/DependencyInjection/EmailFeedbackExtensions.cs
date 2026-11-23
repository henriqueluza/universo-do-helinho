using Helinho.Application.Email;
using Helinho.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class EmailFeedbackExtensions
{
    /// <summary>Porta que grava o retorno de entrega do provedor no trabalho da fila (ADR-0003 §12).</summary>
    public static IServiceCollection AddEmailFeedback(this IServiceCollection services) =>
        services.AddScoped<IEmailFeedbackRecorder, EmailFeedbackRecorder>();
}
