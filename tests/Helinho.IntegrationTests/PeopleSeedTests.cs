using Helinho.Application.Abstractions;
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

        // Banco compartilhado: outros testes criam alunos próprios; conta só os do seed (matrículas 2026001…2026010).
        var seedEnrollments = Enumerable.Range(1, 10).Select(n => $"{2026000 + n}").ToList();
        var students = await db.Students.IgnoreQueryFilters().Where(s => seedEnrollments.Contains(s.Enrollment)).ToListAsync(ct);
        Assert.Equal(10, students.Count);
        Assert.All(units, u => Assert.Equal(5, students.Count(s => s.UnitId == u.Id)));
        Assert.Contains(students, s => !s.GuardianApproved);

        var today = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.UtcDateTime);
        var bands = students.Select(s => s.AgeBandAt(today)).Distinct().ToList();
        Assert.Equal(Enum.GetValues<AgeBand>().Length, bands.Count);

        // Banco compartilhado: só as contas do seed (e-mails @helinho.example criadas por ele).
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           join us in db.Users on ur.UserId equals us.Id
                           where us.Email!.EndsWith("@helinho.example")
                           group r by r.Name into g
                           select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!, x => x.Count, ct);
        Assert.Equal(1, roles[RoleNames.Master]);
        Assert.Equal(2, roles[RoleNames.Admin]);
        Assert.Equal(10, roles[RoleNames.Student]);

        // Critérios de aceite da Issue #7: identificadores exatos.
        Assert.Equal(
            ["Unidade Norte (fictícia)", "Unidade Sul (fictícia)"],
            units.Select(u => u.Name).Order().ToArray());
        var north = units.Single(u => u.Name == PeopleSeeder.NorthUnitName);
        var south = units.Single(u => u.Name == PeopleSeeder.SouthUnitName);

        var master = await db.Users.SingleAsync(u => u.Email == "master@helinho.example", ct);
        Assert.Null(master.UnitId);
        var adminNorth = await db.Users.SingleAsync(u => u.Email == "admin.norte@helinho.example", ct);
        Assert.Equal(north.Id, adminNorth.UnitId);
        var adminSouth = await db.Users.SingleAsync(u => u.Email == "admin.sul@helinho.example", ct);
        Assert.Equal(south.Id, adminSouth.UnitId);

        Assert.Equal(13, await db.Users.CountAsync(u => u.Email!.EndsWith("@helinho.example"), ct));
        var expectedEmails = Enumerable.Range(1, 10).Select(n => $"aluno{n:00}@helinho.example").ToArray();
        var studentEmails = await (from s in db.Students.IgnoreQueryFilters()
                                   join u in db.Users on s.UserId equals u.Id
                                   select u.Email!).ToListAsync(ct);
        Assert.Equal(expectedEmails, studentEmails.Order().ToArray());
        Assert.Equal(
            Enumerable.Range(2026001, 10).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            students.Select(s => s.Enrollment).Order().ToArray());

        // Senha nunca em texto puro: hash do Identity (v3, prefixo "AQAAAA"). O banco é compartilhado entre
        // os testes e o seed é idempotente, então a senha da execução original pode ser outra.
        foreach (var user in new[] { master, adminNorth, adminSouth })
        {
            Assert.NotEqual(password, user.PasswordHash);
            Assert.StartsWith("AQAAAA", user.PasswordHash, StringComparison.Ordinal);
        }
    }
}
