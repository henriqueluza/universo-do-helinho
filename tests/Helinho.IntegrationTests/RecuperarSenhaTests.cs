using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Redefinição real em <c>/recuperar-senha</c> (ADR-0003 §11.1 e §11.6): fila, resposta idêntica e limites. Contas sintéticas.</summary>
[Collection(PostgresCollection.Name)]
public class RecuperarSenhaTests(PostgresFixture postgres, RecuperarSenhaTests.Host shared) : IClassFixture<RecuperarSenhaTests.Host>
{
    private static int _ip; // um IP por pedido, para o limite por IP não entrar nos outros testes
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um host para a classe inteira (#215), atrás de proxy confiável e com relógio que os testes adiantam.</summary>
    public sealed class Host : IAsyncDisposable, IClock
    {
        private HelinhoWebFactory? _factory;

        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres, clock: this, configure: s => s.Configure<ProxyOptions>(o => o.TrustForwardedHeaders = true));
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public ValueTask DisposeAsync() => _factory?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private async Task<T> ComAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider);
    }

    private Task<AppUser> CriarContaAsync(string papel, string? email = null) => ComAsync(async services =>
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var nome = $"r{Guid.NewGuid():N}"[..12];
        var user = new AppUser { UserName = nome, Email = email ?? $"{nome}@example.com" };
        Assert.True((await users.CreateAsync(user, "Senha-Sintetica-1")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, papel)).Succeeded);
        return user;
    });

    private Task<List<QueueJob>> TrabalhosAsync(Guid userId) => ComAsync(services =>
        services.GetRequiredService<AppDbContext>().QueueJobs.AsNoTracking().Where(j => j.SubjectId == userId).ToListAsync(Ct));

    /// <summary>Pedidos diretos ao caso de uso, cada um em outra janela de idempotência; devolve quantos trabalhos a conta tem depois de cada um ("1,2,3,3").</summary>
    private async Task<string> PedirAsync(AppUser conta, int vezes = 1)
    {
        var contagens = new int[vezes];
        for (var i = 0; i < vezes; i++)
        {
            shared.UtcNow += TimeSpan.FromMinutes(3);
            contagens[i] = await ComAsync(async services =>
            {
                await services.GetRequiredService<PedirRedefinicaoHandler>().HandleAsync(conta.Email!, Ct);
                return await services.GetRequiredService<AppDbContext>().QueueJobs.CountAsync(j => j.SubjectId == conta.Id, Ct);
            });
        }

        return string.Join(',', contagens);
    }

    /// <summary>POST como a borda o entrega: o último valor de <c>X-Real-IP</c> é o da borda; os anteriores, do cliente.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string email, params string[] realIp)
    {
        var form = await client.GetStringAsync("/recuperar-senha", Ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/recuperar-senha");
        request.Headers.Add("X-Real-IP", realIp.Length > 0 ? realIp : [$"203.0.113.{Interlocked.Increment(ref _ip)}"]);
        request.Headers.Add("X-Forwarded-For", "198.51.100.99"); // escrito pelo cliente: não é lido
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["__RequestVerificationToken"] = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value,
        });
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Resposta_e_identica_exista_a_conta_ou_nao_e_so_a_conta_unica_do_painel_entra_na_fila()
    {
        var admin = await CriarContaAsync(RoleNames.Admin);
        var aluno = await CriarContaAsync(RoleNames.Student);
        var repetidas = new[] { await CriarContaAsync(RoleNames.Admin, "repetido@example.com"), await CriarContaAsync(RoleNames.Master, "repetido@example.com") };
        var noLimite = await CriarContaAsync(RoleNames.Admin);
        Assert.Equal("1,2,3", await PedirAsync(noLimite, 3));
        using var client = (await shared.GetAsync(postgres)).CreateAnonymousClient();

        var respostas = new List<string>();
        foreach (var email in new[] { admin.Email!.ToUpperInvariant(), "ninguem@example.com", aluno.Email!, "repetido@example.com", noLimite.Email! })
        {
            using var resposta = await PostAsync(client, email);
            var cabecalhos = resposta.Headers.Concat(resposta.Content.Headers).Where(h => h.Key != "Date")
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}").Order();
            respostas.Add($"{(int)resposta.StatusCode}\n{string.Join("\n", cabecalhos)}\n{await resposta.Content.ReadAsStringAsync(Ct)}");
        }

        Assert.Contains("Se o e-mail estiver cadastrado", WebUtility.HtmlDecode(respostas[0]), StringComparison.Ordinal);
        Assert.StartsWith("200\n", respostas[0], StringComparison.Ordinal);
        Assert.All(respostas, resposta => Assert.Equal(respostas[0], resposta));
        var trabalho = Assert.Single(await TrabalhosAsync(admin.Id));
        Assert.Equal((EmailJobTypes.PasswordReset, QueueJobStatus.Pending), (trabalho.Type, trabalho.Status));
        Assert.Equal(TimeSpan.FromMinutes(30), trabalho.ExpiresAt - trabalho.CreatedAt);
        Assert.Empty(await TrabalhosAsync(aluno.Id));
        Assert.Empty(await TrabalhosAsync(repetidas[0].Id));
        Assert.Empty(await TrabalhosAsync(repetidas[1].Id));
        Assert.Equal(3, (await TrabalhosAsync(noLimite.Id)).Count);
    }

    [Fact]
    public async Task Limite_por_conta_conta_so_links_vivos_do_carimbo_atual()
    {
        var conta = await CriarContaAsync(RoleNames.Master);
        Task<int> MarcarAsync(QueueJobStatus de, QueueJobStatus para) => ComAsync(services => services.GetRequiredService<AppDbContext>().QueueJobs
            .Where(j => j.SubjectId == conta.Id && j.Status == de).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, para), Ct));

        // Três pedidos de terceiros: o quarto é descartado, e há link vivo para o dono (enviado, em envio ou a caminho).
        Assert.Equal("1,2,3,3", await PedirAsync(conta, 4));
        Assert.All(await TrabalhosAsync(conta.Id), j => Assert.Equal(QueueJobStatus.Pending, j.Status));
        await MarcarAsync(QueueJobStatus.Pending, QueueJobStatus.Succeeded);
        Assert.Equal("3", await PedirAsync(conta));

        // Com os três em Failed, nenhum link chegou: o pedido seguinte é enfileirado.
        await MarcarAsync(QueueJobStatus.Succeeded, QueueJobStatus.Failed);
        Assert.Equal("4,5,6,6", await PedirAsync(conta, 4));

        // Qualquer troca de carimbo invalida os links anteriores: eles deixam de contar.
        await ComAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<AppUser>>();
            return await users.UpdateSecurityStampAsync(await users.FindByIdAsync(conta.Id.ToString()) ?? throw new InvalidOperationException());
        });
        Assert.Equal("7", await PedirAsync(conta));

        // Fora da janela (a validade do link), os pedidos antigos também deixam de contar.
        Assert.Equal("8,9,9", await PedirAsync(conta, 3));
        shared.UtcNow += LinkDeAcesso.ValidadeRedefinicao;
        Assert.Equal("10", await PedirAsync(conta));
    }

    [Fact]
    public async Task Corrida_na_chave_de_idempotencia_e_recusada_na_gravacao_sem_excecao()
    {
        var pedido = new JobRequest(EmailJobTypes.PasswordReset, Guid.NewGuid(), new JobPayload(), TimeSpan.FromMinutes(30), $"email.password-reset:{Guid.NewGuid():D}:corrida:1");
        async Task<bool> EnfileirarAsync(IServiceProvider services, Func<Task> antesDeGravar)
        {
            await services.GetRequiredService<IJobQueue>().EnqueueAsync(pedido, Ct);
            await antesDeGravar();
            return await services.GetRequiredService<IPasswordResetAccounts>().TrySaveAsync(Ct);
        }

        // Os dois pedidos passam pela conferência da chave antes de qualquer um gravar; o índice único recusa o segundo.
        var segundoGravou = await ComAsync(segundo => EnfileirarAsync(segundo, async () =>
            Assert.True(await ComAsync(primeiro => EnfileirarAsync(primeiro, () => Task.CompletedTask)))));

        Assert.False(segundoGravou);
    }

    [Fact]
    public async Task Limite_por_IP_vale_para_qualquer_e_mail_usa_o_IP_da_borda_e_deixa_o_GET_livre()
    {
        const string ClienteA = "192.0.2.10";
        var admin = await CriarContaAsync(RoleNames.Admin);
        using var client = (await shared.GetAsync(postgres)).CreateAnonymousClient();

        for (var i = 0; i < RedefinicaoDeSenhaArea.PedidosPorIp; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"pessoa{i}@example.com", ClienteA)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(client, admin.Email!, ClienteA)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(client, "ninguem@example.com", ClienteA)).StatusCode);
        // X-Real-IP escrito pelo cliente não troca de balde: só conta o último valor, o da borda.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostAsync(client, "ninguem@example.com", "198.51.100.7", ClienteA)).StatusCode);
        Assert.Empty(await TrabalhosAsync(admin.Id));
        using var get = new HttpRequestMessage(HttpMethod.Get, "/recuperar-senha");
        get.Headers.Add("X-Real-IP", ClienteA);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(get, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, admin.Email!, "192.0.2.20")).StatusCode);
        Assert.Single(await TrabalhosAsync(admin.Id));
    }
}
