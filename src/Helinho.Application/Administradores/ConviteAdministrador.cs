using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;

namespace Helinho.Application.Administradores;

/// <summary>Situação mostrada na lista de administradores (ADR-0003 §11.2), derivada da conta e do convite mais recente.</summary>
public enum AdminSituacao
{
    Ativo,
    Inativo,
    AguardandoEnvio,
    ConviteEnviado,
    ConviteExpirado,
    NaoEntregue,
}

/// <summary>
/// Convite mais recente da conta na fila: se falhou e quando foi enviado (nulo enquanto espera o envio).
/// O retorno do provedor (<c>Feedback</c>, Issue do webhook) entra aqui.
/// </summary>
public sealed record InviteJob(bool Failed, DateTimeOffset? SentAt);

/// <summary>Leitura dos convites na fila (<c>QueueJobs</c> por tipo e conta). Não altera nada; quem enfileira é <see cref="IJobQueue"/>.</summary>
public interface IAdminInviteJobs
{
    /// <summary>O convite mais recente de cada conta, sem contar os cancelados. Conta sem registro fica de fora.</summary>
    Task<IReadOnlyDictionary<Guid, InviteJob>> LatestAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    /// <summary>Convites da conta criados desde o instante dado, em qualquer estado (limite por hora, §11.6).</summary>
    Task<int> CountSinceAsync(Guid accountId, DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>Convite de administrador (ADR-0003 §11.2): o trabalho que vai para a fila e a situação mostrada na lista.</summary>
public static class ConviteAdministrador
{
    /// <summary>Depois disso o trabalhador não envia mais (§6.2). A validade do link é outra e conta do envio.</summary>
    public static TimeSpan ValidadeNaFila { get; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Só referências (§5.3): o tipo e o id da conta. Destinatário, token e corpo são do tratador, na hora de enviar.
    /// A chave junta pedidos repetidos na mesma janela de 2 minutos (§5.4).
    /// </summary>
    public static JobRequest Trabalho(Guid accountId, DateTimeOffset now) => new(
        EmailJobTypes.AdminInvite, accountId, new JobPayload(), ValidadeNaFila,
        $"{EmailJobTypes.AdminInvite}:{accountId:D}:{now.ToUnixTimeSeconds() / 120}");

    public static AdminSituacao Situacao(AdminAccount account, InviteJob? invite, DateTimeOffset now) =>
        !CriarAdministradorHandler.IsActive(account, now) ? AdminSituacao.Inativo
        : account.HasPassword ? AdminSituacao.Ativo
        : invite switch
        {
            { Failed: true } => AdminSituacao.NaoEntregue,
            { SentAt: null } => AdminSituacao.AguardandoEnvio,
            { SentAt: { } sentAt } when now - sentAt < LinkDeAcesso.ValidadeConvite => AdminSituacao.ConviteEnviado,
            _ => AdminSituacao.ConviteExpirado, // enviado há mais de 72 horas, ou nenhum registro na fila
        };
}
