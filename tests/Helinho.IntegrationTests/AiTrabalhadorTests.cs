using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Ai;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>
/// Issue #298: o tratador da IA dentro do processador real da fila (ADR-0004 §10.2 e §14; ADR-0003 §6). O pedido nasce
/// pela tela, com um administrador; quem processa é o <see cref="IJobProcessor"/>, no escopo do item, sem usuário.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class AiTrabalhadorTests(PostgresFixture postgres, AiHostDeTeste host) : IClassFixture<AiHostDeTeste>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Token();

    /// <summary>Pede um lote pela tela, com um administrador novo (cota e chave de pedido próprias), e devolve o id do pedido.</summary>
    private static async Task<Guid> RequestAsync(HelinhoWebFactory factory, QuizType type, AgeBand band)
    {
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var token = Token().Match(await client.GetStringAsync("/Questoes/Ia", Ct)).Groups[1].Value;
        using var response = await client.PostAsync("/Questoes/Ia", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.QuizType"] = type.ToString(),
            ["Input.AgeBand"] = band.ToString(),
            ["Input.Count"] = "5",
            ["__RequestVerificationToken"] = token,
        }), Ct);
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        return Guid.Parse(response.Headers.Location!.OriginalString.Split('/')[^1]);
    }

    private static async Task<(AiGenerationJob Request, QueueJob Job, int Suggestions)> ReadAsync(HelinhoWebFactory factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.AiGenerationJobs.AsNoTracking().SingleAsync(j => j.Id == id, Ct);
        return (request, await db.QueueJobs.AsNoTracking().SingleAsync(j => j.SubjectId == request.RequestedByUserId, Ct),
            await db.AiQuestionSuggestions.CountAsync(s => s.JobId == id, Ct));
    }

    [Fact]
    public async Task Raia_da_IA_nasce_do_registro_real_com_a_politica_do_ADR_e_cabe_na_posse_nos_extremos_do_prazo()
    {
        var factory = await host.GetAsync(postgres); // o host subiu: o trabalhador conferiu as raias (BatchSize × ItemTimeout ≤ Lease)
        host.Configure();

        var lane = Assert.Single(await host.LanesAsync(), l => l.Types.Contains(RequestAiDrafts.JobType));

        Assert.Equal([RequestAiDrafts.JobType], lane.Types);
        Assert.Equal((2, 1, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150)),
            (lane.Policy.MaxAttempts, lane.Policy.BatchSize, lane.Policy.ItemTimeout, lane.Policy.Lease));
        foreach (var seconds in new[] { AiOptions.TimeoutMin, AiOptions.TimeoutMax })
        {
            host.Configure(timeout: seconds);
            var policy = (await host.LanesAsync()).Single(l => l.Types.Contains(RequestAiDrafts.JobType)).Policy; // From lançaria se não coubesse
            Assert.Equal(TimeSpan.FromSeconds(seconds + 60), policy.Lease);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated); // o escopo do item não tem usuário
        // O trabalhador resolve o tratador na subida: as opções dele e as da porta são a mesma instância, validada uma vez.
        Assert.Same(factory.Services.GetRequiredService<IOptionsMonitor<AiOptions>>().CurrentValue, scope.ServiceProvider.GetRequiredService<IOptions<AiOptions>>().Value);
    }

    [Fact]
    public async Task Pedido_da_tela_e_processado_pelo_processador_real_e_sugestoes_e_conclusao_saem_juntas()
    {
        var factory = await host.GetAsync(postgres);
        host.Configure();
        var id = await RequestAsync(factory, QuizType.Cultural, AgeBand.Age8To10);
        var queued = await ReadAsync(factory, id);

        var first = await host.ProcessAsync(Ct);
        var second = await host.ProcessAsync(Ct);

        var (request, job, suggestions) = await ReadAsync(factory, id);
        Assert.Equal((AiJobStatus.Pending, QueueJobStatus.Pending, 0), (queued.Request.Status, queued.Job.Status, queued.Suggestions));
        Assert.Equal((1, 0), (first.Captured, second.Captured));
        Assert.Equal((AiJobStatus.Succeeded, 5, 5, FakeQuestionDraftGenerator.Provider), (request.Status, request.ValidCount, suggestions, request.Provider));
        Assert.Equal((QueueJobStatus.Succeeded, 1, null), (job.Status, job.Attempts, job.LastError));
        Assert.Equal([typeof(FakeQuestionDraftGenerator)], host.Probe.Generators);
    }

    [Fact]
    public async Task Tempo_maximo_do_item_vira_ai_timeout_e_a_fila_nao_repete()
    {
        var factory = await host.GetAsync(postgres);
        host.Configure(failure: FakeAiFailure.Timeout);
        var id = await RequestAsync(factory, QuizType.Esportivo, AgeBand.Age8To10);

        var pass = host.ProcessAsync(Ct);
        await host.Probe.Entered.WaitAsync(Ct); // o tratador já está esperando o gerador
        host.Time.Advance(TimeSpan.FromSeconds(90)); // tempo máximo do item: o processador cancela o token
        await pass;
        var again = await host.ProcessAsync(Ct);

        var (request, job, suggestions) = await ReadAsync(factory, id);
        Assert.Equal((AiJobStatus.Failed, AiFailureCodes.Timeout, 0), (request.Status, request.FailureReason, suggestions));
        Assert.Equal((QueueJobStatus.Failed, 1, AiFailureCodes.Timeout), (job.Status, job.Attempts, job.LastError));
        Assert.Equal(0, again.Captured);
    }

    [Fact]
    public async Task Trabalho_vencido_e_encerrado_pela_fila_e_o_gancho_tira_o_pedido_de_na_fila()
    {
        var factory = await host.GetAsync(postgres);
        host.Configure();
        var id = await RequestAsync(factory, QuizType.Conhecimento, AgeBand.Age8To10);

        host.Time.Advance(RequestAiDrafts.ExpiresIn + TimeSpan.FromSeconds(1)); // ninguém processou em 10 minutos
        await host.ProcessAsync(Ct);

        var (request, job, _) = await ReadAsync(factory, id);
        Assert.Equal((AiJobStatus.Failed, AiFailureCodes.Expired), (request.Status, request.FailureReason));
        Assert.Equal((QueueJobStatus.Failed, AiFailureCodes.Expired), (job.Status, job.LastError));
        Assert.Empty(host.Probe.Generators); // vencido não paga chamada
    }

    /// <summary>
    /// §14 diz "nenhum trabalhador de IA roda": a raia continua registrada (a #228 monta uma por tratador), mas com a IA
    /// desligada ela só encerra o que sobrou na fila. A porta resolve o gerador desligado, que recusa sem tocar a rede.
    /// </summary>
    [Fact]
    public async Task Com_a_IA_desligada_o_trabalho_que_sobrou_e_encerrado_com_ai_disabled_sem_chamar_gerador_de_verdade()
    {
        var factory = await host.GetAsync(postgres);
        host.Configure();
        var id = await RequestAsync(factory, QuizType.Ambiental, AgeBand.Age8To10);

        host.Configure(AiOptions.Off);
        await host.ProcessAsync(Ct);

        var (request, job, suggestions) = await ReadAsync(factory, id);
        Assert.Equal((AiJobStatus.Failed, "ai_disabled", 0), (request.Status, request.FailureReason, suggestions));
        Assert.Equal((QueueJobStatus.Failed, 1, "ai_disabled"), (job.Status, job.Attempts, job.LastError));
        Assert.Equal([typeof(DisabledQuestionDraftGenerator)], host.Probe.Generators);
        Assert.Contains(await host.LanesAsync(), l => l.Types.Contains(RequestAiDrafts.JobType)); // divergência registrada no handoff
    }
}
