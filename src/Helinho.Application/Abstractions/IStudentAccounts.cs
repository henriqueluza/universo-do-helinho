namespace Helinho.Application.Abstractions;

/// <summary>Cria a conta (usuário do Identity, papel Student) do aluno cadastrado, sem senha: credenciais ficam fora desta etapa.</summary>
public interface IStudentAccounts
{
    /// <summary>Adiciona a conta ao contexto sem salvar; quem chama persiste junto com o aluno. Devolve o id do usuário.</summary>
    Task<Guid> StageAsync(Guid unitId, string enrollment, CancellationToken cancellationToken);

    /// <summary>A conta já tem senha? Decide o que o acesso envia: primeiro acesso (72 h) ou troca de senha (1 h) (ADR-0003 §11.4).</summary>
    Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken);
}
