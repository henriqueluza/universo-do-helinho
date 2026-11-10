using System.Reflection;
using Helinho.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace Helinho.ArchitectureTests;

public class ArchitectureRules
{
    private static readonly Assembly Domain = typeof(Helinho.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(Helinho.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Web = typeof(Program).Assembly;

    [Fact]
    public void Domain_e_puro()
    {
        string[] forbidden =
        [
            "Helinho.Application", "Helinho.Infrastructure", "Helinho.Web",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql",
        ];

        var result = Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_nao_depende_de_Infrastructure_nem_Web()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("Helinho.Infrastructure", "Helinho.Web").GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Web_nao_usa_DbContext_concreto_nem_Npgsql()
    {
        var result = Types.InAssembly(Web).ShouldNot()
            .HaveDependencyOnAny(typeof(AppDbContext).FullName!, "Npgsql").GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violações: " + string.Join(", ", result.FailingTypeNames ?? []);
}
