using Helinho.Application.Administradores;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Reenviar e cancelar convite sobre o banco (#268, ADR-0003 §11.2): carimbo, fila e conta na mesma transação. Contas sintéticas.</summary>
[Collection(PostgresCollection.Name)]
public class ConviteAbertoTests(PostgresFixture postgres, DefinirSenhaLinkTests.Host shared) : IClassFixture<DefinirSenhaLinkTests.Host>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> ComAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope(); // um host para a classe (#215)
        return await acao(scope.ServiceProvider);
    }

    /// <summary>Cria a unidade e o administrador sem senha pelo caso de uso; o primeiro convite fica pendente na fila.</summary>
    private Task<Guid> ConvidarAsync(string user) => ComAsync(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        var unit = db.Units.Add(new Unit($"Unidade {user}")).Entity;
        await db.SaveChangesAsync(Ct);
        var account = new NewAdminAccount(user, $"{user}@example.com", unit.Id);
        Assert.Equal(CriarAdministradorStatus.Created, (await services.GetRequiredService<CriarAdministradorHandler>().HandleAsync(account, Ct)).Status);
        return account.Id;
    });

    private Task<ConviteStatus> ReenviarAsync(Guid id) => ComAsync(s => s.GetRequiredService<ConviteAbertoHandler>().ReenviarAsync(id, Ct));

    private Task<ConviteStatus> CancelarAsync(Guid id) => ComAsync(s => s.GetRequiredService<ConviteAbertoHandler>().CancelarAsync(id, Ct));

    private Task<string?> CarimboAsync(Guid id) => ComAsync(async s =>
        (await s.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(id.ToString()))?.SecurityStamp);

    private Task<List<QueueJobStatus>> ConvitesAsync(Guid id) => ComAsync(s => s.GetRequiredService<AppDbContext>().QueueJobs.AsNoTracking()
        .Where(j => j.SubjectId == id).OrderBy(j => j.CreatedAt).Select(j => j.Status).ToListAsync(Ct));

    [Fact]
    public async Task Reenviar_troca_o_carimbo_cancela_o_pendente_e_para_no_limite_de_tres_por_hora()
    {
        var id = await ConvidarAsync("adm.reenvio");
        var original = await CarimboAsync(id);

        Assert.Equal(ConviteStatus.Done, await ReenviarAsync(id));
        Assert.NotEqual(original, await CarimboAsync(id));
        Assert.Equal([QueueJobStatus.Cancelled, QueueJobStatus.Pending], await ConvitesAsync(id));

        Assert.Equal(ConviteStatus.Done, await ReenviarAsync(id)); // terceiro convite da hora: o da criação conta
        var carimbo = await CarimboAsync(id);
        Assert.Equal(ConviteStatus.RateLimited, await ReenviarAsync(id));
        Assert.Equal(carimbo, await CarimboAsync(id));
        Assert.Equal([QueueJobStatus.Cancelled, QueueJobStatus.Cancelled, QueueJobStatus.Pending], await ConvitesAsync(id));
    }

    [Fact]
    public async Task Reenviar_logo_depois_de_um_convite_que_saiu_desfaz_a_troca_do_carimbo()
    {
        var id = await ConvidarAsync("adm.janela");
        var original = await CarimboAsync(id);
        await ComAsync(s => s.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(
            $"""UPDATE "QueueJobs" SET "Status" = 'Succeeded', "FinishedAt" = now() WHERE "SubjectId" = {id}""", Ct));

        Assert.Equal(ConviteStatus.JustSent, await ReenviarAsync(id));

        Assert.Equal(original, await CarimboAsync(id)); // o link que acabou de sair continua valendo
        Assert.Equal([QueueJobStatus.Succeeded], await ConvitesAsync(id));
    }

    [Fact]
    public async Task Cancelar_apaga_so_a_conta_sem_senha_e_cancela_o_convite_pendente()
    {
        var id = await ConvidarAsync("adm.cancelado");
        var comSenha = await ConvidarAsync("adm.ativo");
        Assert.True(await ComAsync(async s => s.GetRequiredService<UserManager<AppUser>>() is var users
            && await users.FindByIdAsync(comSenha.ToString()) is { } conta && (await users.AddPasswordAsync(conta, "Senha-Sintetica-9x")).Succeeded));

        Assert.Equal(ConviteStatus.Done, await CancelarAsync(id));
        Assert.Equal(ConviteStatus.NotInvited, await CancelarAsync(comSenha));
        Assert.Equal(ConviteStatus.NotInvited, await ReenviarAsync(comSenha));

        Assert.Null(await CarimboAsync(id)); // conta apagada
        Assert.Equal([QueueJobStatus.Cancelled], await ConvitesAsync(id));
        Assert.NotNull(await CarimboAsync(comSenha));
        Assert.Equal([QueueJobStatus.Pending], await ConvitesAsync(comSenha));
    }
}
