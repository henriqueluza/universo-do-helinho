using Helinho.Application.Contracts;
using Helinho.Domain;
using Helinho.Domain.Entities;

namespace Helinho.UnitTests;

/// <summary>E-mail do responsável (ADR-0003 §9): a regra de formato e de tamanho fica no domínio.</summary>
public class EmailAddressTests
{
    private static readonly DateOnly Today = new(2026, 11, 22);

    private static Student NewStudent() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ALU-SINT", "Aluno Fictício", "00.000.000-0", "000.000.000-00",
            "Rua Sintética, 1", new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", Today);

    [Theory]
    [InlineData("responsavel@example.org")]
    [InlineData("maria.souza+helinho@escola.example.org")]
    [InlineData("r_01@sub-dominio.example")]
    public void Endereco_bem_formado_e_aceito(string email) => Assert.True(EmailAddress.IsValid(email));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sem-arroba.example.org")]
    [InlineData("duas@@example.org")]
    [InlineData("a@b")]
    [InlineData("@example.org")]
    [InlineData("responsavel@.org")]
    [InlineData("responsavel@example..org")]
    [InlineData("com espaco@example.org")]
    [InlineData("Maria Souza <maria@example.org>")]
    [InlineData("a@example.org,b@example.org")]
    [InlineData("responsavel@example.org\n")]
    [InlineData("respon\u0000savel@example.org")]
    [InlineData("acentuação@example.org")]
    public void Endereco_malformado_e_recusado(string? email) => Assert.False(EmailAddress.IsValid(email));

    [Fact]
    public void Endereco_tem_no_maximo_254_caracteres()
    {
        var domain = "@" + string.Join('.', Enumerable.Repeat(new string('d', 60), 3)) + ".org"; // 187 caracteres

        Assert.True(EmailAddress.IsValid(new string('a', 64) + domain)); // 251
        Assert.False(EmailAddress.IsValid(new string('a', 64) + domain + ".abcd")); // 256
        Assert.False(EmailAddress.IsValid(new string('a', 65) + domain)); // parte local acima de 64
    }

    [Fact]
    public void Aluno_guarda_o_email_do_responsavel_sem_espacos_e_recusa_o_invalido()
    {
        var student = NewStudent();
        Assert.Null(student.GuardianEmail); // aluno anterior ao campo: anulável

        student.SetGuardianEmail("  responsavel@example.org ");

        Assert.Equal("responsavel@example.org", student.GuardianEmail);
        Assert.Null(student.PreviousGuardianEmail); // só a Issue do acesso do aluno escreve aqui
        Assert.Throws<DomainException>(() => student.SetGuardianEmail("nao-e-email"));
        Assert.Throws<DomainException>(() => student.SetGuardianEmail(" "));
        Assert.Equal("responsavel@example.org", student.GuardianEmail);
    }

    [Fact]
    public void Nenhum_DTO_da_API_do_jogo_tem_campo_do_responsavel()
    {
        var members = typeof(ApiJson).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ApiJson).Namespace)
            .SelectMany(t => t.GetProperties().Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.NotEmpty(members);
        Assert.DoesNotContain(members, m => m.Contains("Guardian", StringComparison.OrdinalIgnoreCase));
    }
}
