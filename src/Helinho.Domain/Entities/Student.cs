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

    public string ClassName { get; private set; } = "";

    public bool GuardianApproved { get; private set; }

    public string Avatar { get; private set; } = "";

    public int Xp { get; private set; }

    public AgeBand AgeBandAt(DateOnly today) => AgeBands.FromBirthDate(BirthDate, today);

    public void ApproveByGuardian() => GuardianApproved = true;

    public void AddXp(int amount) =>
        Xp += amount < 0 ? throw new DomainException("XP ganho não pode ser negativo.") : amount;
}
