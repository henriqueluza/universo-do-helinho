using System.Text;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.Persistence;
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

    /// <summary>Um host para a classe inteira (#215: cada host deixa observadores de arquivo); cada teste cria as próprias contas.</summary>
    public sealed class Host : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        public LogCapturado Logs { get; } = new();

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres, configure: s => s.AddSingleton<ILoggerProvider>(Logs));
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public async ValueTask DisposeAsync()
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }
        }
    }

    private static async Task<T> ComAsync<T>(HelinhoWebFactory factory, Func<UserManager<AppUser>, IServiceProvider, Task<T>> acao)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), scope.ServiceProvider);
    }

    private static Task<AppUser> ContaAsync(UserManager<AppUser> users, Guid userId) => users.FindByIdAsync(userId.ToString())!;

    private static Task<AppUser> CriarContaAsync(HelinhoWebFactory factory, string? senha) => ComAsync(factory, async (users, _) =>
    {
        var nome = $"t{Guid.NewGuid():N}"[..12];
        var user = new AppUser { UserName = nome, Email = $"{nome}@example.com" };
        Assert.True((senha is null ? await users.CreateAsync(user) : await users.CreateAsync(user, senha)).Succeeded);
        return user;
    });

    /// <summary>Token como vai na URL, emitido pelo provedor como o tratador de e-mail fará.</summary>
    private static Task<string> EmitirAsync(HelinhoWebFactory factory, Guid userId, string provedor) => ComAsync(factory, async (users, _) =>
        NaUrl(userId, await users.GenerateUserTokenAsync(await ContaAsync(users, userId), provedor, LinkDeAcesso.Finalidade)));

    /// <summary>Token com a criação recuada, no formato do <c>DataProtectorTokenProvider</c>: criação, id, finalidade e carimbo.</summary>
    private static Task<string> EmitirComIdadeAsync(HelinhoWebFactory factory, Guid userId, string provedor, TimeSpan idade) => ComAsync(factory, async (users, services) =>
    {
        using var dados = new MemoryStream();
        using (var escritor = new BinaryWriter(dados, new UTF8Encoding(false, true), leaveOpen: true))
        {
            escritor.Write((DateTimeOffset.UtcNow - idade).UtcTicks);
            escritor.Write(userId.ToString());
            escritor.Write(LinkDeAcesso.Finalidade);
            escritor.Write(await users.GetSecurityStampAsync(await ContaAsync(users, userId)));
        }

        var protetor = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(provedor);
        return NaUrl(userId, Convert.ToBase64String(protetor.Protect(dados.ToArray())));
    });

    private static string NaUrl(Guid userId, string token)
    {
        var link = LinkDeAcesso.Montar(userId, token);
        return link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];
    }

    private static Task<bool> ValeAsync(HelinhoWebFactory factory, Guid userId, string token) => ComAsync(factory, (_, services) =>
        services.GetRequiredService<DefinirSenhaHandler>().LinkValidoAsync(userId.ToString(), token, Ct));

    private static Task<DefinirSenhaResult> DefinirAsync(HelinhoWebFactory factory, Guid userId, string token, string senha = SenhaNova) =>
        ComAsync(factory, (_, services) => services.GetRequiredService<DefinirSenhaHandler>().HandleAsync(userId.ToString(), token, senha, Ct));

    private static Task<bool> SenhaConfereAsync(HelinhoWebFactory factory, Guid userId, string senha) => ComAsync(factory, async (users, _) =>
        await users.CheckPasswordAsync(await ContaAsync(users, userId), senha));

    [Theory]
    [InlineData(LinkDeAcesso.ProvedorConvite, (71 * 60) + 55, true)]
    [InlineData(LinkDeAcesso.ProvedorConvite, (72 * 60) + 5, false)]
    [InlineData(LinkDeAcesso.ProvedorRedefinicao, 55, true)]
    [InlineData(LinkDeAcesso.ProvedorRedefinicao, 65, false)]
    public async Task Convite_vale_72_horas_e_redefinicao_vale_1_hora(string provedor, int idadeEmMinutos, bool vale)
    {
        var factory = await shared.GetAsync(postgres);
        var conta = await CriarContaAsync(factory, senha: null);

        var token = await EmitirComIdadeAsync(factory, conta.Id, provedor, TimeSpan.FromMinutes(idadeEmMinutos));

        Assert.Equal(vale, await ValeAsync(factory, conta.Id, token));
        Assert.Equal(vale ? DefinirSenhaStatus.Definida : DefinirSenhaStatus.LinkInvalido, (await DefinirAsync(factory, conta.Id, token)).Status);
    }

    [Fact]
    public async Task Convite_define_a_senha_de_conta_sem_senha_e_conferir_nao_gasta_o_link()
    {
        var factory = await shared.GetAsync(postgres);
        var conta = await CriarContaAsync(factory, senha: null);
        var token = await EmitirAsync(factory, conta.Id, LinkDeAcesso.ProvedorConvite);

        Assert.True(await ValeAsync(factory, conta.Id, token));
        Assert.True(await ValeAsync(factory, conta.Id, token));
        Assert.False(await SenhaConfereAsync(factory, conta.Id, SenhaNova));

        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(factory, conta.Id, token)).Status);

        Assert.True(await SenhaConfereAsync(factory, conta.Id, SenhaNova));
        Assert.False(await ValeAsync(factory, conta.Id, token));
        Assert.Equal(DefinirSenhaStatus.LinkInvalido, (await DefinirAsync(factory, conta.Id, token, "Outra-Sintetica-3")).Status);
        Assert.True(await SenhaConfereAsync(factory, conta.Id, SenhaNova));
    }

    [Fact]
    public async Task Redefinicao_troca_a_senha_mata_os_outros_links_da_conta_e_nao_mexe_no_bloqueio()
    {
        var factory = await shared.GetAsync(postgres);
        var conta = await CriarContaAsync(factory, SenhaAntiga);
        var bloqueio = DateTimeOffset.UtcNow.AddHours(2);
        await ComAsync(factory, async (users, _) => await users.SetLockoutEndDateAsync(await ContaAsync(users, conta.Id), bloqueio));
        var usado = await EmitirAsync(factory, conta.Id, LinkDeAcesso.ProvedorRedefinicao);
        var outro = await EmitirAsync(factory, conta.Id, LinkDeAcesso.ProvedorRedefinicao);
        var convite = await EmitirAsync(factory, conta.Id, LinkDeAcesso.ProvedorConvite);

        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(factory, conta.Id, usado)).Status);

        Assert.True(await SenhaConfereAsync(factory, conta.Id, SenhaNova));
        Assert.False(await SenhaConfereAsync(factory, conta.Id, SenhaAntiga));
        foreach (var token in new[] { usado, outro, convite })
        {
            Assert.False(await ValeAsync(factory, conta.Id, token));
        }

        var fim = await ComAsync(factory, async (users, _) => await users.GetLockoutEndDateAsync(await ContaAsync(users, conta.Id)));
        Assert.Equal(bloqueio.ToUnixTimeSeconds(), fim!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Conta_inexistente_token_de_outra_conta_e_token_estranho_dao_o_mesmo_link_invalido()
    {
        var factory = await shared.GetAsync(postgres);
        var conta = await CriarContaAsync(factory, SenhaAntiga);
        var outra = await CriarContaAsync(factory, senha: null);
        var daOutra = await EmitirAsync(factory, outra.Id, LinkDeAcesso.ProvedorConvite);

        var respostas = new[]
        {
            await DefinirAsync(factory, Guid.NewGuid(), daOutra),
            await DefinirAsync(factory, conta.Id, daOutra),
            await DefinirAsync(factory, conta.Id, NaUrl(conta.Id, "nao-e-token")),
        };

        Assert.All(respostas, r => Assert.Equal(DefinirSenhaResult.LinkInvalido, r));
        Assert.True(await SenhaConfereAsync(factory, conta.Id, SenhaAntiga));
        Assert.True(await ValeAsync(factory, outra.Id, daOutra));
    }

    [Fact]
    public async Task Senha_fora_da_politica_e_recusada_com_os_codigos_do_Identity_e_o_link_continua_valendo()
    {
        var factory = await shared.GetAsync(postgres);
        var conta = await CriarContaAsync(factory, senha: null);
        var token = await EmitirAsync(factory, conta.Id, LinkDeAcesso.ProvedorConvite);

        var result = await DefinirAsync(factory, conta.Id, token, "curta");

        Assert.Equal(DefinirSenhaStatus.SenhaRecusada, result.Status);
        Assert.Contains("PasswordTooShort", result.ErrorCodes);
        Assert.True(await ValeAsync(factory, conta.Id, token));
        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(factory, conta.Id, token)).Status);
    }

    [Fact]
    public async Task Token_e_senha_nao_aparecem_em_log_e_a_auditoria_leva_so_o_id_da_conta()
    {
        var factory = await shared.GetAsync(postgres);
        var conta = await CriarContaAsync(factory, senha: null);
        var token = await EmitirAsync(factory, conta.Id, LinkDeAcesso.ProvedorConvite);
        const string senha = "Registro-Sintetico-5";

        await DefinirAsync(factory, conta.Id, token, senha);
        await DefinirAsync(factory, conta.Id, token, senha);

        var linhas = shared.Logs.Linhas.ToArray();
        Assert.Contains(linhas, l => l.Contains($"Senha definida por link de acesso para a conta {conta.Id}", StringComparison.Ordinal));
        Assert.Contains(linhas, l => l.Contains("Link de acesso recusado ao definir senha", StringComparison.Ordinal));
        Assert.DoesNotContain(linhas, l => l.Contains(token, StringComparison.Ordinal)
            || l.Contains(LinkDeAcesso.LerToken(token)!, StringComparison.Ordinal) || l.Contains(senha, StringComparison.Ordinal));
    }
}
