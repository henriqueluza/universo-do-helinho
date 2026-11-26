using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Gatilho da aprovação (#267, ADR-0003 §11.4 e D12): aprovar o aluno enfileira o acesso, só para conta sem senha. Sem host; dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public class StudentApprovalAccessTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AccessType = "email.student-access";
    private readonly string _database = $"aprovacao_{Guid.NewGuid():N}";
    private readonly MutableClock _clock = new();
    private readonly Unit _unit = new("Unidade A (fictícia)");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Admin(Guid? UnitId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public bool IsInRole(string role) => role == RoleNames.Admin;
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    }

    public async ValueTask InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        db.Roles.Add(new IdentityRole<Guid>(RoleNames.Student) { NormalizedName = "STUDENT" });
        db.Units.Add(_unit);
        await db.SaveChangesAsync(Ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new Admin(_unit.Id));

    private StudentAccessRequests Requests(AppDbContext db) =>
        new(new JobQueue(db, _clock), new StudentAccessJobs(db), new StudentAccounts(db), _clock);

    private static StudentForm Form(bool approved) =>
        new("Ana Souza", "22.222.222-2", "529.982.247-25", "Rua Sintética, 2", new DateOnly(2018, 5, 20), "Maria Souza", "maria.souza@example.org", "4B", approved);

    private async Task<SaveStudentResult> CreateAsync(bool approved)
    {
        await using var db = NewContext();
        return await SaveStudent.CreateAsync(db, new Admin(_unit.Id), _clock, new StudentAccounts(db), Requests(db), Form(approved), Ct);
    }

    private async Task<SaveStudentResult> UpdateAsync(Guid studentId, bool approved)
    {
        await using var db = NewContext();
        return Assert.IsType<SaveStudentResult>(await SaveStudent.UpdateAsync(db, _clock, Requests(db), studentId, Form(approved), Ct));
    }

    /// <summary>Os pedidos de acesso do aluno e a aprovação gravada.</summary>
    private async Task<(List<QueueJob> Jobs, bool Approved)> ReadAsync(Guid studentId)
    {
        await using var db = NewContext();
        var jobs = await db.QueueJobs.AsNoTracking().Where(j => j.SubjectId == studentId).OrderBy(j => j.CreatedAt).ToListAsync(Ct);
        Assert.All(jobs, j => Assert.Equal((AccessType, QueueJobStatus.Pending), (j.Type, j.Status)));
        return (jobs, await db.Students.Where(s => s.Id == studentId).Select(s => s.GuardianApproved).SingleAsync(Ct));
    }

    [Fact]
    public async Task Cadastro_ja_aprovado_enfileira_o_acesso_e_cadastro_pendente_nao()
    {
        var approved = await CreateAsync(approved: true);
        var pending = await CreateAsync(approved: false);

        var job = Assert.Single((await ReadAsync(approved.StudentId!.Value)).Jobs);
        Assert.True(approved.AccessQueued);
        Assert.Equal(_clock.UtcNow.AddHours(72), job.ExpiresAt);
        Assert.False(pending.AccessQueued);
        Assert.Empty((await ReadAsync(pending.StudentId!.Value)).Jobs);
    }

    [Fact]
    public async Task Aprovar_na_edicao_enfileira_o_acesso_na_mesma_gravacao_e_salvar_de_novo_nao_repete()
    {
        var id = (await CreateAsync(approved: false)).StudentId!.Value;

        var approving = await UpdateAsync(id, approved: true);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(5); // fora da janela de idempotência: a recusa abaixo é da regra, não da chave
        var unchanged = await UpdateAsync(id, approved: true);
        var unapproving = await UpdateAsync(id, approved: false);

        Assert.True(approving.AccessQueued);
        Assert.False(unchanged.AccessQueued);
        Assert.False(unapproving.AccessQueued);
        Assert.Equal((1, false), ((await ReadAsync(id)).Jobs.Count, (await ReadAsync(id)).Approved));

        // Conta ainda sem senha: aprovar de novo envia de novo.
        Assert.True((await UpdateAsync(id, approved: true)).AccessQueued);
        Assert.Equal((2, true), ((await ReadAsync(id)).Jobs.Count, (await ReadAsync(id)).Approved));
    }

    [Fact]
    public async Task Conta_com_senha_nao_recebe_envio_automatico_ao_ser_aprovada()
    {
        var id = (await CreateAsync(approved: false)).StudentId!.Value;
        await using (var db = NewContext())
        {
            var userId = await db.Students.Where(s => s.Id == id).Select(s => s.UserId).SingleAsync(Ct);
            (await db.Users.SingleAsync(u => u.Id == userId, Ct)).PasswordHash = "hash-sintetico";
            await db.SaveChangesAsync(Ct);
        }

        var result = await UpdateAsync(id, approved: true);

        Assert.True(result.Succeeded);
        Assert.False(result.AccessQueued);
        Assert.Equal((0, true), ((await ReadAsync(id)).Jobs.Count, (await ReadAsync(id)).Approved));
    }

    [Fact]
    public async Task Limite_de_3_por_hora_vale_para_o_gatilho_e_a_aprovacao_e_gravada_mesmo_assim()
    {
        var id = (await CreateAsync(approved: false)).StudentId!.Value;
        await using (var db = NewContext())
        {
            for (var i = 0; i < StudentAccessRequests.HourlyLimit; i++)
            {
                await new JobQueue(db, _clock).EnqueueAsync(new JobRequest(AccessType, id, new JobPayload(), TimeSpan.FromHours(72)), Ct);
            }

            await db.SaveChangesAsync(Ct);
        }

        var result = await UpdateAsync(id, approved: true);

        Assert.True(result.Succeeded);
        Assert.False(result.AccessQueued);
        Assert.Equal((3, true), ((await ReadAsync(id)).Jobs.Count, (await ReadAsync(id)).Approved));
    }

    [Fact]
    public async Task Formulario_enviado_duas_vezes_ao_mesmo_tempo_dispara_um_gatilho_so()
    {
        var id = (await CreateAsync(approved: false)).StudentId!.Value;

        var results = await Task.WhenAll(UpdateAsync(id, approved: true), UpdateAsync(id, approved: true));

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Single(results, r => r.AccessQueued);
        Assert.Equal((1, true), ((await ReadAsync(id)).Jobs.Count, (await ReadAsync(id)).Approved));
    }
}
