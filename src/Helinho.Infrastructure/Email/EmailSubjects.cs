using Helinho.Application.Identidade;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// Leitura do tratador de e-mail sobre o Identity e o banco. As consultas não rastreiam e trazem só as colunas que o
/// e-mail usa; a única mudança que entra na gravação final do trabalho (ADR-0003 §6.6) é a do <paramref name="cleaner"/>.
/// </summary>
public sealed class EmailSubjects(AppDbContext db, UserManager<AppUser> users, PreviousGuardianEmailCleaner cleaner) : IEmailSubjects
{
    public async Task<EmailSubject?> FindByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await SubjectAsync(userId, await StudentAsync(Students().Where(s => s.UserId == userId), cancellationToken), cancellationToken);

    public async Task<EmailSubject?> FindByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        await StudentAsync(Students().Where(s => s.Id == studentId), cancellationToken) is { } student
            ? await SubjectAsync(student.UserId, student, cancellationToken)
            : null;

    public async Task<string> GenerateLinkTokenAsync(Guid userId, string provider, CancellationToken cancellationToken)
    {
        // A conta inteira, por causa do carimbo de segurança que o token carrega (§10.1). O UserManager só a lê.
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
        return await users.GenerateUserTokenAsync(user, provider, LinkDeAcesso.Finalidade);
    }

    public Task StagePreviousGuardianEmailRemovalAsync(Guid studentId, CancellationToken cancellationToken) =>
        cleaner.StageAsync(studentId, cancellationToken);

    // ignora-filtro: o trabalhador da fila não tem usuário de painel; a busca é só pelo id que veio no trabalho (SubjectId).
    private IQueryable<Domain.Entities.Student> Students() => db.Students.IgnoreQueryFilters().AsNoTracking();

    private static Task<StudentRow?> StudentAsync(IQueryable<Domain.Entities.Student> query, CancellationToken cancellationToken) =>
        query.Select(s => new StudentRow(s.UserId, s.FullName, s.Enrollment, s.GuardianEmail, s.PreviousGuardianEmail, s.GuardianApproved))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<EmailSubject?> SubjectAsync(Guid userId, StudentRow? student, CancellationToken cancellationToken)
    {
        var account = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.UserName, u.Email, u.UnitId, HasPassword = u.PasswordHash != null })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return null;
        }

        var isPanelAccount = await db.UserRoles.Where(r => r.UserId == userId)
            .Join(db.Roles, r => r.RoleId, role => role.Id, (_, role) => role.Name)
            .AnyAsync(name => name == RoleNames.Master || name == RoleNames.Admin, cancellationToken);
        var unitName = await db.Units.AsNoTracking().Where(u => u.Id == account.UnitId).Select(u => u.Name).SingleOrDefaultAsync(cancellationToken);

        // §11.4: do cadastro do aluno o e-mail leva só o primeiro nome e a matrícula.
        var firstName = student?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return new EmailSubject(userId, account.UserName ?? "", account.Email, unitName, account.HasPassword, isPanelAccount,
            student is null ? null : new EmailStudent(firstName, student.Enrollment, student.GuardianEmail, student.PreviousGuardianEmail, student.Approved));
    }

    private sealed record StudentRow(Guid UserId, string FullName, string Enrollment, string? GuardianEmail, string? PreviousGuardianEmail, bool Approved);
}
