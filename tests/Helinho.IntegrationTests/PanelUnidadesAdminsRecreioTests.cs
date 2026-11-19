using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Marcação do tema Recreio nas telas de Unidades e Administradores (#156).</summary>
[Collection(PostgresCollection.Name)]
public class PanelUnidadesAdminsRecreioTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<string> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await GetAsync(client, path);
        fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Lista_de_unidades_usa_page_head_e_table_wrap_sem_mapa()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var html = await GetAsync(client, "/Unidades");

        Assert.Contains("class=\"page-head\"", html);
        Assert.Contains("class=\"table-wrap\"", html);
        Assert.Contains("Nova unidade", html);
        foreach (var proibido in new[] { "leaflet", "unpkg", "openstreetmap", "tile.", "mapa", "latitude", "lat=", "btn-ai", "tag-ai" })
        {
            Assert.DoesNotContain(proibido, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Lista_de_administradores_mostra_situacao_com_texto_e_tag()
    {
        await using var factory = await NewFactoryAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            db.Units.Add(new Unit("Unidade Recreio"));
            await db.SaveChangesAsync(Ct);
        }

        using var client = factory.CreateFormClientAs(RoleNames.Master);
        var html = await GetAsync(client, "/Administradores");

        Assert.Contains("class=\"page-head\"", html);
        Assert.Contains("class=\"table-wrap\"", html);
        Assert.Contains("Novo administrador", html);
        Assert.DoesNotContain("btn-ai", html);
        Assert.DoesNotContain("Reenviar convite", html);
    }

    [Fact]
    public async Task Formulario_nova_unidade_usa_panel_form_e_erro_acessivel()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var vazio = await GetAsync(client, "/Unidades/Nova");
        var erro = await PostAsync(client, "/Unidades/Nova", new() { ["Input.Name"] = "" });

        Assert.Contains("panel panel-pad form", vazio);
        Assert.Contains("field-error", vazio);
        Assert.Contains("id=\"resumo-erros\"", erro);
        Assert.Contains("role=\"alert\"", erro);
        Assert.Contains("tabindex=\"-1\"", erro);
        Assert.Contains("aria-invalid=\"true\"", erro);
        Assert.Contains("aria-describedby=\"Input_Name-erro\"", erro);
    }

    [Fact]
    public async Task Formulario_novo_administrador_mantem_senha_sem_eco_e_regra_da_unidade()
    {
        await using var factory = await NewFactoryAsync();
        Guid unitId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var unit = new Unit("Unidade Dupla");
            db.Units.Add(unit);
            await db.SaveChangesAsync(Ct);
            unitId = unit.Id;
        }

        using var client = factory.CreateFormClientAs(RoleNames.Master);
        var vazio = await GetAsync(client, "/Administradores/Novo");
        Assert.Contains("panel panel-pad form", vazio);
        Assert.Contains("form-grid", vazio);
        Assert.Matches("<input[^>]*type=\"password\"[^>]*autocomplete=\"new-password\"|<input[^>]*autocomplete=\"new-password\"[^>]*type=\"password\"", vazio);

        Dictionary<string, string> Form(string user) => new()
        {
            ["Input.UserName"] = user,
            ["Input.Email"] = $"{user}@exemplo.test",
            ["Input.UnitId"] = unitId.ToString(),
            ["Input.Password"] = "Segredo-Inicial9x",
        };

        await PostAsync(client, "/Administradores/Novo", Form("adm.primeiro"));
        var segundo = await PostAsync(client, "/Administradores/Novo", Form("adm.segundo"));

        Assert.Contains("Esta unidade já tem um administrador ativo.", segundo);
        Assert.Contains("aria-describedby=\"Input_UnitId-erro\"", segundo);
        Assert.DoesNotContain("Segredo-Inicial9x", segundo);
    }
}
