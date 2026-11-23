using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Microsoft.Extensions.Logging;

namespace Helinho.Application.Identidade;

/// <summary>Conta do painel que pode pedir redefinição. O carimbo de segurança não sai da infraestrutura: só o resumo dele (ADR-0003 §5.4).</summary>
public sealed record ContaParaRedefinicao(Guid UserId, string StampDigest);

/// <summary>Porta do pedido de redefinição: a consulta da conta e a contagem do limite por conta na fila.</summary>
public interface IPasswordResetAccounts
{
    /// <summary>A conta <c>Master</c> ou <c>Admin</c> com o e-mail, só se houver exatamente uma. E-mail repetido (#184) devolve nulo, nunca exceção.</summary>
    Task<ContaParaRedefinicao?> FindSingleAsync(string email, CancellationToken cancellationToken);

    /// <summary>Redefinições com link vivo (§11.1): pendentes, em processamento ou enviadas, criadas desde <paramref name="since"/>, com a chave começando por <paramref name="keyPrefix"/>.</summary>
    Task<int> CountLiveAsync(Guid userId, string keyPrefix, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Grava o que foi enfileirado. <c>false</c> quando outro pedido da mesma janela gravou a chave primeiro.</summary>
    Task<bool> TrySaveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Caso de uso "pedir redefinição de senha" (ADR-0003 §11.1). Não devolve nada: quem chama não fica sabendo se a conta
/// existe, se o limite estourou ou se o pedido repetiu. Uma consulta e, no máximo, uma inserção; o resto é do tratador de e-mail.
/// </summary>
public sealed partial class PedirRedefinicaoHandler(
    IPasswordResetAccounts accounts, IJobQueue queue, IClock clock, TimeProvider time, ILogger<PedirRedefinicaoHandler> logger)
{
    public const int LimitePorConta = 3;

    private const int SegundosDaJanela = 120; // §5.4: clique duplo e reenvio do formulário caem na mesma chave

    /// <summary>A resposta só sai depois deste tempo, contado do início: esconde a inserção a mais de quando a conta existe.</summary>
    public static TimeSpan Piso { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>§6.2: depois disso o trabalho vira <c>Failed</c> e deixa de contar no limite.</summary>
    public static TimeSpan ValidadeDoTrabalho { get; } = TimeSpan.FromMinutes(30);

    public async Task HandleAsync(string email, CancellationToken cancellationToken)
    {
        var inicio = time.GetTimestamp();
        await EnfileirarSeAptaAsync(email, cancellationToken);
        var falta = Piso - time.GetElapsedTime(inicio);
        if (falta > TimeSpan.Zero)
        {
            await Task.Delay(falta, time, cancellationToken);
        }
    }

    private async Task EnfileirarSeAptaAsync(string email, CancellationToken cancellationToken)
    {
        if (await accounts.FindSingleAsync(email, cancellationToken) is not { } conta)
        {
            return;
        }

        var agora = clock.UtcNow;
        var prefixo = $"{EmailJobTypes.PasswordReset}:{conta.UserId:D}:{conta.StampDigest}:";
        // A janela do limite é a validade do link: todo pedido que conta ainda tem um link que funciona ou a caminho (T20).
        var vivos = await accounts.CountLiveAsync(conta.UserId, prefixo, agora - LinkDeAcesso.ValidadeRedefinicao, cancellationToken);
        if (vivos >= LimitePorConta)
        {
            LogLimite(logger, conta.UserId);
            return;
        }

        var pedido = new JobRequest(
            EmailJobTypes.PasswordReset, conta.UserId, new JobPayload(), ValidadeDoTrabalho,
            $"{prefixo}{agora.ToUnixTimeSeconds() / SegundosDaJanela}");
        if (await queue.EnqueueAsync(pedido, cancellationToken) is not null && !await accounts.TrySaveAsync(cancellationToken))
        {
            LogRepetido(logger, conta.UserId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pedido de redefinição descartado pelo limite por conta: {UserId}.")]
    private static partial void LogLimite(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pedido de redefinição repetido na mesma janela para a conta {UserId}.")]
    private static partial void LogRepetido(ILogger logger, Guid userId);
}
