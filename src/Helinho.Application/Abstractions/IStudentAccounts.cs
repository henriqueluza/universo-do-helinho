namespace Helinho.Application.Abstractions;

/// <summary>Cria a conta (usuário do Identity, papel Student) do aluno cadastrado, sem senha: credenciais ficam fora desta etapa.</summary>
public interface IStudentAccounts
{
    /// <summary>Adiciona a conta ao contexto sem salvar; quem chama persiste junto com o aluno. Devolve o id do usuário.</summary>
    Task<Guid> StageAsync(Guid unitId, string enrollment, CancellationToken cancellationToken);

    /// <summary>A conta já tem senha? Decide o que o acesso envia: primeiro acesso (72 h) ou troca de senha (1 h) (ADR-0003 §11.4).</summary>
    Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Troca o carimbo de segurança da conta <b>sem salvar</b>: links de acesso já emitidos deixam de valer (ADR-0003 §10.2 e §11.4).
    /// Devolve um resumo do carimbo novo, para a chave de idempotência do aviso (§5.4); o carimbo não sai daqui.
    /// </summary>
    Task<string> RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken);
}
