using Helinho.Application.Abstractions;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public sealed record StudentListQuery(Guid? UnitId, string? ClassName, AgeBand? AgeBand, int Page);

/// <summary>Linha da listagem. Não carrega CPF, RG nem endereço: dado sensível não sai na tabela.</summary>
public sealed record StudentRow(
    Guid Id, string FullName, string Enrollment, string UnitName, string ClassName, DateOnly BirthDate, bool GuardianApproved);

public sealed record StudentListResult(
    IReadOnlyList<StudentRow> Rows,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<string> Classes,
    IReadOnlyList<UnitOption> Units)
{
    public int TotalPages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>
/// Listagem de alunos do painel. O escopo de unidade vem do filtro global de <c>Student</c> (admin só vê a própria);
/// o filtro por unidade só vale para o master — para o admin o parâmetro é ignorado.
/// </summary>
public static class ListStudents
{
    public const int PageSize = 20;

    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static async Task<StudentListResult> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, StudentListQuery query, CancellationToken cancellationToken)
    {
        var isMaster = user.IsInRole(RoleNames.Master);
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(BrasiliaOffset).DateTime);

        var scoped = db.Students.AsQueryable();
        var students = scoped;
        if (isMaster && query.UnitId is { } unit)
        {
            students = students.Where(s => s.UnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.ClassName))
        {
            var className = query.ClassName.Trim();
            students = students.Where(s => s.ClassName == className);
        }

        if (query.AgeBand is { } band)
        {
            var (youngest, oldest) = GetDashboard.BirthDateRange(band, today);
            students = students.Where(s => s.BirthDate <= youngest && s.BirthDate > oldest);
        }

        var total = await students.CountAsync(cancellationToken);
        var lastPage = Math.Max(1, (total + PageSize - 1) / PageSize);
        var page = Math.Clamp(query.Page, 1, lastPage);

        var rows = await (from s in students
                          join u in db.Units on s.UnitId equals u.Id
                          orderby s.FullName, s.Id
                          select new StudentRow(s.Id, s.FullName, s.Enrollment, u.Name, s.ClassName, s.BirthDate, s.GuardianApproved))
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);
        var classes = await scoped.Select(s => s.ClassName).Distinct().OrderBy(c => c).ToListAsync(cancellationToken);
        var units = isMaster
            ? await db.Units.OrderBy(u => u.Name).Select(u => new UnitOption(u.Id, u.Name)).ToListAsync(cancellationToken)
            : [];

        return new StudentListResult(rows, total, page, PageSize, classes, units);
    }
}
