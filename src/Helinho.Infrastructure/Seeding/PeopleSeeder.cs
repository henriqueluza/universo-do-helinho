using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Helinho.Infrastructure.Seeding;

/// <summary>
/// Seed sintético de pessoas: 2 unidades, 1 master, 2 administradores e 10 alunos. Idempotente.
/// Tudo fictício: nomes inventados, CPF/RG marcados como fictícios, e-mails em helinho.example.
/// A senha vem de HELINHO_SEED_PASSWORD; nunca é escrita em código ou log.
/// </summary>
public sealed class PeopleSeeder(AppDbContext db, IClock clock, IConfiguration configuration) : ISeeder
{
    public const string PasswordVariable = "HELINHO_SEED_PASSWORD";
    public const string NorthUnitName = "Unidade Norte (fictícia)";
    public const string SouthUnitName = "Unidade Sul (fictícia)";

    // Idades por aluno (01..10): cobrem as 4 faixas etárias.
    private static readonly int[] Ages = [6, 7, 8, 9, 10, 11, 12, 13, 14, 9];

    // Alunos 03 e 08 ficam com a aprovação do responsável pendente.
    private static readonly HashSet<int> PendingGuardian = [3, 8];

    private static readonly PasswordHasher<AppUser> Hasher = new();

    public int Order => 10;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var password = configuration[PasswordVariable];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Defina a variável de ambiente {PasswordVariable} para executar o seed (a senha não fica no código).");
        }

        var north = await EnsureUnitAsync(NorthUnitName, cancellationToken);
        var south = await EnsureUnitAsync(SouthUnitName, cancellationToken);

        foreach (var role in new[] { RoleNames.Master, RoleNames.Admin, RoleNames.Student })
        {
            await EnsureRoleAsync(role, cancellationToken);
        }

        await EnsureUserAsync("master", "master@helinho.example", null, RoleNames.Master, password, cancellationToken);
        await EnsureUserAsync("admin.norte", "admin.norte@helinho.example", north.Id, RoleNames.Admin, password, cancellationToken);
        await EnsureUserAsync("admin.sul", "admin.sul@helinho.example", south.Id, RoleNames.Admin, password, cancellationToken);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        for (var n = 1; n <= Ages.Length; n++)
        {
            var unit = n % 2 == 1 ? north : south;
            await EnsureStudentAsync(n, unit, today, password, cancellationToken);
        }
    }

    private async Task<Unit> EnsureUnitAsync(string name, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Name == name, ct);
        if (unit is null)
        {
            unit = new Unit(name);
            db.Units.Add(unit);
            await db.SaveChangesAsync(ct);
        }

        return unit;
    }

    private async Task EnsureRoleAsync(string role, CancellationToken ct)
    {
        if (!await db.Roles.AnyAsync(r => r.Name == role, ct))
        {
            db.Roles.Add(new IdentityRole<Guid>(role) { Id = Guid.NewGuid(), NormalizedName = role.ToUpperInvariant() });
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<AppUser> EnsureUserAsync(
        string userName, string email, Guid? unitId, string role, string password, CancellationToken ct)
    {
        var normalizedEmail = email.ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (user is not null)
        {
            return user;
        }

        user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            UnitId = unitId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);

        var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync(ct);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task EnsureStudentAsync(int n, Unit unit, DateOnly today, string password, CancellationToken ct)
    {
        var enrollment = $"{2026000 + n}";
        // ignora-filtro: o seed roda sem usuário/unidade de contexto e precisa enxergar alunos de todas as unidades para ser idempotente.
        if (await db.Students.IgnoreQueryFilters().AnyAsync(s => s.Enrollment == enrollment, ct))
        {
            return;
        }

        var nn = n.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        var user = await EnsureUserAsync($"aluno{nn}", $"aluno{nn}@helinho.example", unit.Id, RoleNames.Student, password, ct);

        // 3 meses além do aniversário: a idade e a faixa não mudam por dias de diferença no relógio.
        var birthDate = today.AddYears(-Ages[n - 1]).AddMonths(-3);
        var student = new Student(
            user.Id, unit.Id, enrollment, $"Aluno Fictício {nn}", $"FICT-RG-{nn}", $"000.000.0{nn}-00",
            $"Rua Inexistente {n}, Bairro Fictício", birthDate, $"Responsável Fictício {nn}",
            $"Turma {(char)('A' + (n - 1) % 4)}", today);

        if (!PendingGuardian.Contains(n))
        {
            student.ApproveByGuardian();
        }

        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
    }
}
