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

    /// <summary>ADR-0004 §4: o SDK do provedor de IA só existe na infraestrutura, atrás da porta.</summary>
    [Fact]
    public void So_a_infraestrutura_conhece_o_SDK_do_provedor_de_IA()
    {
        string[] sdk = ["Anthropic.AnthropicClient", "Anthropic.Core", "Anthropic.Models", "Anthropic.Exceptions", "Anthropic.Services"];
        foreach (var assembly in new[] { Domain, Application, Web })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(sdk).GetResult();

            Assert.True(result.IsSuccessful, Describe(result));
        }

        // Controle: a regra enxerga o SDK onde ele de fato é usado.
        Assert.False(Types.InAssembly(typeof(AppDbContext).Assembly).ShouldNot().HaveDependencyOnAny(sdk).GetResult().IsSuccessful);
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
