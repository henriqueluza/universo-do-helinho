using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Reenviar e cancelar convite de administrador (#268, ADR-0003 §11.2). Contas sintéticas; cada teste cria a sua unidade.</summary>
[Collection(PostgresCollection.Name)]
public class PanelConvitesTests(PostgresFixture postgres, DefinirSenhaLinkTests.Host shared) : IClassFixture<DefinirSenhaLinkTests.Host>
{
    private const string Novo = "/Administradores/Novo"; // sempre tem formulário: fonte do token antiforgery

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> ComAsync<T>(Func<UserManager<AppUser>, AppDbContext, Task<T>> acao)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope(); // um host para a classe (#215)
        return await acao(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<string> PostAsync(HttpClient client, string formPage, string action, Dictionary<string, string>? fields = null)
    {
        var html = await (await client.GetAsync(formPage, Ct)).Content.ReadAsStringAsync(Ct);
        fields ??= [];
        fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync(action, new FormUrlEncodedContent(fields), Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return WebUtility.HtmlDecode(await (await client.GetAsync(response.Headers.Location, Ct)).Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Cria a unidade e convida o administrador pela tela, como master.</summary>
    private async Task<(HttpClient Client, AppUser User, Guid UnitId)> ConvidarAsync(string user)
    {
        var client = (await shared.GetAsync(postgres)).CreateFormClientAs(RoleNames.Master);
        var unitId = await ComAsync(async (_, db) =>
        {
            var unit = db.Units.Add(new Unit($"Unidade {user}")).Entity;
            await db.SaveChangesAsync(Ct);
            return unit.Id;
        });
        await PostAsync(client, Novo, Novo, Form(user, unitId));
        return (client, await ContaAsync(user) ?? throw new InvalidOperationException("Conta de teste não criada."), unitId);
    }

    private static Dictionary<string, string> Form(string user, Guid unitId) =>
        new() { ["Input.UserName"] = user, ["Input.Email"] = $"{user}@example.com", ["Input.UnitId"] = unitId.ToString() };

    private static string Reenviar(Guid accountId) => $"/Administradores?id={accountId}&handler=Reenviar";

    private static string Cancelar(Guid accountId) => $"/Administradores/CancelarConvite/{accountId}";

    private Task<AppUser?> ContaAsync(string user) => ComAsync((users, _) => users.FindByNameAsync(user));

    private async Task<string?> CarimboAsync(string user) => (await ContaAsync(user))?.SecurityStamp;

    private Task<List<QueueJobStatus>> ConvitesAsync(Guid accountId) => ComAsync((_, db) =>
        db.QueueJobs.AsNoTracking().Where(j => j.SubjectId == accountId).OrderBy(j => j.CreatedAt).Select(j => j.Status).ToListAsync(Ct));

    [Fact]
    public async Task Reenviar_invalida_o_link_anterior_cancela_o_pendente_e_para_no_limite_de_tres_por_hora()
    {
        var convite = await ConvidarAsync("adm.reenvio");
        using var client = convite.Client;

        var lista = await PostAsync(client, Novo, Reenviar(convite.User.Id));

        Assert.Contains("Novo convite na fila de envio. O link anterior deixou de valer.", lista);
        Assert.Contains("Reenviar convite<span class=\"visually-hidden\"> para adm.reenvio</span>", lista);
        Assert.Contains("Cancelar convite<span class=\"visually-hidden\"> de adm.reenvio</span>", lista);
        Assert.Equal([QueueJobStatus.Cancelled, QueueJobStatus.Pending], await ConvitesAsync(convite.User.Id));
        Assert.NotEqual(convite.User.SecurityStamp, await CarimboAsync("adm.reenvio"));

        await PostAsync(client, Novo, Reenviar(convite.User.Id)); // terceiro convite da hora: o da criação conta
        var carimbo = await CarimboAsync("adm.reenvio");
        var recusado = await PostAsync(client, Novo, Reenviar(convite.User.Id));

        Assert.Contains("Esta conta já teve 3 convites na última hora.", recusado);
        Assert.Contains("class=\"note\" role=\"alert\"", recusado);
        Assert.Equal(carimbo, await CarimboAsync("adm.reenvio"));
        Assert.Equal([QueueJobStatus.Cancelled, QueueJobStatus.Cancelled, QueueJobStatus.Pending], await ConvitesAsync(convite.User.Id));
    }

    [Fact]
    public async Task Reenviar_logo_depois_de_um_convite_que_saiu_desfaz_a_troca_do_carimbo()
    {
        var convite = await ConvidarAsync("adm.janela");
        using var client = convite.Client;
        await ComAsync((_, db) => db.Database.ExecuteSqlAsync(
            $"""UPDATE "QueueJobs" SET "Status" = 'Succeeded', "FinishedAt" = now() WHERE "SubjectId" = {convite.User.Id}""", Ct));

        var lista = await PostAsync(client, Novo, Reenviar(convite.User.Id));

        Assert.Contains("Um convite acabou de sair para esta conta.", lista);
        Assert.Equal(convite.User.SecurityStamp, await CarimboAsync("adm.janela"));
        Assert.Equal([QueueJobStatus.Succeeded], await ConvitesAsync(convite.User.Id));
    }

    [Fact]
    public async Task Cancelar_pede_confirmacao_apaga_a_conta_cancela_o_pendente_e_libera_a_unidade()
    {
        var convite = await ConvidarAsync("adm.cancelado");
        using var client = convite.Client;
        var pagina = Cancelar(convite.User.Id);

        var confirmacao = await (await client.GetAsync(pagina, Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("<b>adm.cancelado</b>", confirmacao);
        // O foco inicial fica na saída segura, nunca no botão que apaga a conta.
        Assert.Contains("<a class=\"btn\" href=\"/Administradores\" autofocus>Voltar sem cancelar</a>", confirmacao);
        Assert.DoesNotMatch("<button[^>]*autofocus", confirmacao);
        Assert.NotNull(await ContaAsync("adm.cancelado")); // abrir a confirmação não apaga nada

        var lista = await PostAsync(client, pagina, pagina);

        Assert.Contains("Convite cancelado e conta apagada.", lista);
        Assert.DoesNotContain("adm.cancelado", lista);
        Assert.Null(await ContaAsync("adm.cancelado"));
        Assert.Equal([QueueJobStatus.Cancelled], await ConvitesAsync(convite.User.Id));
        var substituto = await PostAsync(client, Novo, Novo, Form("adm.substituto", convite.UnitId));
        Assert.Contains("adm.substituto", substituto);
        Assert.Contains("Administrador criado. O convite está na fila de envio.", substituto);
    }

    [Fact]
    public async Task Conta_que_ja_tem_senha_nao_tem_acoes_e_recusa_reenvio_e_cancelamento()
    {
        var convite = await ConvidarAsync("adm.ativo");
        using var client = convite.Client;
        Assert.True(await ComAsync(async (users, _) =>
            await users.FindByNameAsync("adm.ativo") is { } conta && (await users.AddPasswordAsync(conta, "Senha-Sintetica-9x")).Succeeded));
        var carimbo = await CarimboAsync("adm.ativo");

        var reenvio = await PostAsync(client, Novo, Reenviar(convite.User.Id));
        var cancelamento = await PostAsync(client, Novo, Cancelar(convite.User.Id));

        Assert.Contains("Este convite não está mais em aberto.", reenvio);
        Assert.Contains("Este convite não está mais em aberto.", cancelamento);
        Assert.DoesNotContain("para adm.ativo</span>", cancelamento);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Cancelar(convite.User.Id), Ct)).StatusCode);
        Assert.Equal(carimbo, await CarimboAsync("adm.ativo"));
        Assert.Equal([QueueJobStatus.Pending], await ConvitesAsync(convite.User.Id));
    }

    [Fact]
    public async Task So_o_master_reenvia_ou_cancela_e_sem_antiforgery_nada_acontece()
    {
        var convite = await ConvidarAsync("adm.protegido");
        using var master = convite.Client;
        using var admin = (await shared.GetAsync(postgres)).CreateFormClientAs(RoleNames.Admin, convite.UnitId);
        using var vazio = new FormUrlEncodedContent(new Dictionary<string, string>());

        foreach (var action in new[] { Reenviar(convite.User.Id), Cancelar(convite.User.Id) })
        {
            var negado = await admin.PostAsync(action, vazio, Ct);
            Assert.Equal("/acesso-negado", new Uri(new Uri("http://localhost"), negado.Headers.Location!).AbsolutePath);
            Assert.Equal(HttpStatusCode.BadRequest, (await master.PostAsync(action, vazio, Ct)).StatusCode);
        }

        Assert.Equal(convite.User.SecurityStamp, await CarimboAsync("adm.protegido"));
        Assert.Equal([QueueJobStatus.Pending], await ConvitesAsync(convite.User.Id));
    }
}
