using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Aviso "sua senha foi alterada" (#280, ADR-0003 §11.3): entra na fila junto com a troca de senha. Contas sintéticas.</summary>
[Collection(PostgresCollection.Name)]
public class SenhaAlteradaAvisoTests(PostgresFixture postgres, SenhaAlteradaAvisoTests.Host shared) : IClassFixture<SenhaAlteradaAvisoTests.Host>
{
    private const string SenhaNova = "Nova-Sintetica-7";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um host para a classe inteira (#215); cada teste cria a própria conta.</summary>
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

    private sealed class FilaQuebrada : IJobQueue
    {
        public Task<Guid?> EnqueueAsync(JobRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("fila fora do ar");

        public Task<int> CancelPendingAsync(string type, Guid subjectId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
        var nome = $"s{Guid.NewGuid():N}"[..12];
        var user = new AppUser { UserName = nome, Email = $"{nome}@example.com" };
        Assert.True((senha is null ? await users.CreateAsync(user) : await users.CreateAsync(user, senha)).Succeeded);
        return user;
    });

    private Task<string> EmitirAsync(Guid userId, string provedor) => ComAsync(async (users, _) => LinkDeAcesso.TokenParaUrl(
        await users.GenerateUserTokenAsync(await ContaAsync(users, userId), provedor, LinkDeAcesso.Finalidade)));

    private Task<DefinirSenhaResult> DefinirAsync(Guid userId, string token, string senha = SenhaNova) =>
        ComAsync((_, services) => services.GetRequiredService<DefinirSenhaHandler>().HandleAsync(userId.ToString(), token, senha, Ct));

    private Task<List<QueueJob>> AvisosAsync(Guid userId) => ComAsync((_, services) => services.GetRequiredService<AppDbContext>().QueueJobs
        .AsNoTracking().Where(j => j.SubjectId == userId).OrderBy(j => j.CreatedAt).ToListAsync(Ct));

    private Task<string> ChaveEsperadaAsync(Guid userId) => ComAsync(async (users, _) =>
        $"email.password-changed:{userId:D}:{SecurityStampDigest.Of((await ContaAsync(users, userId)).SecurityStamp ?? "")}");

    [Fact]
    public async Task Cada_troca_de_senha_por_link_enfileira_um_aviso_so_com_referencias()
    {
        var conta = await CriarContaAsync(senha: null);

        // Senha recusada e link inválido não mudam a senha: nada entra na fila.
        var convite = await EmitirAsync(conta.Id, LinkDeAcesso.ProvedorConvite);
        Assert.Equal(DefinirSenhaStatus.SenhaRecusada, (await DefinirAsync(conta.Id, convite, "curta")).Status);
        Assert.Equal(DefinirSenhaStatus.LinkInvalido, (await DefinirAsync(conta.Id, LinkDeAcesso.TokenParaUrl("nao-e-token"))).Status);
        Assert.Empty(await AvisosAsync(conta.Id));

        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(conta.Id, convite)).Status);
        var aviso = Assert.Single(await AvisosAsync(conta.Id));
        Assert.Equal((EmailJobTypes.PasswordChanged, QueueJobStatus.Pending, TimeSpan.FromHours(24)), (aviso.Type, aviso.Status, aviso.ExpiresAt - aviso.CreatedAt));
        Assert.Equal(await ChaveEsperadaAsync(conta.Id), aviso.IdempotencyKey);
        Assert.Equal("""{"v":1}""", JsonSerializer.Serialize(JsonDocument.Parse(aviso.Payload).RootElement));

        // Conta que já tem senha (redefinição): outro aviso, com a chave do carimbo novo.
        var redefinicao = await EmitirAsync(conta.Id, LinkDeAcesso.ProvedorRedefinicao);
        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(conta.Id, redefinicao, "Outra-Sintetica-3")).Status);
        var avisos = await AvisosAsync(conta.Id);
        Assert.Equal(2, avisos.Count);
        Assert.Equal(await ChaveEsperadaAsync(conta.Id), avisos[1].IdempotencyKey);
        Assert.NotEqual(avisos[0].IdempotencyKey, avisos[1].IdempotencyKey);
    }

    [Fact]
    public async Task Se_o_aviso_nao_entra_na_fila_a_senha_nao_muda_e_o_link_continua_valendo()
    {
        var conta = await CriarContaAsync("Antiga-Sintetica-1");
        var link = await EmitirAsync(conta.Id, LinkDeAcesso.ProvedorRedefinicao);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ComAsync((_, services) => new PasswordChangeNotifyingGateway(
            services.GetRequiredService<PasswordLinkGateway>(), services.GetRequiredService<AppDbContext>(), new FilaQuebrada())
            .SetPasswordAsync(conta.Id, LinkDeAcesso.LerToken(link) ?? "", SenhaNova, Ct)));

        Assert.True(await ComAsync(async (users, _) => await users.CheckPasswordAsync(await ContaAsync(users, conta.Id), "Antiga-Sintetica-1")));
        Assert.Equal(DefinirSenhaStatus.Definida, (await DefinirAsync(conta.Id, link)).Status);
        Assert.Single(await AvisosAsync(conta.Id));
    }
}
