using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Capturador no lugar do envio, para os hosts de teste que precisam ver o e-mail sair. <see cref="Registrar"/> liga
/// também <c>App:PublicBaseUrl</c>: sem a base pública o tratador suspende o trabalho com <c>base_url_missing</c>.
/// </summary>
public sealed class EmailCapturado : IEmailSender
{
    public const string BaseUrl = "https://painel.example.org";

    private readonly List<EmailMessage> _sent = [];

    /// <summary>Resposta do provedor para a mensagem; sem ela, aceito, com o id entre <c>&lt; &gt;</c> como nos cabeçalhos.</summary>
    public Func<EmailMessage, EmailSendResult?>? Resposta { get; set; }

    public IReadOnlyList<EmailMessage> Enviados
    {
        get
        {
            lock (_sent) return [.. _sent];
        }
    }

    public static string IdDaMensagem(Guid? trabalho) => $"{trabalho:N}@relay.example.org";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_sent) _sent.Add(message);
        return Task.FromResult(Resposta?.Invoke(message) ?? new EmailSendResult(EmailSendStatus.Sent, $"<{IdDaMensagem(message.IdempotencyKey)}>"));
    }

    public void Registrar(IServiceCollection services) => services
        .AddSingleton<IEmailSender>(this)
        .PostConfigure<EmailOptions>(o => o.PublicBaseUrl = BaseUrl);

    /// <summary>Uma passada do processador real em cada raia, como o trabalhador faz, sem esperar o relógio. Devolve quantos trabalhos pegou.</summary>
    public static async Task<int> ProcessarFilaAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IReadOnlyList<JobLane> lanes;
        await using (var scope = services.CreateAsyncScope())
        {
            lanes = JobLane.From(scope.ServiceProvider.GetServices<IJobHandler>());
        }

        var processor = services.GetRequiredService<IJobProcessor>();
        var captured = 0;
        foreach (var lane in lanes)
        {
            captured += (await processor.ProcessBatchAsync(lane, CancellationToken.None, cancellationToken)).Captured;
        }

        return captured;
    }
}
