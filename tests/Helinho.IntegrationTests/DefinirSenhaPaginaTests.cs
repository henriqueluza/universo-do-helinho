using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helinho.IntegrationTests;

/// <summary>Página /conta/definir-senha (#240, ADR-0003 §10.3 e §11.3). Contas e endereços de IP sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public partial class DefinirSenhaPaginaTests(PostgresFixture postgres, DefinirSenhaPaginaTests.Host shared) : IClassFixture<DefinirSenhaPaginaTests.Host>
{
    private const string Pagina = "/conta/definir-senha";
    private const string SenhaNova = "Nova-Sintetica-7";
    private const string LinkInvalido = "Este link não vale mais";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"(__RequestVerificationToken|Usuario|Token)\"[^>]*value=\"([^\"]*)\"")]
    private static partial Regex CampoOculto();

    /// <summary>Um host para a classe inteira (#215). Confia no cabeçalho de IP do proxy: cada teste usa um IP próprio e não divide o limite de taxa.</summary>
    public sealed class Host : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        public LogCapturado Logs { get; } = new();

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres, configure: s => s
                    .AddSingleton<ILoggerProvider>(Logs).Configure<ProxyOptions>(o => o.TrustForwardedHeaders = true));
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public ValueTask DisposeAsync() => _factory?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private async Task<HttpClient> ClienteAsync(string ip)
    {
        var client = (await shared.GetAsync(postgres)).CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Real-IP", ip);
        return client;
    }

    /// <summary>Conta nova, sem senha, e o link dela, emitido pelo provedor como o tratador de e-mail fará.</summary>
    private async Task<(Guid UserId, string Link, string Token)> ContaComLinkAsync(string provedor = LinkDeAcesso.ProvedorConvite)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var nome = $"t{Guid.NewGuid():N}"[..12];
        var user = new AppUser { UserName = nome, Email = $"{nome}@example.com" };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var token = await users.GenerateUserTokenAsync(user, provedor, LinkDeAcesso.Finalidade);
        return (user.Id, LinkDeAcesso.Montar(user.Id, token), LinkDeAcesso.TokenParaUrl(token));
    }

    private static async Task<(HttpResponseMessage Response, string Html)> AbrirAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        return (response, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)));
    }

    /// <summary>Envia o formulário como o navegador: com os campos ocultos que a página devolveu.</summary>
    private static async Task<(HttpResponseMessage Response, string Html)> EnviarAsync(HttpClient client, string formulario, string senha, string? confirmacao = null)
    {
        var campos = CampoOculto().Matches(formulario).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        campos["Input.Password"] = senha;
        campos["Input.Confirmation"] = confirmacao ?? senha;
        var response = await client.PostAsync(Pagina, new FormUrlEncodedContent(campos), Ct);
        return (response, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Link_valido_mostra_o_formulario_com_o_token_so_no_campo_oculto_e_abrir_nao_gasta_o_link()
    {
        var (_, link, token) = await ContaComLinkAsync();
        using var client = await ClienteAsync("203.0.113.11");

        await AbrirAsync(client, link);
        var (response, html) = await AbrirAsync(client, link);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("class=\"bare-card\" tabindex=\"-1\" data-limpar-endereco", html);
        Assert.Contains($"action=\"{Pagina}\"", html);
        Assert.Equal(2, Regex.Count(html, "type=\"password\"[^>]*autocomplete=\"new-password\"|autocomplete=\"new-password\"[^>]*type=\"password\""));
        Assert.Equal(1, Regex.Count(html, Regex.Escape(token)));
        Assert.Equal(token, CampoOculto().Matches(html).Single(m => m.Groups[1].Value == "Token").Groups[2].Value);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Equal(1, Regex.Count(html, "<h1\\b"));
        // Sem script inline e sem recurso de outro domínio: nada leva o endereço (com o token) para fora.
        Assert.Empty(Regex.Matches(html, @"<script\b(?![^>]*\ssrc=)"));
        Assert.Empty(Regex.Matches(html, "(src|href|action)=\"(https?:)?//"));
    }

    [Theory]
    [InlineData(LinkDeAcesso.ProvedorConvite, "203.0.113.12")]
    [InlineData(LinkDeAcesso.ProvedorRedefinicao, "203.0.113.13")]
    public async Task Senha_valida_e_gravada_sem_login_automatico_e_o_link_deixa_de_valer(string provedor, string ip)
    {
        var (userId, link, token) = await ContaComLinkAsync(provedor);
        using var client = await ClienteAsync(ip);
        var (_, formulario) = await AbrirAsync(client, link);

        var (response, _) = await EnviarAsync(client, formulario, SenhaNova);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{Pagina}?definida=True", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain("helinho.panel=", string.Join(';', response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : []));
        var (final, sucesso) = await AbrirAsync(client, $"{Pagina}?definida=True");
        Assert.True(final.Headers.CacheControl?.NoStore);
        Assert.Contains("Senha definida", sucesso);
        Assert.Contains("href=\"/entrar\"", sucesso);
        Assert.DoesNotContain("data-limpar-endereco", sucesso);
        Assert.Contains(LinkInvalido, (await AbrirAsync(client, link)).Html);
        Assert.Contains(LinkInvalido, (await EnviarAsync(client, formulario, "Outra-Sintetica-3")).Html);

        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(userId.ToString()))!, SenhaNova));
        var linhas = shared.Logs.Linhas.ToArray();
        Assert.Contains(linhas, l => l.Contains($"Senha definida por link de acesso para a conta {userId}", StringComparison.Ordinal));
        Assert.DoesNotContain(linhas, l => l.Contains(token, StringComparison.Ordinal)
            || l.Contains(LinkDeAcesso.LerToken(token)!, StringComparison.Ordinal) || l.Contains(SenhaNova, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Link_invalido_tem_a_mesma_resposta_para_qualquer_causa_e_oferece_pedir_outro()
    {
        var (userId, _, _) = await ContaComLinkAsync();
        var (_, _, deOutraConta) = await ContaComLinkAsync();
        using var client = await ClienteAsync("203.0.113.14");
        string[] urls =
        [
            Pagina,
            $"{Pagina}?usuario={Guid.NewGuid()}&token={deOutraConta}",
            $"{Pagina}?usuario={userId}&token={deOutraConta}",
            $"{Pagina}?usuario={userId}&token=nao-e-token",
            $"{Pagina}?usuario=nao-e-uuid&token={deOutraConta}",
        ];

        var respostas = new List<(HttpResponseMessage Response, string Html)>();
        foreach (var url in urls)
        {
            respostas.Add(await AbrirAsync(client, url));
        }

        Assert.All(respostas, r => Assert.Equal((HttpStatusCode.OK, respostas[0].Html), (r.Response.StatusCode, r.Html)));
        Assert.All(respostas, r => Assert.Equal("no-referrer", Assert.Single(r.Response.Headers.GetValues("Referrer-Policy"))));
        Assert.Contains(LinkInvalido, respostas[0].Html);
        Assert.Contains("href=\"/recuperar-senha\"", respostas[0].Html);
        Assert.DoesNotContain("<form", respostas[0].Html);
        Assert.DoesNotContain(deOutraConta, string.Concat(respostas.Select(r => r.Html)));
    }

    [Fact]
    public async Task Senha_fora_da_politica_ou_confirmacao_diferente_volta_com_resumo_de_erros_e_o_link_continua_valendo()
    {
        var (_, link, token) = await ContaComLinkAsync();
        using var client = await ClienteAsync("203.0.113.15");
        var (_, formulario) = await AbrirAsync(client, link);

        var (_, fraca) = await EnviarAsync(client, formulario, "curta");
        var (_, diferente) = await EnviarAsync(client, formulario, SenhaNova, "Outra-Sintetica-3");
        var (_, vazia) = await EnviarAsync(client, formulario, "");

        Assert.All(new[] { fraca, diferente, vazia }, html =>
        {
            Assert.Contains("class=\"alert\" role=\"alert\" tabindex=\"-1\" id=\"resumo-erros\"", html);
            Assert.Contains("class=\"field-error\"", html);
            Assert.Contains("aria-invalid=\"true\"", html);
            Assert.Equal(1, Regex.Count(html, Regex.Escape(token)));
        });
        Assert.Contains("<a href=\"#Input_Password\">A senha deve ter pelo menos 6 caracteres.</a>", fraca);
        Assert.Contains("aria-describedby=\"Input_Password-erro senha-dica\"", fraca);
        Assert.Contains("<a href=\"#Input_Confirmation\">As duas senhas precisam ser iguais.</a>", diferente);
        Assert.Contains("Informe a nova senha.", vazia);
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(client, formulario, SenhaNova)).Response.StatusCode);
    }

    [Fact]
    public async Task Envio_sem_antiforgery_e_recusado_e_o_decimo_primeiro_do_mesmo_ip_recebe_429_sem_contar_os_gets()
    {
        var (_, link, _) = await ContaComLinkAsync();
        using var client = await ClienteAsync("203.0.113.16");
        using var outroIp = await ClienteAsync("203.0.113.17");
        Task<HttpResponseMessage> SemAntiforgery(HttpClient c) =>
            c.PostAsync(Pagina, new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Password"] = SenhaNova }), Ct);

        for (var i = 0; i < 10; i++)
        {
            await AbrirAsync(client, link);
            Assert.Equal(HttpStatusCode.BadRequest, (await SemAntiforgery(client)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SemAntiforgery(client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AbrirAsync(client, link)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SemAntiforgery(outroIp)).StatusCode);
    }
}
