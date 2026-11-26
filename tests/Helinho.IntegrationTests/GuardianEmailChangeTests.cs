using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Troca do e-mail do responsável (#267, ADR-0003 §9 e §11.4; T13 e T16): carimbo, cancelamento, aviso e o ciclo do endereço anterior. Sem host; dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public class GuardianEmailChangeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AccessType = "email.student-access";
    private const string NoticeType = "email.guardian-email-changed";
    private const string First = "primeiro.responsavel@example.org";
    private const string Second = "segundo.responsavel@example.org";
    private const string Third = "terceiro.responsavel@example.org";
    private readonly string _database = $"troca_{Guid.NewGuid():N}";
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

    private sealed record Snapshot(Student Student, string? Stamp, List<QueueJob> Access, List<QueueJob> Notices);

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

    private AppDbContext NewContext(ICurrentUser? user = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, user ?? new Admin(_unit.Id));

    private StudentAccessRequests Requests(AppDbContext db) =>
        new(new JobQueue(db, _clock), new StudentAccessJobs(db), new StudentAccounts(db), _clock);

    private static StudentForm Form(string? guardianEmail) =>
        new("Ana Souza", "22.222.222-2", "529.982.247-25", "Rua Sintética, 2", new DateOnly(2018, 5, 20), "Maria Souza", guardianEmail, "4B", true);

    /// <summary>Aluno já aprovado, com o primeiro endereço: nasce com um acesso pendente na fila (gatilho da aprovação).</summary>
    private async Task<Guid> CreateAsync()
    {
        await using var db = NewContext();
        var result = await SaveStudent.CreateAsync(db, new Admin(_unit.Id), _clock, new StudentAccounts(db), Requests(db), Form(First), Ct);
        return Assert.IsType<Guid>(result.StudentId);
    }

    private async Task<SaveStudentResult> ChangeAsync(Guid studentId, string guardianEmail)
    {
        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        await using var db = NewContext();
        return Assert.IsType<SaveStudentResult>(await SaveStudent.UpdateAsync(db, _clock, Requests(db), studentId, Form(guardianEmail), Ct));
    }

    private async Task<Snapshot> ReadAsync(Guid studentId)
    {
        await using var db = NewContext();
        var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId, Ct);
        var stamp = await db.Users.Where(u => u.Id == student.UserId).Select(u => u.SecurityStamp).SingleAsync(Ct);
        var jobs = await db.QueueJobs.AsNoTracking().Where(j => j.SubjectId == studentId).OrderBy(j => j.CreatedAt).ToListAsync(Ct);
        return new Snapshot(student, stamp, jobs.FindAll(j => j.Type == AccessType), jobs.FindAll(j => j.Type == NoticeType));
    }

    /// <summary>O que o tratador faz ao terminar o aviso: num contexto sem usuário, apaga o endereço e grava junto com a conclusão.</summary>
    private async Task FinishNoticeAsync(Guid studentId, string status)
    {
        await using var worker = NewContext(new AnonymousCurrentUser());
        Assert.False(await worker.Students.AnyAsync(Ct)); // sem usuário, o filtro de unidade esconde todos os alunos

        await new PreviousGuardianEmailCleaner(worker).StageAsync(studentId, Ct);
        await new PreviousGuardianEmailCleaner(worker).StageAsync(Guid.NewGuid(), Ct); // aluno que não existe mais: nada a apagar
        Assert.NotNull((await ReadAsync(studentId)).Student.PreviousGuardianEmail); // o serviço não salva (§6.6)

        await worker.Database.ExecuteSqlAsync(
            $"""UPDATE "QueueJobs" SET "Status" = {status}, "FinishedAt" = {_clock.UtcNow} WHERE "Type" = {NoticeType} AND "Status" = 'Pending'""", Ct);
        await worker.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Trocar_o_email_troca_o_carimbo_cancela_o_acesso_pendente_guarda_o_anterior_e_enfileira_o_aviso()
    {
        var id = await CreateAsync();
        var before = await ReadAsync(id);

        var result = await ChangeAsync(id, Second);

        var after = await ReadAsync(id);
        var notice = Assert.Single(after.Notices);
        Assert.True(result.GuardianEmailChanged);
        Assert.Equal(QueueJobStatus.Pending, Assert.Single(before.Access).Status);
        Assert.Equal(QueueJobStatus.Cancelled, Assert.Single(after.Access).Status);
        Assert.NotEqual(before.Stamp, after.Stamp);
        Assert.Equal((Second, First), (after.Student.GuardianEmail, after.Student.PreviousGuardianEmail));
        Assert.Equal((QueueJobStatus.Pending, _clock.UtcNow.AddHours(24)), (notice.Status, notice.ExpiresAt));
        Assert.Equal("{\"v\":1}", notice.Payload.Replace(" ", "", StringComparison.Ordinal));
        // Uma chave por troca, sem o carimbo em si e sem endereço: a fila continua sem destinatário (§5.3).
        Assert.Matches($"^{NoticeType}:{id}:[0-9a-f]{{16}}$", notice.IdempotencyKey);
        Assert.NotNull(after.Stamp);
        Assert.DoesNotContain(after.Stamp, notice.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Primeiro_preenchimento_e_salvar_o_mesmo_endereco_nao_tem_efeito_nenhum()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var user = new AppUser { UserName = "troca-legado", UnitId = _unit.Id, SecurityStamp = "carimbo-original" };
            db.Users.Add(user); // o id nasce ao entrar no contexto
            var legacy = new Student(user.Id, _unit.Id, "TRO-0001", "Aluno Fictício", "11.111.111-1", "111.111.111-11",
                "Rua Sintética, 1", new DateOnly(2018, 5, 20), "Responsável Fictício", "5A", new DateOnly(2026, 11, 22));
            db.Students.Add(legacy);
            await db.SaveChangesAsync(Ct);
            id = legacy.Id;
        }

        var firstFill = await ChangeAsync(id, First);
        var same = await ChangeAsync(id, $"  {First} ");

        var after = await ReadAsync(id);
        Assert.False(firstFill.GuardianEmailChanged);
        Assert.False(same.GuardianEmailChanged);
        Assert.Equal((First, null, "carimbo-original"), (after.Student.GuardianEmail, after.Student.PreviousGuardianEmail, after.Stamp));
        Assert.Empty(after.Notices);
        Assert.All(after.Access, job => Assert.Equal(QueueJobStatus.Pending, job.Status)); // o acesso da aprovação continua de pé
    }

    [Fact]
    public async Task Trocar_de_novo_com_aviso_pendente_mantem_o_primeiro_endereco_e_nao_enfileira_outro_aviso()
    {
        var id = await CreateAsync();
        await ChangeAsync(id, Second);
        var middle = await ReadAsync(id);
        await using (var db = NewContext())
        {
            Assert.Equal(SendStudentAccessResult.Queued, await SendStudentAccess.HandleAsync(db, new Admin(_unit.Id), Requests(db), id, Ct));
        }

        var result = await ChangeAsync(id, Third);

        var after = await ReadAsync(id);
        Assert.True(result.GuardianEmailChanged);
        Assert.Equal((Third, First), (after.Student.GuardianEmail, after.Student.PreviousGuardianEmail));
        Assert.Equal(middle.Notices.Single().Id, Assert.Single(after.Notices).Id);
        // A proteção vale em toda troca, com ou sem aviso novo: carimbo trocado e acessos pendentes cancelados.
        Assert.NotEqual(middle.Stamp, after.Stamp);
        Assert.Equal([QueueJobStatus.Cancelled, QueueJobStatus.Cancelled], after.Access.Select(j => j.Status));
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Failed")] // falha permanente, teto de tentativas ou expired: para a coluna é o mesmo fim
    public async Task Quando_o_aviso_termina_o_endereco_anterior_e_apagado_sem_usuario_e_a_troca_seguinte_avisa_de_novo(string finalStatus)
    {
        var id = await CreateAsync();
        await ChangeAsync(id, Second);

        await FinishNoticeAsync(id, finalStatus);
        var finished = await ReadAsync(id);
        var result = await ChangeAsync(id, Third);

        var after = await ReadAsync(id);
        Assert.Null(finished.Student.PreviousGuardianEmail);
        Assert.Equal(Second, finished.Student.GuardianEmail);
        Assert.True(result.GuardianEmailChanged);
        Assert.Equal((Third, Second), (after.Student.GuardianEmail, after.Student.PreviousGuardianEmail));
        Assert.Equal([Enum.Parse<QueueJobStatus>(finalStatus), QueueJobStatus.Pending], after.Notices.Select(j => j.Status));
    }
}
