using System.Net;
using System.Text.RegularExpressions;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public partial class PainelScriptTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpResponseMessage Response, string Body)> GetScriptAsync()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateAnonymousClient();
        var response = await client.GetAsync("/js/painel.js", Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Painel_js_e_servido_sem_login_como_javascript()
    {
        var (response, body) = await GetScriptAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tipo = response.Content.Headers.ContentType?.MediaType;
        Assert.True(tipo is "text/javascript" or "application/javascript", $"Tipo inesperado: {tipo}");
        Assert.StartsWith("'use strict';", body);
    }

    [Theory]
    [InlineData("estado")] // estados de demonstração (?estado=...)
    [InlineData("URLSearchParams")]
    [InlineData("Maria Fernandes")] // feed "ao vivo" de exemplo
    [InlineData("feed")]
    [InlineData("Tudo certo")] // envio simulado do login
    [InlineData("Entrando")]
    [InlineData("resumo-erros")]
    public async Task Painel_js_nao_contem_codigo_so_da_preview(string trecho)
    {
        var (_, body) = await GetScriptAsync();

        Assert.DoesNotContain(trecho, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("setTimeout(\"")]
    [InlineData("fetch(")]
    [InlineData("XMLHttpRequest")]
    [InlineData("sendBeacon")]
    [InlineData("cssText")]
    [InlineData("setAttribute('style'")]
    public async Task Painel_js_nao_usa_apis_proibidas(string api)
    {
        var (_, body) = await GetScriptAsync();

        Assert.DoesNotContain(api, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Painel_js_nao_usa_manipulador_inline_nem_armazenamento_alem_do_tema()
    {
        var (_, body) = await GetScriptAsync();

        Assert.DoesNotMatch(ManipuladorInline(), body);
        Assert.DoesNotContain("sessionStorage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("document.cookie", body, StringComparison.Ordinal);
        Assert.All(ChavesDeLocalStorage().Matches(body), m => Assert.Equal("'tema'", m.Groups[1].Value));
    }

    [GeneratedRegex(@"setAttribute\(\s*['""]on\w+|\.on(click|load|error|change|submit)\s*=")]
    private static partial Regex ManipuladorInline();

    [GeneratedRegex(@"localStorage\.\w+Item\(\s*([^,)]+)")]
    private static partial Regex ChavesDeLocalStorage();
}
