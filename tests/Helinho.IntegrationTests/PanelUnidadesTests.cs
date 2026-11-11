using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PanelUnidadesTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await (await client.GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct);
        fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    [Theory]
    [InlineData("/Unidades")]
    [InlineData("/Unidades/Nova")]
    public async Task Administrador_comum_recebe_403_por_url_direta(string path)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var redirect = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var response = await client.GetAsync(redirect.Headers.Location, Ct);

        Assert.Equal("/acesso-negado", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/Unidades/Nova")]
    public async Task Administrador_comum_nao_cria_por_post(string path)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()), Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/acesso-negado", new Uri(new Uri("http://localhost"), response.Headers.Location!).AbsolutePath);
    }

    [Fact]
    public async Task Master_cria_unidade_e_ela_aparece_na_lista()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var response = await PostFormAsync(client, "/Unidades/Nova", new() { ["Input.Name"] = "Unidade Horizonte" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await (await client.GetAsync("/Unidades", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Unidade Horizonte", list);
    }

    [Fact]
    public async Task Unidade_sem_nome_mostra_mensagem_em_portugues()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var response = await PostFormAsync(client, "/Unidades/Nova", new() { ["Input.Name"] = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Informe o nome da unidade.", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Criar_sem_antiforgery_e_recusado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var response = await client.PostAsync("/Unidades/Nova",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Name"] = "X" }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
