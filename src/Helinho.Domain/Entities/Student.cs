namespace Helinho.Domain.Entities;

/// <summary>Dados cadastrais e de progresso do aluno; a credencial fica no usuário do Identity (<see cref="UserId"/>).</summary>
public class Student
{
    private Student()
    {
    }

    public Student(
        Guid userId, Guid unitId, string enrollment, string fullName, string rg, string cpf,
        string address, DateOnly birthDate, string guardianName, string className, DateOnly today)
    {
        UserId = Guard.Required(userId, "Usuário");
        UnitId = Guard.Required(unitId, "Unidade");
        Enrollment = Guard.Required(enrollment, "Matrícula");
        FullName = Guard.Required(fullName, "Nome completo");
        Rg = Guard.Required(rg, "RG");
        Cpf = Guard.Required(cpf, "CPF");
        Address = Guard.Required(address, "Endereço");
        GuardianName = Guard.Required(guardianName, "Responsável");
        ClassName = Guard.Required(className, "Turma");
        BirthDate = birthDate;
        AgeBands.FromBirthDate(birthDate, today);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; }

    public Guid UnitId { get; private set; }

    public string Enrollment { get; private set; } = "";

    public string FullName { get; private set; } = "";

    public string Rg { get; private set; } = "";

    public string Cpf { get; private set; } = "";

    public string Address { get; private set; } = "";

    public DateOnly BirthDate { get; private set; }

    public string GuardianName { get; private set; } = "";

    /// <summary>E-mail do responsável (ADR-0003 §9). Não é único nem identificador de login; nulo nos alunos anteriores ao campo.</summary>
    public string? GuardianEmail { get; private set; }

    /// <summary>Endereço anterior, guardado só enquanto o aviso de troca não termina (ADR-0003 §9 e §11.4).</summary>
    public string? PreviousGuardianEmail { get; private set; }

    public string ClassName { get; private set; } = "";

    public bool GuardianApproved { get; private set; }

    public string Avatar { get; private set; } = "";

    public int Xp { get; private set; }

    public AgeBand AgeBandAt(DateOnly today) => AgeBands.FromBirthDate(BirthDate, today);

    public void ApproveByGuardian() => GuardianApproved = true;

    public void SetGuardianApproval(bool approved) => GuardianApproved = approved;

    /// <summary>Edição cadastral: a unidade, a matrícula e a credencial não mudam por aqui.</summary>
    public void UpdateRegistration(
        string fullName, string rg, string cpf, string address, DateOnly birthDate, string guardianName, string className, DateOnly today)
    {
        AgeBands.FromBirthDate(birthDate, today);
        FullName = Guard.Required(fullName, "Nome completo");
        Rg = Guard.Required(rg, "RG");
        Cpf = Guard.Required(cpf, "CPF");
        Address = Guard.Required(address, "Endereço");
        GuardianName = Guard.Required(guardianName, "Responsável");
        ClassName = Guard.Required(className, "Turma");
        BirthDate = birthDate;
    }

    public void SetGuardianEmail(string? email)
    {
        var value = Guard.Required(email, "E-mail do responsável");
        GuardianEmail = EmailAddress.IsValid(value) ? value : throw new DomainException("E-mail do responsável inválido.");
    }

    /// <summary>Guarda o endereço que o aviso de troca vai usar; só existe enquanto o aviso não termina (ADR-0003 §9).</summary>
    public void KeepPreviousGuardianEmail(string email) => PreviousGuardianEmail = Guard.Required(email, "E-mail anterior do responsável");

    public void ClearPreviousGuardianEmail() => PreviousGuardianEmail = null;

    public void AddXp(int amount) =>
        Xp += amount < 0 ? throw new DomainException("XP ganho não pode ser negativo.") : amount;
}
