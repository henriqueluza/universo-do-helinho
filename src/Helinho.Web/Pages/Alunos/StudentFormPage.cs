using Helinho.Application.Painel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Alunos;

/// <summary>Modelo próprio do formulário (anti over-posting): só estes campos são ligados; unidade, matrícula e usuário não.</summary>
public sealed class StudentInput
{
    public string? FullName { get; set; }

    public string? Rg { get; set; }

    public string? Cpf { get; set; }

    public string? Address { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? GuardianName { get; set; }

    public string? ClassName { get; set; }

    public bool GuardianApproved { get; set; }

    public StudentForm ToForm() => new(FullName, Rg, Cpf, Address, BirthDate, GuardianName, ClassName, GuardianApproved);

    public static StudentInput From(StudentForm f) => new()
    {
        FullName = f.FullName,
        Rg = f.Rg,
        Cpf = f.Cpf,
        Address = f.Address,
        BirthDate = f.BirthDate,
        GuardianName = f.GuardianName,
        ClassName = f.ClassName,
        GuardianApproved = f.GuardianApproved,
    };
}

public abstract class StudentFormPage : PageModel
{
    [BindProperty]
    public StudentInput Input { get; set; } = new();

    public IReadOnlyDictionary<string, string> Errors { get; protected set; } = new Dictionary<string, string>();

    public string? UnitName { get; protected set; }

    public string? Error(string field) => Errors.GetValueOrDefault(field);
}
