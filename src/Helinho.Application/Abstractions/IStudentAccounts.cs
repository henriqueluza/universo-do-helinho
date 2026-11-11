namespace Helinho.Application.Abstractions;

/// <summary>Cria a conta (usuário do Identity, papel Student) do aluno cadastrado, sem senha: credenciais ficam fora desta etapa.</summary>
public interface IStudentAccounts
{
    /// <summary>Adiciona a conta ao contexto sem salvar; quem chama persiste junto com o aluno. Devolve o id do usuário.</summary>
    Task<Guid> StageAsync(Guid unitId, string enrollment, CancellationToken cancellationToken);
}
