using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PeopleSeedTests(PostgresFixture postgres)
{
    // Senha gerada na hora: nenhuma senha fixa em teste.
    private static string RandomPassword() => $"{Guid.NewGuid():N}Aa1!";

    private static async Task SeedAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Seed_sem_senha_falha_com_mensagem_clara()
    {
        await using var factory = new HelinhoWebFactory(postgres, "");
        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.RunAsync(TestContext.Current.CancellationToken));

        Assert.Contains(PeopleSeeder.PasswordVariable, ex.Message);
    }

    [Fact]
    public async Task Seed_cria_contagens_esperadas_e_e_idempotente()
    {
        var password = RandomPassword();
        await using var factory = new HelinhoWebFactory(postgres, password);

        await SeedAsync(factory);
        await SeedAsync(factory); // segunda execução: não duplica nem falha

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;

        var units = await db.Units.Where(u => u.Name.EndsWith("(fictícia)")).ToListAsync(ct);
        Assert.Equal(2, units.Count);

        var students = await db.Students.IgnoreQueryFilters().ToListAsync(ct);
        Assert.Equal(10, students.Count);
        Assert.All(units, u => Assert.Equal(5, students.Count(s => s.UnitId == u.Id)));
        Assert.Contains(students, s => !s.GuardianApproved);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bands = students.Select(s => s.AgeBandAt(today)).Distinct().ToList();
        Assert.Equal(Enum.GetValues<AgeBand>().Length, bands.Count);

        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           group r by r.Name into g
                           select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!, x => x.Count, ct);
        Assert.Equal(1, roles[RoleNames.Master]);
        Assert.Equal(2, roles[RoleNames.Admin]);
        Assert.Equal(10, roles[RoleNames.Student]);

        var admins = await db.Users.Where(u => u.Email!.StartsWith("admin.")).ToListAsync(ct);
        Assert.Equal(2, admins.Select(a => a.UnitId).Distinct().Count());
        Assert.Equal(13, await db.Users.CountAsync(u => u.Email!.EndsWith("@helinho.example"), ct));
        Assert.All(students, s => Assert.StartsWith("20260", s.Enrollment));
    }
}
