using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class StudentAccessExtensions
{
    /// <summary>Acesso do aluno enviado ao responsável (ADR-0003 §11.4): leitura da fila e os pedidos de acesso.</summary>
    public static IServiceCollection AddStudentAccess(this IServiceCollection services)
    {
        services.AddScoped<IStudentAccessJobs, StudentAccessJobs>();
        services.AddScoped<StudentAccessRequests>();
        services.AddScoped<PreviousGuardianEmailCleaner>();
        return services;
    }
}
