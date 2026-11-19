using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Panel;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Acabamento do tema Recreio (#189): nenhuma página do painel guarda estilo em <c>style=</c>, a não ser o
/// valor que só o servidor conhece (variável CSS por instância ou largura percentual de barra). Layout, cor e
/// posição ficam no <c>theme.css</c>, o que deixa a porta aberta para uma CSP sem <c>unsafe-inline</c> em estilos.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class PainelSemStyleInlineTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\sstyle=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex(@"(?<![\w-])([a-z-]+)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex Property();

    /// <summary>Propriedades que um <c>style=</c> pode declarar: variável CSS (<c>--x</c>) ou <c>width</c> de barra de dado.</summary>
    private static bool Permitida(string propriedade) =>
        propriedade.StartsWith("--", StringComparison.Ordinal) || propriedade.Equals("width", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Violacoes(string origem, string texto)
    {
        foreach (Match atributo in StyleAttribute().Matches(texto))
        {
            // Expressão Razor (@IndexModel.Algo): só o texto fixo conta; o valor dinâmico não declara propriedade.
            var valor = Regex.Replace(atributo.Groups[1].Value, @"@\([^)]*\)|@[\w.]+(\([^)]*\))?", "0");
            foreach (Match propriedade in Property().Matches(valor))
            {
                if (!Permitida(propriedade.Groups[1].Value))
                {
                    yield return $"{origem}: style=\"{atributo.Groups[1].Value}\" declara '{propriedade.Groups[1].Value}'";
                }
            }
        }
    }

    [Fact]
    public void Marcacao_das_paginas_nao_declara_layout_nem_cor_em_style()
    {
        var pages = Path.Combine(FindRepositoryRoot(), "src", "Helinho.Web", "Pages");
        var achados = Directory.EnumerateFiles(pages, "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(f => Violacoes(Path.GetRelativePath(pages, f), File.ReadAllText(f)))
            .ToList();

        Assert.True(achados.Count == 0, string.Join(Environment.NewLine, achados));
    }

    [Fact]
    public void Tema_declara_foco_alvo_de_clique_e_rolagem_que_a_marcacao_antes_contornava()
    {
        var css = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Helinho.Web", "wwwroot", "css", "theme.css"));

        Assert.Matches(@":is\(\.input, \.select\)\[aria-invalid=""true""\] \{ border-color: var\(--bad\)", css);
        Assert.Matches(@"\.alt \.correct \{[^}]*min-height: var\(--h-control\)", css);
        Assert.Matches(@"\.heat tbody td:is\(:hover, :focus-visible\) span \{ opacity: 1", css);
        Assert.Matches(@"\.tscroll \{ position: relative", css);
        Assert.Matches(@"\.table-wrap \{ position: relative", css);
        Assert.Contains(".two > * { min-width: 0; }", css);
        Assert.DoesNotContain(".select:focus-visible { outline: none", css);
        Assert.DoesNotContain(".input:focus { outline: none", css);
    }

    [Fact]
    public void Verificador_pega_o_contorno_que_o_teste_existe_para_barrar()
    {
        Assert.NotEmpty(Violacoes("x", """<section style="min-width:0"></section>"""));
        Assert.NotEmpty(Violacoes("x", """<div style="position:relative"></div>"""));
        Assert.NotEmpty(Violacoes("x", """<span style="background:var(--q-con-bg);color:red"></span>"""));
        Assert.Empty(Violacoes("x", """<span style="--t:var(--tile-1)"></span>"""));
        Assert.Empty(Violacoes("x", """<i style="width: @(pct)%"></i>"""));
        Assert.Empty(Violacoes("x", """<td style="--v:@IndexModel.Inv(x);--k:@i"></td>"""));
    }

    [Fact]
    public async Task Paginas_renderizadas_do_menu_e_da_conta_nao_declaram_layout_nem_cor_em_style()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        Guid unitId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            unitId = scope.ServiceProvider.GetRequiredService<AppDbContext>().Units.OrderBy(u => u.Name).First().Id;
        }

        var achados = new List<string>();
        using (var master = factory.CreateClientAs(RoleNames.Master))
        {
            var paths = NavMenu.For(_ => true).Select(i => i.Path).Concat(["/Unidades/Nova", "/Administradores/Novo", "/Questoes/Editar"]);
            foreach (var path in paths)
            {
                achados.AddRange(Violacoes(path, await GetOkAsync(master, path)));
            }
        }

        using (var admin = factory.CreateClientAs(RoleNames.Admin, unitId))
        {
            foreach (var path in new[] { "/Dashboard", "/Alunos", "/Alunos/Novo", "/Desempenho", "/Eventos" })
            {
                achados.AddRange(Violacoes(path, await GetOkAsync(admin, path)));
            }
        }

        using (var anonimo = factory.CreateClient())
        {
            foreach (var path in new[] { "/entrar", "/recuperar-senha" })
            {
                achados.AddRange(Violacoes(path, await GetOkAsync(anonimo, path)));
            }

            var negado = await anonimo.GetAsync("/Conta/AcessoNegado", Ct);
            achados.AddRange(Violacoes("/Conta/AcessoNegado", await negado.Content.ReadAsStringAsync(Ct)));
        }

        Assert.True(achados.Count == 0, string.Join(Environment.NewLine, achados.Distinct()));
    }

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} respondeu {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Helinho.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Helinho.slnx não encontrado acima do diretório de testes.");
    }
}
