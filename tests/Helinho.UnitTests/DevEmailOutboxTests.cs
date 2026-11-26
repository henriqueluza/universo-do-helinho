using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Fakes;
using Microsoft.Extensions.Options;

namespace Helinho.UnitTests;

public class DevEmailOutboxTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static DevEmailOutbox Caixa(bool enabled) => new(Options.Create(new DevEmailOutboxOptions { Enabled = enabled }));

    private static EmailMessage Mensagem(int n) => new($"pessoa{n}@example.com", $"Assunto {n}", $"<p>{n}</p>", $"{n}");

    [Fact]
    public void Desligada_por_padrao_nao_guarda_nada()
    {
        var caixa = new DevEmailOutbox(Options.Create(new DevEmailOutboxOptions()));

        caixa.Add(Mensagem(1), Inicio);

        Assert.Empty(caixa.Snapshot());
    }

    [Fact]
    public void Ligada_guarda_so_as_ultimas_mensagens_da_mais_recente_para_a_mais_antiga()
    {
        var caixa = Caixa(enabled: true);

        for (var n = 1; n <= DevEmailOutbox.Capacity + 5; n++)
        {
            caixa.Add(Mensagem(n), Inicio.AddMinutes(n));
        }

        var guardadas = caixa.Snapshot();
        Assert.Equal(DevEmailOutbox.Capacity, guardadas.Count);
        Assert.Equal($"Assunto {DevEmailOutbox.Capacity + 5}", guardadas[0].Message.Subject);
        Assert.Equal(Inicio.AddMinutes(DevEmailOutbox.Capacity + 5), guardadas[0].SentAt);
        Assert.Equal("Assunto 6", guardadas[^1].Message.Subject);
    }

    [Fact]
    public void A_copia_devolvida_nao_muda_quando_chega_outra_mensagem()
    {
        var caixa = Caixa(enabled: true);
        caixa.Add(Mensagem(1), Inicio);

        var antes = caixa.Snapshot();
        caixa.Add(Mensagem(2), Inicio.AddMinutes(1));

        Assert.Single(antes);
        Assert.Equal(2, caixa.Snapshot().Count);
    }
}
