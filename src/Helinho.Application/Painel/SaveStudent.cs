using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

/// <summary>Modelo do formulário de aluno. Não é a entidade: unidade, matrícula e usuário nunca vêm do formulário.</summary>
public sealed record StudentForm(
    string? FullName, string? Rg, string? Cpf, string? Address, DateOnly? BirthDate,
    string? GuardianName, string? ClassName, bool GuardianApproved);

/// <summary>Resultado do salvamento. <see cref="Errors"/> mapeia campo → mensagem fixa (nunca ecoa CPF ou RG).</summary>
public sealed record SaveStudentResult(Guid? StudentId, IReadOnlyDictionary<string, string> Errors)
{
    public bool Succeeded => StudentId is not null;
}

/// <summary>Cadastro e edição de aluno pelo administrador, sempre na unidade dele (filtro global de <c>Student</c>).</summary>
public static class SaveStudent
{
    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    /// <summary>Formulário preenchido com o aluno; <c>null</c> se não existe na unidade do usuário (a tela responde 404).</summary>
    public static async Task<StudentForm?> LoadAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.Students.Where(s => s.Id == id)
            .Select(s => new StudentForm(s.FullName, s.Rg, s.Cpf, s.Address, s.BirthDate, s.GuardianName, s.ClassName, s.GuardianApproved))
            .SingleOrDefaultAsync(cancellationToken);

    public static async Task<string?> UnitNameAsync(IAppDbContext db, ICurrentUser user, CancellationToken cancellationToken) =>
        user.UnitId is { } unitId
            ? await db.Units.Where(u => u.Id == unitId).Select(u => u.Name).SingleOrDefaultAsync(cancellationToken)
            : null;

    public static async Task<SaveStudentResult> CreateAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, IStudentAccounts accounts, StudentForm form, CancellationToken cancellationToken)
    {
        var errors = Validate(form, Today(clock));
        if (user.UnitId is not { } unitId || !user.IsInRole(RoleNames.Admin))
        {
            errors["Unidade"] = "Seu usuário não está vinculado a uma unidade.";
            return new SaveStudentResult(null, errors);
        }

        if (errors.Count > 0)
        {
            return new SaveStudentResult(null, errors);
        }

        // Matrícula gerada pelo sistema; o índice único protege contra colisão.
        var enrollment = $"ALU-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        var userId = await accounts.StageAsync(unitId, enrollment, cancellationToken);
        var student = new Student(
            userId, unitId, enrollment, form.FullName!, form.Rg!, Cpf.Format(form.Cpf), form.Address!,
            form.BirthDate!.Value, form.GuardianName!, form.ClassName!, Today(clock));
        student.SetGuardianApproval(form.GuardianApproved);
        db.Students.Add(student);
        await db.SaveChangesAsync(cancellationToken);
        return new SaveStudentResult(student.Id, errors);
    }

    public static async Task<SaveStudentResult?> UpdateAsync(
        IAppDbContext db, IClock clock, Guid id, StudentForm form, CancellationToken cancellationToken)
    {
        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (student is null)
        {
            return null;
        }

        var errors = Validate(form, Today(clock));
        if (errors.Count > 0)
        {
            return new SaveStudentResult(null, errors);
        }

        student.UpdateRegistration(
            form.FullName!, form.Rg!, Cpf.Format(form.Cpf), form.Address!, form.BirthDate!.Value, form.GuardianName!, form.ClassName!, Today(clock));
        student.SetGuardianApproval(form.GuardianApproved);
        await db.SaveChangesAsync(cancellationToken);
        return new SaveStudentResult(student.Id, errors);
    }

    internal static Dictionary<string, string> Validate(StudentForm form, DateOnly today)
    {
        var errors = new Dictionary<string, string>();
        Require(errors, nameof(StudentForm.FullName), form.FullName, "Informe o nome completo.", 200);
        Require(errors, nameof(StudentForm.Rg), form.Rg, "Informe o RG.", 30);
        Require(errors, nameof(StudentForm.Address), form.Address, "Informe o endereço.", 300);
        Require(errors, nameof(StudentForm.GuardianName), form.GuardianName, "Informe o nome do responsável.", 200);
        Require(errors, nameof(StudentForm.ClassName), form.ClassName, "Informe a turma.", 60);
        if (string.IsNullOrWhiteSpace(form.Cpf))
        {
            errors[nameof(StudentForm.Cpf)] = "Informe o CPF.";
        }
        else if (!Cpf.IsValid(form.Cpf))
        {
            errors[nameof(StudentForm.Cpf)] = "CPF inválido. Confira os 11 dígitos.";
        }

        if (form.BirthDate is not { } birth)
        {
            errors[nameof(StudentForm.BirthDate)] = "Informe a data de nascimento.";
        }
        else if (AgeBands.TryFromBirthDate(birth, today) is null)
        {
            errors[nameof(StudentForm.BirthDate)] = "O aluno precisa ter entre 6 e 14 anos.";
        }

        return errors;
    }

    private static void Require(Dictionary<string, string> errors, string field, string? value, string message, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = message;
        }
        else if (value.Trim().Length > max)
        {
            errors[field] = $"Use no máximo {max} caracteres.";
        }
    }

    private static DateOnly Today(IClock clock) => DateOnly.FromDateTime(clock.UtcNow.ToOffset(BrasiliaOffset).DateTime);
}
