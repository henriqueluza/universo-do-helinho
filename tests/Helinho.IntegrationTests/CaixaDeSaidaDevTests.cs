using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Pages.Dev;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Helinho.IntegrationTests;

/// <summary>Caixa de saída de desenvolvimento (#281, ADR-0003 D8) em <c>Development</c>, o ambiente padrão da fábrica. Dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public class CaixaDeSaidaDevTests(PostgresFixture postgres, CaixaDeSaidaDevTests.Host shared) : IClassFixture<CaixaDeSaidaDevTests.Host>
{
    public const string Path = "/Dev/CaixaDeSaida";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um host para a classe inteira (#215). Só um teste envia e-mail: a caixa é a mesma para todos.</summary>
    public sealed class Host : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres);
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public ValueTask DisposeAsync() => _factory?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Anonimo_vai_para_o_login_e_administrador_de_unidade_e_barrado()
    {
        var factory = await shared.GetAsync(postgres);
        using var anonimo = factory.CreateAnonymousClient();
        using var admin = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var semSessao = await anonimo.GetAsync(Path, Ct);
        var comum = await admin.GetAsync(Path, Ct);

        Assert.Equal(HttpStatusCode.Redirect, semSessao.StatusCode);
        Assert.StartsWith("/entrar", semSessao.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, comum.StatusCode);
        Assert.StartsWith("/acesso-negado", comum.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_ve_a_caixa_vazia_e_depois_os_e_mails_do_mais_recente_ao_mais_antigo_sem_executar_o_html()
    {
        var factory = await shared.GetAsync(postgres);
        using var master = factory.CreateClientAs(RoleNames.Master);

        var vazia = await master.GetAsync(Path, Ct);
        var htmlVazia = await vazia.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, vazia.StatusCode);
        Assert.Matches("""<div class="empty">\s*<img src="/img/cara-helinho\.webp"[^>]* width="117" height="120" />\s*<b>Nenhum e-mail""", htmlVazia);
        Assert.DoesNotContain("<iframe", htmlVazia);

        // 1) pelo fluxo real da recuperação de senha; 2) uma mensagem hostil, direto na porta de envio.
        var destino = await CriarAdministradorAsync(factory);
        using (var anonimo = factory.CreateAnonymousClient())
        {
            var formulario = await anonimo.GetStringAsync("/recuperar-senha", Ct);
            var token = Regex.Match(formulario, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            await anonimo.PostAsync("/recuperar-senha", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = destino,
                ["__RequestVerificationToken"] = token,
            }), Ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            Assert.IsType<FakeEmailSender>(sender);
            await sender.SendAsync(new EmailMessage(
                "convidada@example.org", "Convite de teste",
                "<script>alert(1)</script><img src=\"https://example.org/pixel.png\"><p>Abra o link</p>",
                "Abra <b>o link</b>: https://painel.example.org/conta/definir-senha?t=abc"), Ct);
        }

        var cheia = await master.GetAsync(Path, Ct);
        var html = await cheia.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, cheia.StatusCode);
        Assert.Contains(destino, html);
        Assert.Contains("convidada@example.org", html);
        Assert.True(html.IndexOf("Convite de teste", StringComparison.Ordinal) < html.IndexOf(destino, StringComparison.Ordinal), "a mais recente vem primeiro");
        Assert.Matches("""<time datetime="[^"]+">\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}</time>""", html);
        // Texto e HTML saem codificados: nada do corpo vira marcação da página.
        Assert.Contains("Abra &lt;b&gt;o link&lt;/b&gt;: https://painel.example.org/conta/definir-senha?t=abc", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<img src=\"https://example.org", html);
        Assert.Contains("""<iframe class="outbox-frame" sandbox="" srcdoc="&lt;script&gt;alert(1)&lt;/script&gt;&lt;img src=&quot;https://example.org/pixel.png&quot;&gt;""", html);
        Assert.DoesNotContain("allow-scripts", html);
        Assert.DoesNotContain("allow-same-origin", html);
        Assert.Equal(CaixaDeSaidaModel.ContentSecurityPolicy, Assert.Single(cheia.Headers.GetValues("Content-Security-Policy")));
        Assert.StartsWith("default-src 'self';", CaixaDeSaidaModel.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("http", CaixaDeSaidaModel.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.True(cheia.Headers.CacheControl!.NoStore);
    }

    private static async Task<string> CriarAdministradorAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var nome = $"cx{Guid.NewGuid():N}"[..12];
        var user = new AppUser { UserName = nome, Email = $"{nome}@example.com" };
        Assert.True((await users.CreateAsync(user, "Senha-Sintetica-1")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.Admin)).Succeeded);
        return user.Email;
    }
}

/// <summary>Fora de <c>Development</c> a rota não existe e o envio Fake não guarda nada (#281).</summary>
[Collection(PostgresCollection.Name)]
public class CaixaDeSaidaForaDeDevTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Em_Production_a_rota_responde_404_para_master_e_anonimo_e_o_fake_nao_guarda_mensagem()
    {
        await using var factory = new HelinhoWebFactory(postgres).WithWebHostBuilder(b => b.UseEnvironment(Environments.Production));
        // https desde o cliente: fora de Development, http é redirecionado antes de chegar à rota.
        WebApplicationFactoryClientOptions https = new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://painel.example.org") };
        using var anonimo = factory.CreateClient(https);
        using var master = factory.CreateClient(https);
        master.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, RoleNames.Master);

        var controle = await anonimo.GetAsync("/entrar", Ct);
        var semSessao = await anonimo.GetAsync(CaixaDeSaidaDevTests.Path, Ct);
        var comMaster = await master.GetAsync(CaixaDeSaidaDevTests.Path, Ct);
        var minusculas = await master.GetAsync(CaixaDeSaidaDevTests.Path.ToLowerInvariant(), Ct);

        Assert.Equal(HttpStatusCode.OK, controle.StatusCode); // o host responde páginas; o 404 é da rota
        Assert.Equal(HttpStatusCode.NotFound, semSessao.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, comMaster.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, minusculas.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        Assert.IsType<FakeEmailSender>(sender);
        await sender.SendAsync(new EmailMessage("alguem@example.org", "Assunto", "<p>corpo</p>", "corpo"), Ct);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<DevEmailOutbox>().Snapshot());
    }
}
