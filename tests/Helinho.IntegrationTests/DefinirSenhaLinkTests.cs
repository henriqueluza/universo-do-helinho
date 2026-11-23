using System.Text;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helinho.IntegrationTests;

/// <summary>Links de acesso (#239, ADR-0003 §10): provedores de 72 h e de 1 h, uso único e resposta única para link inválido. Contas sintéticas.</summary>
[Collection(PostgresCollection.Name)]
public class DefinirSenhaLinkTests(PostgresFixture postgres, DefinirSenhaLinkTests.Host shared) : IClassFixture<DefinirSenhaLinkTests.Host>
{
    private const string SenhaAntiga = "Antiga-Sintetica-1";
    private const string SenhaNova = "Nova-Sintetica-7";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Um host para a classe inteira (#215: cada host deixa observadores de arquivo); cada teste cria as próprias contas.
    /// Guarda os logs e confia no cabeçalho de IP do proxy, para os testes da página (#240) usarem um IP por teste.
    /// </summary>
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

    private async Task<T> ComAsync<T>(Func<UserManager<AppUser>, IServiceProvider, Task<T>> acao)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), scope.ServiceProvider);
    }

    private static async Task<AppUser> ContaAsync(UserManager<AppUser> users, Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("Conta de teste não encontrada.");

    private Task<AppUser> CriarContaAsync(string? senha) => ComAsync(async (users, _) =>
    {
        var nome = $"t{Guid.NewGuid():N}"[..12];
        var user = new AppUser { UserName = nome, Email = $"{nome}@example.com" };
        Assert.True((senha is null ? await users.CreateAsync(user) : await users.CreateAsync(user, senha)).Succeeded);
        return user;
    });

    private Task<string> EmitirAsync(Guid userId, string provedor) => ComAsync(async (users, _) =>
        LinkDeAcesso.TokenParaUrl(await users.GenerateUserTokenAsync(await ContaAsync(users, userId), provedor, LinkDeAcesso.Finalidade)));

    /// <summary>
    /// Token com a criação recuada, no formato do <c>DataProtectorTokenProvider</c>: criação, id, finalidade e carimbo.
    /// Os testes da página (#240) também o usam, com os serviços de um escopo do host compartilhado.
    /// </summary>
    internal static async Task<string> EmitirComIdadeAsync(IServiceProvider services, Guid userId, string provedor, TimeSpan idade)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        using var dados = new MemoryStream();
        using (var escritor = new BinaryWriter(dados, new UTF8Encoding(false, true), leaveOpen: true))
        {
            escritor.Write((DateTimeOffset.UtcNow - idade).UtcTicks);
            escritor.Write(userId.ToString());
            escritor.Write(LinkDeAcesso.Finalidade);
            escritor.Write(await users.GetSecurityStampAsync(await ContaAsync(users, userId)));
        }

        var protetor = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(provedor);
        return LinkDeAcesso.TokenParaUrl(Convert.ToBase64String(protetor.Protect(dados.ToArray())));
    }

    private Task<bool> ValeAsync(Guid userId, string token) => ComAsync((_, services) =>
        services.GetRequiredService<DefinirSenhaHandler>().LinkValidoAsync(userId.ToString(), token, Ct));

    private Task<DefinirSenhaResult> DefinirAsync(Guid userId, string token, string senha = SenhaNova) =>
        ComAsync((_, services) => services.GetRequiredService<DefinirSenhaHandler>().HandleAsync(userId.ToString(), token, senha, Ct));

    private Task<bool> SenhaConfereAsync(Guid userId, string senha) => ComAsync(async (users, _) =>
        await users.CheckPasswordAsync(await ContaAsync(users, userId), senha));

    [Theory]
    [InlineData(LinkDeAcesso.ProvedorConvite, (71 * 60) + 55, true)]
    [InlineData(LinkDeAcesso.ProvedorConvite, (72 * 60) + 5, false)]
    [InlineData(LinkDeAcesso.ProvedorRedefinicao, 55, true)]
    [InlineData(LinkDeAcesso.ProvedorRedefinicao, 65, false)]
    public async Task Convite_vale_72_horas_e_redefinicao_vale_1_hora(string provedor, int idadeEmMinutos, bool vale)
    {
        var conta = await CriarContaAsync(senha: null);

        var token = await ComAsync((_, services) => EmitirComIdadeAsync(services, conta.Id, provedor, TimeSpan.FromMinutes(idadeEmMinutos)));

        Assert.Equal(vale, await ValeAsync(conta.Id, token));
        Assert.Equal(vale ? DefinirSenhaStatus.Definida : DefinirSenhaStatus.LinkInvalido, (await DefinirAsync(conta.Id, token)).Status);
    }

    [Fact]
    public async Task Convite_define_a_senha_de_conta_sem_senha_uma_vez_e_conferir_ou_errar_a_senha_nao_gasta_o_link()
    {
        var conta = await CriarContaAsync(senha: null);
        var token = await EmitirAsync(conta.Id, LinkDeAcesso.ProvedorConvite);

        Assert.True(await ValeAsync(conta.Id, token));
        var fraca = await DefinirAsync(conta.Id, token, "curta");
        Assert.Equal(DefinirSenhaStatus.SenhaRecusada, fraca.Status);
        Assert.Contains("PasswordTooShort", fraca.ErrorCodes);
        Assert.True(await ValeAsync(conta.Id, token));
        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(conta.Id, token)).Status);
        Assert.True(await SenhaConfereAsync(conta.Id, SenhaNova));
        Assert.False(await ValeAsync(conta.Id, token));
        Assert.Equal(DefinirSenhaStatus.LinkInvalido, (await DefinirAsync(conta.Id, token, "Outra-Sintetica-3")).Status);
        Assert.True(await SenhaConfereAsync(conta.Id, SenhaNova));
    }

    [Fact]
    public async Task Redefinicao_troca_a_senha_mata_os_outros_links_da_conta_e_nao_mexe_no_bloqueio()
    {
        var conta = await CriarContaAsync(SenhaAntiga);
        await ComAsync(async (users, _) => await users.SetLockoutEndDateAsync(await ContaAsync(users, conta.Id), DateTimeOffset.MaxValue));
        var usado = await EmitirAsync(conta.Id, LinkDeAcesso.ProvedorRedefinicao);
        var outro = await EmitirAsync(conta.Id, LinkDeAcesso.ProvedorRedefinicao);

        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(conta.Id, usado)).Status);

        Assert.True(await SenhaConfereAsync(conta.Id, SenhaNova));
        Assert.False(await SenhaConfereAsync(conta.Id, SenhaAntiga));
        Assert.False(await ValeAsync(conta.Id, usado));
        Assert.False(await ValeAsync(conta.Id, outro));
        Assert.True(await ComAsync(async (users, _) => await users.IsLockedOutAsync(await ContaAsync(users, conta.Id))));
    }

    [Fact]
    public async Task Conta_inexistente_token_de_outra_conta_token_estranho_e_convite_para_quem_tem_senha_dao_o_mesmo_link_invalido()
    {
        var conta = await CriarContaAsync(SenhaAntiga);
        var outra = await CriarContaAsync(senha: null);
        var daOutra = await EmitirAsync(outra.Id, LinkDeAcesso.ProvedorConvite);

        var respostas = new[]
        {
            await DefinirAsync(Guid.NewGuid(), daOutra),
            await DefinirAsync(conta.Id, daOutra),
            await DefinirAsync(conta.Id, LinkDeAcesso.TokenParaUrl("nao-e-token")),
            // Conta que já tem senha não aceita o convite de 72 horas, nem recém-emitido (D4).
            await DefinirAsync(conta.Id, await EmitirAsync(conta.Id, LinkDeAcesso.ProvedorConvite)),
        };

        Assert.All(respostas, r => Assert.Equal(DefinirSenhaStatus.LinkInvalido, r.Status));
        Assert.True(await SenhaConfereAsync(conta.Id, SenhaAntiga));
        Assert.True(await ValeAsync(outra.Id, daOutra));
    }
}
