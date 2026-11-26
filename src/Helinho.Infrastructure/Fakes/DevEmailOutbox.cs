using Helinho.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.Fakes;

/// <summary>Desligada por padrão. Não vem de configuração: só a área E-mail do Web liga, e só em <c>Development</c>.</summary>
public sealed class DevEmailOutboxOptions
{
    public bool Enabled { get; set; }
}

public sealed record DevEmail(EmailMessage Message, DateTimeOffset SentAt);

/// <summary>
/// Caixa de saída de desenvolvimento (ADR-0003 D8): as últimas mensagens que o <see cref="FakeEmailSender"/> recebeu,
/// só em memória. Some ao reiniciar a aplicação; desligada, não guarda nada.
/// </summary>
public sealed class DevEmailOutbox(IOptions<DevEmailOutboxOptions> options)
{
    public const int Capacity = 20;

    private readonly Queue<DevEmail> _items = new();
    private readonly Lock _gate = new();

    public void Add(EmailMessage message, DateTimeOffset sentAt)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            _items.Enqueue(new DevEmail(message, sentAt));
            while (_items.Count > Capacity)
            {
                _items.Dequeue();
            }
        }
    }

    /// <summary>Cópia das mensagens guardadas, da mais recente para a mais antiga.</summary>
    public IReadOnlyList<DevEmail> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items.Reverse()];
        }
    }
}
