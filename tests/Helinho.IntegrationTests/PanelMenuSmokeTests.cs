using System.Net;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Panel;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Rede de segurança da demonstração (#19): toda página do menu abre para master e administrador,
/// sem erro 500 e sem o texto "em construção". A lista de páginas vem do próprio <see cref="NavMenu"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PanelMenuSmokeTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Todos os itens do menu (visão do master), um caso por página.</summary>
    public static TheoryData<string> MenuPaths()
    {
        var data = new TheoryData<string>();
        foreach (var item in NavMenu.For(_ => true))
        {
            data.Add(item.Path);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MenuPaths))]
    public async Task Master_abre_toda_pagina_do_menu(string path)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSemEmConstrucao(path, await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(MenuPaths))]
    public async Task Admin_abre_a_pagina_do_menu_ou_e_barrado_nunca_500(string path)
    {
        await using var factory = await NewFactoryAsync();
        Guid unitId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            unitId = scope.ServiceProvider.GetRequiredService<AppDbContext>().Units.OrderBy(u => u.Name).First().Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);
        var visibleToAdmin = NavMenu.For(_ => false).Any(i => i.Path == path);

        var response = await client.GetAsync(path, Ct);

        if (visibleToAdmin)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertSemEmConstrucao(path, await response.Content.ReadAsStringAsync(Ct));
        }
        else
        {
            // Sem acesso: o painel redireciona para /acesso-negado (equivalente ao 403).
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/acesso-negado", new Uri(new Uri("http://localhost"), response.Headers.Location!).AbsolutePath);
        }
    }

    private static void AssertSemEmConstrucao(string path, string html) =>
        Assert.False(
            html.Contains("em construção", StringComparison.OrdinalIgnoreCase),
            $"A página {path} ainda mostra \"em construção\".");

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }
}
