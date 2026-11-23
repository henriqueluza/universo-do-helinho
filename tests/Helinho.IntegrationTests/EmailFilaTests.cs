using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Email;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Tratador de e-mail (#272) com o trabalhador de verdade (#228): fila, captura por raia, gravação final e suspensão
/// pelo teto diário (ADR-0003 §6 e §15). Só o envio é um capturador. Contas e alunos sintéticos.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EmailFilaTests(PostgresFixture postgres) : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Add(message);
        return Task.FromResult(new EmailSendResult(EmailSendStatus.Sent, $"{message.IdempotencyKey}@example.org"));
    }

    private static async Task<Guid> EnfileirarAsync(IServiceProvider services, string type, bool aluno)
    {
        await using var scope = services.CreateAsyncScope();
        var (users, db, tag) = (scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), $"{Guid.NewGuid():N}"[..10]);
        var unit = new Unit($"Unidade {tag} (fictícia)");
        db.Units.Add(unit);
        var user = new AppUser { UserName = $"t{tag}", Email = aluno ? null : $"t{tag}@example.com", UnitId = unit.Id };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, aluno ? RoleNames.Student : RoleNames.Admin)).Succeeded);
        var student = new Student(user.Id, unit.Id, $"M{tag}", "Ana Sintética Souza", $"rg{tag}", $"cpf{tag}", "Rua Fictícia, 1",
            new DateOnly(2016, 5, 5), "Responsável Sintético", "5A", new DateOnly(2026, 11, 22));
        student.SetGuardianEmail($"resp{tag}@example.com");
        student.ApproveByGuardian();
        if (aluno)
        {
            db.Students.Add(student);
        }

        var id = await scope.ServiceProvider.GetRequiredService<IJobQueue>()
            .EnqueueAsync(new JobRequest(type, aluno ? student.Id : user.Id, new JobPayload(), TimeSpan.FromHours(24)), Ct);
        await db.SaveChangesAsync(Ct);
        return id ?? throw new InvalidOperationException("Trabalho de teste não enfileirado.");
    }

    [Fact]
    public async Task Fila_entrega_cada_tipo_ao_registro_certo_e_o_teto_diario_suspende_sem_gastar_tentativa()
    {
        // Teto de 2: os demais param em 1 (80%), os urgentes em 2.
        await using var factory = new HelinhoWebFactory(postgres, configure: services => services
            .AddSingleton<IEmailSender>(this)
            .PostConfigure<EmailOptions>(o => o.PublicBaseUrl = "https://painel.example.org")
            .Configure<EmailDailyLimitOptions>(o => o.Value = "2"));
        await factory.EnsureDatabaseAsync();
        var services = factory.Services;
        IReadOnlyList<JobLane> lanes;
        await using (var scope = services.CreateAsyncScope())
        {
            lanes = JobLane.From(scope.ServiceProvider.GetServices<IJobHandler>()); // o que o trabalhador faz na subida
        }

        var urgent = lanes.Single(l => l.Types.Contains(EmailJobTypes.AdminInvite));
        var others = lanes.Single(l => l.Types.Contains(EmailJobTypes.StudentAccess));
        var processor = services.GetRequiredService<IJobProcessor>();
        var access1 = await EnfileirarAsync(services, EmailJobTypes.StudentAccess, aluno: true);
        var access2 = await EnfileirarAsync(services, EmailJobTypes.StudentAccess, aluno: true);
        var invite1 = await EnfileirarAsync(services, EmailJobTypes.AdminInvite, aluno: false);
        var invite2 = await EnfileirarAsync(services, EmailJobTypes.AdminInvite, aluno: false);

        var othersBatch = await processor.ProcessBatchAsync(others, CancellationToken.None, Ct);
        var urgentBatch = await processor.ProcessBatchAsync(urgent, CancellationToken.None, Ct);

        await using var read = services.CreateAsyncScope();
        var rows = await read.ServiceProvider.GetRequiredService<AppDbContext>().QueueJobs.AsNoTracking().ToDictionaryAsync(j => j.Id, Ct);
        Assert.Equal(2, lanes.Count);
        Assert.Equal((2, 2), (othersBatch.Captured, urgentBatch.Captured));

        // O primeiro acesso e o primeiro convite saem: ao chegar a vez do convite, só 1 de 2 tinha saído.
        Assert.Equal([access1, invite1], _sent.Select(m => m.IdempotencyKey ?? Guid.Empty));
        Assert.All([access1, invite1], id => Assert.Equal(
            (QueueJobStatus.Succeeded, $"{id}@example.org", 1), (rows[id].Status, rows[id].ExternalId, rows[id].Attempts)));
        Assert.NotNull(rows[access1].FinishedAt);

        // O segundo de cada raia espera a hora seguinte: pendente, com a tentativa devolvida e o código fechado.
        Assert.All([access2, invite2], id => Assert.Equal(
            (QueueJobStatus.Pending, EmailDailyLimit.Code, 0, null), (rows[id].Status, rows[id].LastError, rows[id].Attempts, rows[id].ExternalId)));
        Assert.All([othersBatch, urgentBatch], batch => Assert.InRange(batch.PausedUntil ?? default, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
    }
}
