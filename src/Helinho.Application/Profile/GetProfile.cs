using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Profile;

/// <summary>Caso de uso do GET /profile: perfil de jogo do aluno autenticado, sem dados cadastrais.</summary>
public static class GetProfile
{
    public static async Task<ProfileResponse?> HandleAsync(
        IAppDbContext db, ICurrentStudent current, CancellationToken cancellationToken)
    {
        var studentId = current.StudentId;

        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        var student = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.FullName, s.Avatar, s.Xp })
            .SingleOrDefaultAsync(cancellationToken);
        if (student is null)
        {
            return null;
        }

        var level = LevelPolicy.LevelFor(student.Xp);
        return new ProfileResponse(
            student.FullName, student.Avatar, level, LevelPolicy.LevelName(level), student.Xp,
            LevelPolicy.NextLevelXp(student.Xp));
    }
}
