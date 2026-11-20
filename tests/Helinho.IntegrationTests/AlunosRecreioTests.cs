using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Telas de Alunos no tema Recreio (#152): marcação do tema, sem IA/lilás e sem dado pessoal na lista.</summary>
[Collection(PostgresCollection.Name)]
public partial class AlunosRecreioTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"<(script|style)\b.*?</\1>|<[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Tags();

    private async Task<(HelinhoWebFactory Factory, Guid UnitId)> NewAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = await db.Units.IgnoreQueryFilters().OrderBy(u => u.Name).FirstAsync(Ct);
        return (factory, unit.Id);
    }

    private static void AssertSemIa(string html)
    {
        foreach (var marca in new[] { "btn-ai", "tag-ai", "ai-help", "note ai", "--ai" })
        {
            Assert.DoesNotContain(marca, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("onchange", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<noscript", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lista_admin_usa_o_tema_Recreio_e_nao_mostra_dado_pessoal()
    {
        var (factory, unitId) = await NewAsync();
        await using var _ = factory;
        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);

        var html = await (await client.GetAsync("/Alunos", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("class=\"page-head\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"filters-bar\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"table-wrap\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"pager\"", html, StringComparison.Ordinal);
        Assert.Contains(">Aplicar</button>", html, StringComparison.Ordinal);
        Assert.Contains("Novo aluno", html, StringComparison.Ordinal);
        Assert.Contains("class=\"link-btn\"", html, StringComparison.Ordinal);
        Assert.Contains("<caption class=\"sr\">Alunos cadastrados</caption>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("caption class=\"visually-hidden\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"alunos-vazio\"", html, StringComparison.Ordinal);
        Assert.Matches(@"id=""alunos-resumo"">\d+ alunos?", html);
        AssertSemIa(html);
        var visivel = Tags().Replace(html, " ");
        Assert.DoesNotContain("CPF", visivel, StringComparison.Ordinal);
        Assert.DoesNotContain("RG", visivel, StringComparison.Ordinal);
        Assert.DoesNotContain("Endereço", visivel, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{3}\.\d{3}\.\d{3}-\d{2}", html);
    }

    [Fact]
    public async Task Lista_master_so_consulta_e_filtra_por_unidade()
    {
        var (factory, _) = await NewAsync();
        await using var _ = factory;
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await (await client.GetAsync("/Alunos", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("id=\"unidade\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Novo aluno", html, StringComparison.Ordinal);
        Assert.DoesNotContain("link-btn", html, StringComparison.Ordinal);
        AssertSemIa(html);
    }

    [Fact]
    public async Task Lista_sem_alunos_mostra_o_estado_vazio_do_tema_sem_tabela()
    {
        var (factory, _) = await NewAsync();
        await using var _ = factory;
        Guid unidadeVazia;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = new Unit("Unidade Vazia (fictícia)");
            db.Units.Add(unit);
            await db.SaveChangesAsync(Ct);
            unidadeVazia = unit.Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Master);
        var response = await client.GetAsync($"/Alunos?unidade={unidadeVazia}", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"panel panel-pad\" id=\"alunos-vazio\"", html, StringComparison.Ordinal);
        Assert.Contains("<b>Nenhum aluno encontrado.</b>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"empty\"", html, StringComparison.Ordinal);
        Assert.Contains("Limpar filtros", html, StringComparison.Ordinal);
        Assert.DoesNotContain("table-wrap", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<caption", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"pager\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("alunos-resumo", html, StringComparison.Ordinal);
        AssertSemIa(html);
    }

    [Fact]
    public async Task Editar_usa_o_tema_Recreio_com_os_dados_do_aluno()
    {
        var (factory, unitId) = await NewAsync();
        await using var _ = factory;
        Student aluno;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            aluno = await db.Students.IgnoreQueryFilters().AsNoTracking().OrderBy(s => s.Enrollment).FirstAsync(s => s.UnitId == unitId, Ct);
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);
        var response = await client.GetAsync($"/Alunos/Editar/{aluno.Id}", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("panel panel-pad form", html, StringComparison.Ordinal);
        Assert.Contains("class=\"form-grid\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"form-actions\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"note info\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{HtmlEncoder.Default.Encode(aluno.FullName)}\"", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{HtmlEncoder.Default.Encode(aluno.ClassName)}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("resumo-erros", html, StringComparison.Ordinal);
        AssertSemIa(html);
    }

    [Fact]
    public async Task Formulario_usa_o_tema_Recreio_com_os_mesmos_campos()
    {
        var (factory, unitId) = await NewAsync();
        await using var _ = factory;
        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);

        var html = await (await client.GetAsync("/Alunos/Novo", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("panel panel-pad form", html, StringComparison.Ordinal);
        Assert.Contains("class=\"form-grid\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"form-actions\"", html, StringComparison.Ordinal);
        foreach (var campo in new[] { "FullName", "BirthDate", "Rg", "Cpf", "Address", "GuardianName", "ClassName", "GuardianApproved" })
        {
            Assert.Contains($"name=\"Input.{campo}\"", html, StringComparison.Ordinal);
            Assert.Contains($"for=\"Input_{campo}\"", html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("resumo-erros", html, StringComparison.Ordinal);
        AssertSemIa(html);
    }

    [Fact]
    public async Task Formulario_invalido_mostra_resumo_focavel_e_campos_com_aria_invalid()
    {
        var (factory, unitId) = await NewAsync();
        await using var _ = factory;
        using var client = factory.CreateFormClientAs(RoleNames.Admin, unitId);
        var pagina = await (await client.GetAsync("/Alunos/Novo", Ct)).Content.ReadAsStringAsync(Ct);
        var token = Regex.Match(pagina, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync("/Alunos/Novo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.FullName"] = string.Empty,
        }), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("role=\"alert\" tabindex=\"-1\" id=\"resumo-erros\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"Input_FullName-erro\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"Input_FullName-erro\" class=\"field-error\"", html, StringComparison.Ordinal);
    }
}
