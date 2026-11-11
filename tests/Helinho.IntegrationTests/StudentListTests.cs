using System.Net;
using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1859 // consulta pela porta IAppDbContext, como o caso de uso

namespace Helinho.IntegrationTests;

/// <summary>Listagem de alunos do painel (#13): colunas, filtros, segregação por unidade, sigilo de CPF/RG e paginação (dados sintéticos).</summary>
[Collection(PostgresCollection.Name)]
public class StudentListTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly string _database = $"studentlist_{Guid.NewGuid():N}";
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");

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
        db.Units.AddRange(_unitA, _unitB);
        // A: Ana (7 anos, 5A, aprovada), Bia (12 anos, 6B, pendente); B: Caio (7 anos, 5A).
        var specs = new[]
        {
            ("Ana", _unitA, new DateOnly(2019, 3, 1), "5A", true),
            ("Bia", _unitA, new DateOnly(2014, 3, 1), "6B", false),
            ("Caio", _unitB, new DateOnly(2019, 3, 1), "5A", true),
        };
        foreach (var (name, unit, birth, className, approved) in specs)
        {
            var appUser = new AppUser { UserName = $"list{name}", UnitId = unit.Id };
            db.Users.Add(appUser);
            var student = new Student(appUser.Id, unit.Id, $"LST-{name}", name, "11.111.111-1", "111.111.111-11",
                "Rua Sintética, 1", birth, "Responsável Fictício", className, Today);
            if (approved)
            {
                student.ApproveByGuardian();
            }

            db.Students.Add(student);
        }

        await db.SaveChangesAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Master_ve_todos_com_unidade_turma_e_aprovacao()
    {
        var result = await RunAsync(RoleNames.Master, null, new StudentListQuery(null, null, null, 1));

        Assert.Equal(["Ana", "Bia", "Caio"], result.Rows.Select(r => r.FullName));
        var ana = result.Rows[0];
        Assert.Equal(("LST-Ana", "Unidade A", "5A", true), (ana.Enrollment, ana.UnitName, ana.ClassName, ana.GuardianApproved));
        Assert.False(result.Rows[1].GuardianApproved);
        Assert.Equal(["5A", "6B"], result.Classes);
        Assert.Equal(2, result.Units.Count);
    }

    [Fact]
    public async Task Filtros_de_turma_faixa_e_unidade()
    {
        var turma = await RunAsync(RoleNames.Master, null, new StudentListQuery(null, "5A", null, 1));
        var faixa = await RunAsync(RoleNames.Master, null, new StudentListQuery(null, null, AgeBand.Age11To12, 1));
        var unidade = await RunAsync(RoleNames.Master, null, new StudentListQuery(_unitB.Id, null, null, 1));
        var nenhum = await RunAsync(RoleNames.Master, null, new StudentListQuery(null, null, AgeBand.Age13To14, 1));

        Assert.Equal(["Ana", "Caio"], turma.Rows.Select(r => r.FullName));
        Assert.Equal(["Bia"], faixa.Rows.Select(r => r.FullName));
        Assert.Equal(["Caio"], unidade.Rows.Select(r => r.FullName));
        Assert.Empty(nenhum.Rows);
    }

    [Fact]
    public async Task Admin_nunca_ve_aluno_de_outra_unidade_mesmo_forcando_a_unidade()
    {
        var own = await RunAsync(RoleNames.Admin, _unitA.Id, new StudentListQuery(null, null, null, 1));
        var forced = await RunAsync(RoleNames.Admin, _unitA.Id, new StudentListQuery(_unitB.Id, null, null, 1));

        Assert.Equal(["Ana", "Bia"], own.Rows.Select(r => r.FullName));
        Assert.Equal(["Ana", "Bia"], forced.Rows.Select(r => r.FullName));
        Assert.Empty(forced.Units);
    }

    [Fact]
    public async Task Aluno_fora_de_6_a_14_anos_aparece_sem_faixa_em_vez_de_derrubar_a_listagem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = NewContext(new FakeUser(RoleNames.Master, null)))
        {
            // Nasceu em 2011: tinha 14 anos quando foi cadastrado e fez 15 depois.
            var appUser = new AppUser { UserName = "listDora", UnitId = _unitA.Id };
            db.Users.Add(appUser);
            db.Students.Add(new Student(appUser.Id, _unitA.Id, "LST-Dora", "Dora", "11.111.111-1", "111.111.111-11",
                "Rua Sintética, 1", new DateOnly(2011, 3, 1), "Responsável Fictício", "9A", new DateOnly(2025, 11, 10)));
            await db.SaveChangesAsync(ct);
        }

        var result = await RunAsync(RoleNames.Master, null, new StudentListQuery(null, null, null, 1));

        Assert.Equal(4, result.Total);
        Assert.Null(result.Rows.Single(r => r.FullName == "Dora").Band);
        Assert.Equal(AgeBand.Age6To7, result.Rows.Single(r => r.FullName == "Ana").Band);
    }

    [Fact]
    public async Task Pagina_fora_do_intervalo_e_ajustada()
    {
        var result = await RunAsync(RoleNames.Master, null, new StudentListQuery(null, null, null, 99));

        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task Pagina_admin_nao_mostra_outra_unidade_nem_cpf_ou_rg()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        Guid unitId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            unitId = (await db.Units.IgnoreQueryFilters().OrderBy(u => u.Name).FirstAsync(TestContext.Current.CancellationToken)).Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);
        var other = Guid.NewGuid();
        var response = await client.GetAsync($"/Alunos?unidade={other}", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("id=\"unidade\"", html, StringComparison.Ordinal);

        // Só o texto visível: o token antiforgery (atributo de <input>) é aleatório e pode conter "RG".
        var visible = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)\b.*?</\1>", " ", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            "<[^>]*>",
            " ");
        Assert.DoesNotContain("CPF", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("RG", visible, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{3}\.\d{3}\.\d{3}-\d{2}", visible);
    }

    [Fact]
    public async Task Pagina_sem_alunos_mostra_estado_vazio()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        Guid emptyUnit;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = new Unit("Unidade Vazia");
            db.Units.Add(unit);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            emptyUnit = unit.Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Master);
        var response = await client.GetAsync($"/Alunos?unidade={emptyUnit}", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nenhum aluno encontrado.", html, StringComparison.Ordinal);
    }

    private async Task<StudentListResult> RunAsync(string role, Guid? unitId, StudentListQuery query)
    {
        var user = new FakeUser(role, unitId);
        await using var context = NewContext(user);
        IAppDbContext db = context;
        return await ListStudents.HandleAsync(db, user, new FixedClock(), query, TestContext.Current.CancellationToken);
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, user);
}
