using System.Net;
using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Identidade;
using Helinho.Web.Pages.Alunos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1859 // consulta pela porta IAppDbContext, como o caso de uso

namespace Helinho.IntegrationTests;

/// <summary>Cadastro e edição de aluno (#16): validação, unidade vinda do usuário, 404 entre unidades, aprovação e sigilo (dados sintéticos).</summary>
[Collection(PostgresCollection.Name)]
public class StudentFormTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] AllFields = ["FullName", "Rg", "Address", "GuardianName", "GuardianEmail", "ClassName", "Cpf", "BirthDate"];
    private const string ValidCpf = "529.982.247-25";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly string _database = $"studentform_{Guid.NewGuid():N}";
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");
    private Guid _studentB;

    private sealed class FakeUser(string role, Guid? unitId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid? UserId => null;

        public Guid? UnitId => unitId;

        public bool IsInRole(string r) => r == role;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(ct);
        db.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole<Guid>(RoleNames.Student) { NormalizedName = "STUDENT" });
        db.Units.AddRange(_unitA, _unitB);
        var user = new AppUser { UserName = "formCaio", UnitId = _unitB.Id };
        db.Users.Add(user);
        var caio = new Student(user.Id, _unitB.Id, "FRM-Caio", "Caio", "11.111.111-1", "111.111.111-11",
            "Rua Sintética, 1", new DateOnly(2019, 3, 1), "Responsável Fictício", "5A", Today);
        db.Students.Add(caio);
        _studentB = caio.Id;
        await db.SaveChangesAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private static StudentForm Valid(bool approved = false) =>
        new("Ana Souza", "22.222.222-2", ValidCpf, "Rua Sintética, 2", new DateOnly(2018, 5, 20), "Maria Souza", "maria.souza@example.org", "4B", approved);

    [Fact]
    public async Task Admin_cadastra_na_propria_unidade_com_conta_de_aluno_sem_senha()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Admin, _unitA.Id));

        var result = await SaveStudent.CreateAsync(db, new FakeUser(RoleNames.Admin, _unitA.Id), new FixedClock(), new StudentAccounts(db), Requests(db), Valid(approved: true), ct);

        Assert.True(result.Succeeded);
        await using var read = NewContext(new FakeUser(RoleNames.Master, null));
        var student = await read.Students.SingleAsync(s => s.Id == result.StudentId, ct);
        Assert.Equal(_unitA.Id, student.UnitId);
        Assert.True(student.GuardianApproved);
        Assert.Equal("Ana Souza", student.FullName);
        var account = await read.Users.SingleAsync(u => u.Id == student.UserId, ct);
        Assert.Null(account.PasswordHash);
        Assert.Equal(_unitA.Id, account.UnitId);
    }

    [Fact]
    public async Task Cadastro_com_campos_vazios_cpf_invalido_e_idade_fora_de_6_a_14_e_rejeitado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Admin, _unitA.Id));
        var user = new FakeUser(RoleNames.Admin, _unitA.Id);

        var empty = await SaveStudent.CreateAsync(db, user, new FixedClock(), new StudentAccounts(db), Requests(db), new StudentForm(null, " ", null, "", null, null, null, null, false), ct);
        var badCpf = await SaveStudent.CreateAsync(db, user, new FixedClock(), new StudentAccounts(db), Requests(db), Valid() with { Cpf = "529.982.247-24" }, ct);
        var young = await SaveStudent.CreateAsync(db, user, new FixedClock(), new StudentAccounts(db), Requests(db), Valid() with { BirthDate = new DateOnly(2021, 1, 1) }, ct);
        var old = await SaveStudent.CreateAsync(db, user, new FixedClock(), new StudentAccounts(db), Requests(db), Valid() with { BirthDate = new DateOnly(2010, 1, 1) }, ct);

        Assert.Equal(
            AllFields.Order(),
            empty.Errors.Keys.Order());
        Assert.Equal("Cpf", Assert.Single(badCpf!.Errors).Key);
        Assert.Equal("BirthDate", Assert.Single(young!.Errors).Key);
        Assert.Equal("BirthDate", Assert.Single(old!.Errors).Key);
        Assert.Equal(1, await db.Students.IgnoreQueryFilters().CountAsync(ct)); // só o Caio do seed do teste
    }

    [Fact]
    public async Task Mensagens_de_erro_nao_ecoam_cpf_nem_rg()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Admin, _unitA.Id));

        var result = await SaveStudent.CreateAsync(
            db, new FakeUser(RoleNames.Admin, _unitA.Id), new FixedClock(), new StudentAccounts(db), Requests(db),
            Valid() with { Cpf = "123.456.789-00", Rg = new string('9', 40) }, ct);

        Assert.All(result.Errors.Values, m =>
        {
            Assert.DoesNotContain("123.456", m, StringComparison.Ordinal);
            Assert.DoesNotContain("9999", m, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Logs_de_cadastro_e_edicao_nao_contem_cpf_nem_rg()
    {
        var ct = TestContext.Current.CancellationToken;
        var capture = new CaptureLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        var user = new FakeUser(RoleNames.Admin, _unitA.Id);
        await using var db = NewContext(user, loggerFactory);
        var secretRg = "77.888.999-X";
        var accounts = new StudentAccounts(db);

        var invalid = await SaveStudent.CreateAsync(db, user, new FixedClock(), accounts, Requests(db), Valid() with { Cpf = "123.456.789-00", Rg = secretRg }, ct);
        var created = await SaveStudent.CreateAsync(db, user, new FixedClock(), accounts, Requests(db), Valid() with { Rg = secretRg }, ct);
        var edited = await SaveStudent.UpdateAsync(db, new FixedClock(), Requests(db), created.StudentId!.Value, Valid(approved: true) with { Rg = secretRg }, ct);

        Assert.False(invalid.Succeeded);
        Assert.True(created.Succeeded);
        Assert.True(edited!.Succeeded);
        Assert.NotEmpty(capture.Entries); // prova que a captura está ligada
        Assert.All(capture.Entries, e =>
        {
            Assert.DoesNotContain("529.982.247-25", e, StringComparison.Ordinal);
            Assert.DoesNotContain("52998224725", e, StringComparison.Ordinal);
            Assert.DoesNotContain("123.456.789-00", e, StringComparison.Ordinal);
            Assert.DoesNotContain(secretRg, e, StringComparison.Ordinal);
            Assert.DoesNotContain("maria.souza@example.org", e, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Email_do_responsavel_invalido_e_recusado_sem_ecoar_o_valor()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new FakeUser(RoleNames.Admin, _unitA.Id);
        await using var db = NewContext(user);
        var tooLong = new string('a', 250) + "@example.org";

        foreach (var email in new[] { "sem-arroba", "Maria Souza <maria@example.org>", "maria@example.org, outra@example.org", tooLong })
        {
            var result = await SaveStudent.CreateAsync(db, user, new FixedClock(), new StudentAccounts(db), Requests(db), Valid() with { GuardianEmail = email }, ct);

            var error = Assert.Single(result.Errors);
            Assert.Equal("GuardianEmail", error.Key);
            Assert.Equal(email == tooLong ? "Use no máximo 254 caracteres." : "E-mail inválido. Confira o endereço.", error.Value);
        }

        Assert.Equal(1, await db.Students.IgnoreQueryFilters().CountAsync(ct)); // só o Caio do seed do teste
    }

    [Fact]
    public async Task Email_do_responsavel_nao_e_unico_nem_vira_identificador_de_login()
    {
        const string Email = "responsavel.irmaos@example.org";
        var ct = TestContext.Current.CancellationToken;
        var user = new FakeUser(RoleNames.Admin, _unitA.Id);
        await using var db = NewContext(user);

        var first = await SaveStudent.CreateAsync(db, user, new FixedClock(), new StudentAccounts(db), Requests(db), Valid() with { GuardianEmail = Email }, ct);
        var sibling = await SaveStudent.CreateAsync(
            db, user, new FixedClock(), new StudentAccounts(db), Requests(db), Valid() with { FullName = "Bia Souza", GuardianEmail = $" {Email} " }, ct);

        Assert.True(first.Succeeded);
        Assert.True(sibling.Succeeded); // irmãos: o mesmo responsável
        await using var read = NewContext(new FakeUser(RoleNames.Master, null));
        var students = await read.Students.Where(s => s.GuardianEmail == Email).ToListAsync(ct);
        Assert.Equal(2, students.Count);
        Assert.All(students, s => Assert.Null(s.PreviousGuardianEmail));
        // A credencial continua na matrícula: o e-mail do responsável não chega à conta do Identity.
        var normalized = Email.ToUpperInvariant();
        var userIds = students.Select(s => s.UserId).ToList();
        var accounts = await read.Users.Where(u => userIds.Contains(u.Id)).ToListAsync(ct);
        Assert.Equal(2, accounts.Count);
        Assert.All(accounts, a => Assert.Null(a.Email));
        Assert.False(await read.Users.AnyAsync(
            u => u.Email == Email || u.UserName == Email || u.NormalizedEmail == normalized || u.NormalizedUserName == normalized, ct));
    }

    [Fact]
    public async Task Edicao_carrega_e_troca_o_email_e_aluno_anterior_ao_campo_abre_sem_ele()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Admin, _unitB.Id));

        var legacy = await SaveStudent.LoadAsync(db, _studentB, ct); // o Caio foi criado sem e-mail do responsável
        var missing = await SaveStudent.UpdateAsync(db, new FixedClock(), Requests(db), _studentB, Valid() with { GuardianEmail = null }, ct);
        var saved = await SaveStudent.UpdateAsync(db, new FixedClock(), Requests(db), _studentB, Valid() with { GuardianEmail = "novo.responsavel@example.org" }, ct);

        Assert.Null(legacy!.GuardianEmail);
        Assert.Equal(["GuardianEmail"], missing!.Errors.Keys);
        Assert.Equal("Informe o e-mail do responsável.", missing.Errors["GuardianEmail"]);
        Assert.True(saved!.Succeeded);
        await using var read = NewContext(new FakeUser(RoleNames.Admin, _unitB.Id));
        Assert.Equal("novo.responsavel@example.org", (await SaveStudent.LoadAsync(read, _studentB, ct))!.GuardianEmail);
        Assert.Null((await read.Students.SingleAsync(s => s.Id == _studentB, ct)).PreviousGuardianEmail);
    }

    [Fact]
    public async Task Master_ou_usuario_sem_unidade_nao_cadastra()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Master, null));

        var result = await SaveStudent.CreateAsync(db, new FakeUser(RoleNames.Master, null), new FixedClock(), new StudentAccounts(db), Requests(db), Valid(), ct);

        Assert.False(result.Succeeded);
        Assert.Contains("Unidade", result.Errors.Keys);
    }

    [Fact]
    public async Task Admin_edita_e_altera_a_aprovacao_sem_mudar_a_unidade()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new FakeUser(RoleNames.Admin, _unitA.Id);
        await using var db = NewContext(user);
        var created = await SaveStudent.CreateAsync(db, user, new FixedClock(), new StudentAccounts(db), Requests(db), Valid(), ct);

        var edited = await SaveStudent.UpdateAsync(db, new FixedClock(), Requests(db), created.StudentId!.Value,
            Valid(approved: true) with { FullName = "Ana Souza Lima", ClassName = "5A" }, ct);

        Assert.True(edited!.Succeeded);
        await using var read = NewContext(user);
        var form = await SaveStudent.LoadAsync(read, created.StudentId.Value, ct);
        Assert.Equal(("Ana Souza Lima", "5A", true), (form!.FullName, form.ClassName, form.GuardianApproved));
        var invalid = await SaveStudent.UpdateAsync(db, new FixedClock(), Requests(db), created.StudentId.Value, Valid() with { Cpf = "000" }, ct);
        Assert.Equal(["Cpf"], invalid!.Errors.Keys);
    }

    [Fact]
    public async Task Admin_nao_carrega_nem_edita_aluno_de_outra_unidade()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(new FakeUser(RoleNames.Admin, _unitA.Id));

        Assert.Null(await SaveStudent.LoadAsync(db, _studentB, ct));
        Assert.Null(await SaveStudent.UpdateAsync(db, new FixedClock(), Requests(db), _studentB, Valid(), ct));
    }

    [Fact]
    public void Modelo_do_formulario_nao_expoe_unidade_nem_ids()
    {
        var names = typeof(StudentInput).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n is "Id" or "UnitId" or "UserId" or "Enrollment" or "Xp" or "PreviousGuardianEmail");
    }

    [Fact]
    public async Task Editar_aluno_de_outra_unidade_por_url_direta_da_404()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        Guid unitId, otherStudent;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var units = await db.Units.IgnoreQueryFilters().OrderBy(u => u.Name).ToListAsync(TestContext.Current.CancellationToken);
            unitId = units[0].Id;
            otherStudent = (await db.Students.IgnoreQueryFilters().FirstAsync(s => s.UnitId != unitId, TestContext.Current.CancellationToken)).Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);
        var response = await client.GetAsync($"/Alunos/Editar/{otherStudent}", TestContext.Current.CancellationToken);
        var novo = await client.GetAsync("/Alunos/Novo", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, novo.StatusCode);
    }

    [Fact]
    public async Task Master_nao_acessa_o_formulario()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await client.GetAsync("/Alunos/Novo", TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static StudentAccessRequests Requests(AppDbContext db) =>
        new(new JobQueue(db, new FixedClock()), new StudentAccessJobs(db), new StudentAccounts(db), new FixedClock());

    private AppDbContext NewContext(ICurrentUser user, ILoggerFactory? loggerFactory = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString);
        if (loggerFactory is not null)
        {
            options.UseLoggerFactory(loggerFactory);
        }

        return new AppDbContext(options.Options, user);
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add($"{formatter(state, exception)} {exception}");
                }
            }
        }
    }
}
