using System.Net.Http.Headers;
using Helinho.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Helinho.IntegrationTests;

/// <summary>Validador de teste: o token `test-student-{guid}` autentica o aluno daquele guid.</summary>
public sealed class TestSessionValidator : IApiSessionValidator
{
    public const string Prefix = "test-student-";

    public Task<ValidatedSession?> ValidateAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult(
            token.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParse(token[Prefix.Length..], out var id)
                ? new ValidatedSession(id)
                : null);
}

public static class ApiTestClient
{
    /// <summary>Fábrica cujo validador de sessão aceita os tokens de <see cref="CreateStudentClient"/>.</summary>
    public static WebApplicationFactory<Program> WithTestSessions(this HelinhoWebFactory factory) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.Replace(ServiceDescriptor.Singleton<IApiSessionValidator, TestSessionValidator>())));

    /// <summary>Cliente HTTP autenticado como o aluno informado, para as frentes da API.</summary>
    public static HttpClient CreateStudentClient(this WebApplicationFactory<Program> factory, Guid studentId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestSessionValidator.Prefix + studentId);
        return client;
    }
}
