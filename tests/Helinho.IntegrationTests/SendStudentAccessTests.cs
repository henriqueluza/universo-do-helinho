using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Enviar acesso do aluno (#267, ADR-0003 §11.4): o que entra na fila, quem pode pedir e o limite. Sem host; dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public class SendStudentAccessTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AccessType = "email.student-access";
    private static readonly DateOnly Today = new(2026, 11, 22);
    private readonly string _database = $"acesso_{Guid.NewGuid():N}";
    private readonly MutableClock _clock = new();
    private readonly Unit _unitA = new("Unidade A (fictícia)");
    private readonly Unit _unitB = new("Unidade B (fictícia)");
    private Guid _approved, _pending, _withoutEmail;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeUser AdminA => new(RoleNames.Admin, _unitA.Id);

    private sealed record FakeUser(string Role, Guid? UnitId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public bool IsInRole(string role) => role == Role;
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Corrida do clique duplo: a outra requisição grava a mesma chave depois da conferência desta e antes da gravação.</summary>
    private sealed class RacingQueue(IJobQueue inner, Func<JobRequest, Task> other) : IJobQueue
    {
        public async Task<Guid?> EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
        {
            var id = await inner.EnqueueAsync(request, cancellationToken);
            await other(request);
            return id;
        }

        public Task<int> CancelPendingAsync(string type, Guid subjectId, CancellationToken cancellationToken) =>
            inner.CancelPendingAsync(type, subjectId, cancellationToken);
    }

    public async ValueTask InitializeAsync()
    {
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(Ct);
        db.Units.AddRange(_unitA, _unitB);
        _approved = Add(db, "ACE-0001", "ana.responsavel@example.org", approved: true);
        _pending = Add(db, "ACE-0002", "bia.responsavel@example.org", approved: false);
        _withoutEmail = Add(db, "ACE-0003", null, approved: true);
        await db.SaveChangesAsync(Ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private Guid Add(AppDbContext db, string enrollment, string? guardianEmail, bool approved)
    {
        var user = new AppUser { UserName = enrollment.ToLowerInvariant(), UnitId = _unitA.Id };
        db.Users.Add(user);
        var student = new Student(user.Id, _unitA.Id, enrollment, "Aluno Fictício", "11.111.111-1", "111.111.111-11",
            "Rua Sintética, 1", new DateOnly(2017, 3, 1), "Responsável Fictício", "5A", Today);
        if (guardianEmail is not null)
        {
            student.SetGuardianEmail(guardianEmail);
        }

        student.SetGuardianApproval(approved);
        db.Students.Add(student);
        return student.Id;
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, user);

    private StudentAccessRequests Requests(AppDbContext db, IJobQueue? queue = null) =>
        new(queue ?? new JobQueue(db, _clock), new StudentAccessJobs(db), new StudentAccounts(db), _clock);

    private async Task<SendStudentAccessResult> SendAsync(Guid studentId, ICurrentUser? user = null, ICurrentUser? contextUser = null)
    {
        await using var db = NewContext(contextUser ?? user ?? AdminA);
        return await SendStudentAccess.HandleAsync(db, user ?? AdminA, Requests(db), studentId, Ct);
    }

    private async Task<List<JsonElement>> RowsAsync()
    {
        await using var db = NewContext(AdminA);
        var rows = await db.Database
            .SqlQuery<string>($"""SELECT row_to_json(q)::text AS "Value" FROM "QueueJobs" q ORDER BY q."CreatedAt" """).ToListAsync(Ct);
        return rows.ConvertAll(r => JsonDocument.Parse(r).RootElement);
    }

    [Fact]
    public async Task Enfileira_so_referencias_com_validade_de_72_horas_e_chave_por_janela_de_2_minutos()
    {
        var result = await SendAsync(_approved);

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(SendStudentAccessResult.Queued, result);
        Assert.Equal(AccessType, row.GetProperty("Type").GetString());
        Assert.Equal(_approved, row.GetProperty("SubjectId").GetGuid());
        Assert.Equal("Pending", row.GetProperty("Status").GetString());
        Assert.Equal("v", Assert.Single(row.GetProperty("Payload").EnumerateObject()).Name);
        Assert.Equal(1, row.GetProperty("Payload").GetProperty("v").GetInt32());
        Assert.Equal(_clock.UtcNow.AddHours(72), row.GetProperty("ExpiresAt").GetDateTimeOffset());
        Assert.Equal($"{AccessType}:{_approved}:{_clock.UtcNow.ToUnixTimeSeconds() / 120}", row.GetProperty("IdempotencyKey").GetString());
        // §5.3: nem destinatário, nem nome, nem matrícula em repouso na fila.
        Assert.DoesNotMatch("@|example|Fict|ACE-", row.GetRawText());
    }

    [Fact]
    public async Task Sem_aprovacao_ou_sem_email_do_responsavel_nada_e_enfileirado()
    {
        Assert.Equal(SendStudentAccessResult.NotApproved, await SendAsync(_pending));
        Assert.Equal(SendStudentAccessResult.MissingGuardianEmail, await SendAsync(_withoutEmail));
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task Admin_de_outra_unidade_master_e_usuario_sem_unidade_recebem_nao_encontrado()
    {
        var master = new FakeUser(RoleNames.Master, null);
        var adminB = new FakeUser(RoleNames.Admin, _unitB.Id);

        Assert.Equal(SendStudentAccessResult.NotFound, await SendAsync(_approved, adminB));
        Assert.Equal(SendStudentAccessResult.NotFound, await SendAsync(_approved, master));
        Assert.Equal(SendStudentAccessResult.NotFound, await SendAsync(_approved, new FakeUser(RoleNames.Admin, null)));
        Assert.Equal(SendStudentAccessResult.NotFound, await SendAsync(Guid.NewGuid()));
        // A unidade é conferida no próprio caso de uso: num contexto que enxerga todas, o admin da B continua sem o aluno da A.
        Assert.Equal(SendStudentAccessResult.NotFound, await SendAsync(_approved, adminB, contextUser: master));
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task Limite_de_3_por_hora_e_contado_na_fila_e_trabalho_cancelado_nao_conta()
    {
        var results = new List<SendStudentAccessResult>();
        for (var i = 0; i < 4; i++)
        {
            results.Add(await SendAsync(_approved));
            _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        }

        Assert.Equal([SendStudentAccessResult.Queued, SendStudentAccessResult.Queued, SendStudentAccessResult.Queued, SendStudentAccessResult.LimitReached], results);
        Assert.Equal(3, (await RowsAsync()).Count);
        Assert.Equal(SendStudentAccessResult.Queued, await SendAsync(await ApproveAsync(_pending))); // o limite é por aluno

        await using (var db = NewContext(AdminA))
        {
            Assert.Equal(3, await new JobQueue(db, _clock).CancelPendingAsync(AccessType, _approved, Ct));
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(SendStudentAccessResult.Queued, await SendAsync(_approved));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(61); // a janela do limite é de uma hora
        Assert.Equal(SendStudentAccessResult.Queued, await SendAsync(_approved));
    }

    [Fact]
    public async Task Clique_duplo_na_mesma_janela_nao_cria_a_segunda_linha_nem_quando_as_requisicoes_correm_juntas()
    {
        Assert.Equal(SendStudentAccessResult.Queued, await SendAsync(_approved));
        Assert.Equal(SendStudentAccessResult.Queued, await SendAsync(_approved));
        Assert.Single(await RowsAsync());

        await using var db = NewContext(AdminA);
        var racing = new RacingQueue(new JobQueue(db, _clock), async request =>
        {
            await using var other = NewContext(AdminA);
            await new JobQueue(other, _clock).EnqueueAsync(request, Ct);
            await other.SaveChangesAsync(Ct);
        });
        var raced = await SendStudentAccess.HandleAsync(db, AdminA, Requests(db, racing), await ApproveAsync(_pending), Ct);

        Assert.Equal(SendStudentAccessResult.Queued, raced); // a outra requisição gravou o pedido: está na fila
        Assert.Equal(2, (await RowsAsync()).Count);
    }

    private async Task<Guid> ApproveAsync(Guid studentId)
    {
        await using var db = NewContext(AdminA);
        (await db.Students.SingleAsync(s => s.Id == studentId, Ct)).SetGuardianApproval(true);
        await db.SaveChangesAsync(Ct);
        return studentId;
    }
}
