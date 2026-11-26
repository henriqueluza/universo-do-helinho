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

    /// <summary>
    /// ADR-0003 §5.3 (#270): o tratador de e-mail não grava nada e não escreve destinatário, token, link nem corpo em
    /// log. Ele nem tem como: não depende de <c>ILogger</c>, do EF Core nem do contexto do banco.
    /// </summary>
    [Fact]
    public void Tratador_de_email_nao_tem_como_gravar_nem_escrever_em_log()
    {
        string[] forbidden =
        [
            "Microsoft.Extensions.Logging", "Microsoft.EntityFrameworkCore",
            typeof(AppDbContext).FullName!, typeof(Helinho.Application.Abstractions.IAppDbContext).FullName!,
        ];
        var infrastructure = typeof(AppDbContext).Assembly;
        var handler = Types.InAssembly(infrastructure).That().HaveName("EmailJobHandler");

        var result = handler.ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, Describe(result));

        // Controle: a regra achou o tratador e enxerga log e banco onde eles de fato são usados.
        Assert.Single(handler.GetTypes());
        Assert.False(Types.InAssembly(infrastructure).ShouldNot().HaveDependencyOnAny(forbidden).GetResult().IsSuccessful);
    }

    /// <summary>
    /// ADR-0004 (segurança de LLM): nada do texto gerado, do prompt ou do assunto digitado vai para log. Os casos de uso
    /// de IA nem têm como registrar: nenhum tipo de <c>Helinho.Application.Ai</c> depende de <c>ILogger</c>.
    /// </summary>
    [Fact]
    public void Casos_de_uso_de_IA_nao_tem_como_escrever_em_log()
    {
        const string logging = "Microsoft.Extensions.Logging";

        var result = Types.InAssembly(Application).That().ResideInNamespace("Helinho.Application.Ai")
            .ShouldNot().HaveDependencyOn(logging).GetResult();

        Assert.True(result.IsSuccessful, Describe(result));

        // Controle: a regra enxerga o ILogger onde ele de fato é usado, e o namespace de IA não está vazio.
        Assert.False(Types.InAssembly(typeof(AppDbContext).Assembly).ShouldNot().HaveDependencyOn(logging).GetResult().IsSuccessful);
        Assert.NotEmpty(Types.InAssembly(Application).That().ResideInNamespace("Helinho.Application.Ai").GetTypes());
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
