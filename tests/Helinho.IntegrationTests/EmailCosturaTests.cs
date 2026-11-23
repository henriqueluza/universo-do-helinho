using System.Net;
using System.Text;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Costura das fatias de e-mail (#291): o tratador (#270) com o processador real da fila (#228), o serviço que apaga o
/// endereço anterior do responsável (#267) e a rota do webhook (#265). Só o envio é um capturador. Dados sintéticos.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EmailCosturaTests(PostgresFixture postgres, EmailCosturaTests.Host shared) : IClassFixture<EmailCosturaTests.Host>
{
    /// <summary>Gerado a cada execução: nenhum segredo fica escrito no repositório.</summary>
    private static readonly string Secret = $"{Guid.NewGuid():N}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um host para a classe inteira (#215). A resposta do provedor é escolhida por trabalho.</summary>
    public sealed class Host : IAsyncDisposable
    {
        private HelinhoWebFactory? _root;
        private WebApplicationFactory<Program>? _factory;

        public EmailCapturado Email { get; } = new();

        public Dictionary<Guid, EmailSendResult> Respostas { get; } = [];

        internal async Task<WebApplicationFactory<Program>> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                Email.Resposta = m => m.IdempotencyKey is { } id && Respostas.TryGetValue(id, out var resposta) ? resposta : null;
                _root = new HelinhoWebFactory(postgres, configure: Email.Registrar);
                var factory = _root.WithWebHostBuilder(b => b.UseSetting("Brevo:WebhookSecret", Secret));
                await using var scope = factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(CancellationToken.None);
                _factory = factory;
            }

            return _factory;
        }

        public ValueTask DisposeAsync() => _root?.DisposeAsync() ?? ValueTask.CompletedTask; // leva junto a fábrica derivada
    }

    private async Task<T> ComAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider);
    }

    /// <summary>Conta numa unidade nova e um trabalho do <paramref name="type"/> na fila. Com aluno, o e-mail do responsável já foi trocado.</summary>
    private Task<(Guid Job, AppUser Conta, Guid StudentId, string Anterior)> EnfileirarAsync(string type, bool aluno) => ComAsync(async services =>
    {
        var (users, db, tag) = (services.GetRequiredService<UserManager<AppUser>>(), services.GetRequiredService<AppDbContext>(), $"{Guid.NewGuid():N}"[..10]);
        var unit = new Unit($"Unidade {tag} (fictícia)");
        db.Units.Add(unit);
        await db.SaveChangesAsync(Ct);
        var user = new AppUser { UserName = $"t{tag}", Email = aluno ? null : $"t{tag}@example.com", UnitId = unit.Id };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, aluno ? RoleNames.Student : RoleNames.Admin)).Succeeded);
        var student = new Student(user.Id, unit.Id, $"M{tag}", "Ana Sintética Souza", $"rg{tag}", $"cpf{tag}", "Rua Fictícia, 1",
            new DateOnly(2016, 5, 5), "Responsável Sintético", "5A", new DateOnly(2026, 11, 22));
        student.SetGuardianEmail($"novo{tag}@example.com");
        student.KeepPreviousGuardianEmail($"anterior{tag}@example.com");
        student.ApproveByGuardian();
        if (aluno)
        {
            db.Students.Add(student);
        }

        var job = await services.GetRequiredService<IJobQueue>()
            .EnqueueAsync(new JobRequest(type, aluno ? student.Id : user.Id, new JobPayload(), TimeSpan.FromHours(24)), Ct);
        await db.SaveChangesAsync(Ct);
        return (job ?? throw new InvalidOperationException("Trabalho de teste não enfileirado."), user, student.Id, student.PreviousGuardianEmail!);
    });

    private async Task<QueueJob> ProcessarAsync(Guid job)
    {
        await EmailCapturado.ProcessarFilaAsync((await shared.GetAsync(postgres)).Services, Ct);
        return await ComAsync(services => services.GetRequiredService<AppDbContext>().QueueJobs.AsNoTracking().SingleAsync(j => j.Id == job, Ct));
    }

    // ignora-filtro: a conferência do teste roda sem usuário de painel, como o trabalhador.
    private Task<string?> EnderecoAnteriorAsync(Guid studentId) => ComAsync(services => services.GetRequiredService<AppDbContext>()
        .Students.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == studentId).Select(s => s.PreviousGuardianEmail).SingleAsync(Ct));

    [Theory]
    [InlineData("enviado", QueueJobStatus.Succeeded, null, true)]
    [InlineData("recusado", QueueJobStatus.Failed, "invalid_parameter", true)]
    [InlineData("vencido", QueueJobStatus.Failed, "expired", true)] // a fila encerra sem chamar o tratador: vale o gancho de abandono
    [InlineData("tentar-de-novo", QueueJobStatus.Pending, "http_500", false)] // o aviso ainda vai sair: o endereço fica
    public async Task Aviso_de_troca_vai_ao_endereco_anterior_e_o_endereco_e_apagado_na_gravacao_que_encerra_o_trabalho(
        string cenario, QueueJobStatus status, string? erro, bool apagado)
    {
        var (job, _, studentId, anterior) = await EnfileirarAsync(EmailJobTypes.GuardianEmailChanged, aluno: true);
        switch (cenario)
        {
            case "recusado": shared.Respostas[job] = new(EmailSendStatus.PermanentFailure, ErrorCode: erro); break;
            case "tentar-de-novo": shared.Respostas[job] = new(EmailSendStatus.TransientFailure, ErrorCode: erro); break;
            case "vencido":
                await ComAsync(services => services.GetRequiredService<AppDbContext>().QueueJobs.Where(j => j.Id == job)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
                break;
        }

        var row = await ProcessarAsync(job);

        Assert.Equal((status, erro), (row.Status, row.LastError));
        Assert.Equal(apagado ? null : anterior, await EnderecoAnteriorAsync(studentId));
        var enviados = shared.Email.Enviados.Where(m => m.IdempotencyKey == job).ToList();
        Assert.Equal(cenario == "vencido" ? 0 : 1, enviados.Count);
        Assert.All(enviados, m => Assert.Equal((anterior, EmailJobTypes.GuardianEmailChanged), (m.Recipient, m.Tag)));
        var novo = anterior.Replace("anterior", "novo", StringComparison.Ordinal);
        Assert.All(enviados, m => Assert.DoesNotContain(novo, m.TextBody + m.HtmlBody, StringComparison.Ordinal)); // o endereço novo não vai no aviso
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Webhook_acha_o_trabalho_que_o_tratador_enviou_com_o_id_entre_sinais_ou_sem_eles(bool eventoComSinais)
    {
        var (job, _, _, _) = await EnfileirarAsync(EmailJobTypes.PasswordReset, aluno: false);
        var id = EmailCapturado.IdDaMensagem(job);

        var row = await ProcessarAsync(job); // o capturador devolve o id entre < >, como nos cabeçalhos
        using var request = new HttpRequestMessage(HttpMethod.Post, BrevoWebhookArea.Route)
        {
            Content = new StringContent(
                $$"""{"event":"hard_bounce","message-id":"{{(eventoComSinais ? $"<{id}>" : id)}}"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(BrevoWebhookArea.SecretHeader, Secret);
        using var response = await (await shared.GetAsync(postgres)).CreateDefaultClient().SendAsync(request, Ct);

        Assert.Equal((QueueJobStatus.Succeeded, id), (row.Status, row.ExternalId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var feedback = await ComAsync(services => services.GetRequiredService<AppDbContext>().QueueJobs.Where(j => j.Id == job).Select(j => j.Feedback).SingleAsync(Ct));
        Assert.Equal(EmailFeedback.HardBounce, feedback);
    }

    [Theory]
    [InlineData("cancelado", "subject_missing")] // o cancelamento apaga a conta sem senha (#268)
    [InlineData("ja-tem-senha", "not_eligible")]
    public async Task Convite_cancelado_ou_de_conta_que_ja_tem_senha_falha_na_fila_sem_enviar(string cenario, string erro)
    {
        var (job, conta, _, _) = await EnfileirarAsync(EmailJobTypes.AdminInvite, aluno: false);
        await ComAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(conta.Id.ToString()) ?? throw new InvalidOperationException("Conta de teste não encontrada.");
            return cenario == "cancelado" ? await users.DeleteAsync(user) : await users.AddPasswordAsync(user, "Senha-Sintetica-1");
        });

        var row = await ProcessarAsync(job);

        Assert.Equal((QueueJobStatus.Failed, erro, 1), (row.Status, row.LastError, row.Attempts));
        Assert.DoesNotContain(shared.Email.Enviados, m => m.IdempotencyKey == job);
    }
}
