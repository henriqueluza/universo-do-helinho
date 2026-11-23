using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    private sealed class FakeUser(string? role, Guid? unitId) : ICurrentUser
    {
        public bool IsAuthenticated => role is not null;

        public Guid? UserId => null;

        public Guid? UnitId => unitId;

        public bool IsInRole(string r) => r == role;
    }

    private AppDbContext NewContext(string database, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString)
            .Options, user);

    [Fact]
    public async Task Migracao_unica_aplica_em_banco_limpo_e_cobre_o_modelo()
    {
        await using var db = NewContext("migracao", new FakeUser(null, null));

        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.False(db.Database.HasPendingModelChanges());
        // Exceções ao schema congelado: AddDataProtectionKeys (Issue #103, ADR-0002 §9) e AddQueueJobs (Issue #223, ADR-0003 §5).
        Assert.Equal(
            ["20261110034535_InitialCreate", "20261119015853_AddDataProtectionKeys", "20261122233915_AddQueueJobs"],
            db.Database.GetMigrations());
    }

    [Fact]
    public async Task Filtro_global_isola_alunos_por_unidade()
    {
        var ct = TestContext.Current.CancellationToken;
        var unitA = new Unit("Unidade A");
        var unitB = new Unit("Unidade B");
        await using (var seed = NewContext("filtro", new FakeUser(RoleNames.Master, null)))
        {
            await seed.Database.MigrateAsync(ct);
            var users = new[] { new AppUser { UserName = "a", UnitId = unitA.Id }, new AppUser { UserName = "b", UnitId = unitB.Id } };
            seed.Users.AddRange(users);
            seed.Units.AddRange(unitA, unitB);
            seed.Students.Add(NewStudent(users[0].Id, unitA.Id, "A1"));
            seed.Students.Add(NewStudent(users[1].Id, unitB.Id, "B1"));
            await seed.SaveChangesAsync(ct);
        }

        await using var master = NewContext("filtro", new FakeUser(RoleNames.Master, null));
        await using var adminA = NewContext("filtro", new FakeUser(RoleNames.Admin, unitA.Id));
        await using var anonymous = NewContext("filtro", new FakeUser(null, null));

        Assert.Equal(2, await master.Students.CountAsync(ct));
        Assert.Equal(["A1"], await adminA.Students.Select(s => s.Enrollment).ToListAsync(ct));
        Assert.Equal(0, await anonymous.Students.CountAsync(ct));
    }

    [Fact]
    public async Task Matricula_e_unica()
    {
        var ct = TestContext.Current.CancellationToken;
        var unit = new Unit("Unidade U");
        await using var db = NewContext("unicidade", new FakeUser(RoleNames.Master, null));
        await db.Database.MigrateAsync(ct);
        var users = new[] { new AppUser { UserName = "u1", UnitId = unit.Id }, new AppUser { UserName = "u2", UnitId = unit.Id } };
        db.Users.AddRange(users);
        db.Units.Add(unit);
        db.Students.AddRange(NewStudent(users[0].Id, unit.Id, "DUP"), NewStudent(users[1].Id, unit.Id, "DUP"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    private static Student NewStudent(Guid userId, Guid unitId, string enrollment) =>
        new(userId, unitId, enrollment, "Aluno Fictício", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1", new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", Today);
}
