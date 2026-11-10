using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

#pragma warning disable CA1859 // o teste consulta de propósito pela porta IAppDbContext, como os casos de uso

namespace Helinho.IntegrationTests;

/// <summary>T1 no nível de dados: o filtro global de Student isola unidades por IAppDbContext (dados sintéticos).</summary>
[Collection(PostgresCollection.Name)]
public partial class SegregacaoUnidadeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _database = $"segregacao_{Guid.NewGuid():N}";
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly Unit _unitA = new("Unidade A");
    private readonly Unit _unitB = new("Unidade B");
    private Guid _studentB;

    private sealed class FakeUser(string? role, Guid? unitId) : ICurrentUser
    {
        public bool IsAuthenticated => role is not null || unitId is not null;

        public Guid? UserId => null;

        public Guid? UnitId => unitId;

        public bool IsInRole(string r) => r == role;
    }

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var seed = NewContext(new FakeUser(RoleNames.Master, null));
        await seed.Database.MigrateAsync(ct);
        var users = Enumerable.Range(0, 4).Select(i => new AppUser { UserName = $"seg{i}", UnitId = i < 2 ? _unitA.Id : _unitB.Id }).ToArray();
        seed.Users.AddRange(users);
        seed.Units.AddRange(_unitA, _unitB);
        var b1 = NewStudent(users[2].Id, _unitB.Id, "SEG-B1");
        seed.Students.AddRange(NewStudent(users[0].Id, _unitA.Id, "SEG-A1"), NewStudent(users[1].Id, _unitA.Id, "SEG-A2"), b1, NewStudent(users[3].Id, _unitB.Id, "SEG-B2"));
        await seed.SaveChangesAsync(ct);
        _studentB = b1.Id;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Admin_da_unidade_A_nao_le_nao_conta_e_nao_altera_aluno_da_B()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = NewContext(new FakeUser(RoleNames.Admin, _unitA.Id));
        IAppDbContext db = context;

        Assert.Equal(["SEG-A1", "SEG-A2"], (await db.Students.Select(s => s.Enrollment).ToListAsync(ct)).Order());
        Assert.Equal(2, await db.Students.CountAsync(ct));
        Assert.False(await db.Students.AnyAsync(s => s.UnitId == _unitB.Id, ct));
        Assert.Null(await db.Students.FindAsync([_studentB], ct));
        Assert.Null(await db.Students.SingleOrDefaultAsync(s => s.Id == _studentB, ct));

        var changed = await db.Students.Where(s => s.Id == _studentB).ExecuteUpdateAsync(s => s.SetProperty(x => x.Xp, 999), ct);
        Assert.Equal(0, changed);
        await using var master = NewContext(new FakeUser(RoleNames.Master, null));
        Assert.Equal(0, (await master.Students.SingleAsync(s => s.Id == _studentB, ct)).Xp);
    }

    [Fact]
    public async Task Master_le_as_duas_unidades()
    {
        await using var context = NewContext(new FakeUser(RoleNames.Master, null));
        IAppDbContext db = context;

        var units = await db.Students.Select(s => s.UnitId).Distinct().ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains(_unitA.Id, units);
        Assert.Contains(_unitB.Id, units);
    }

    [Fact]
    public async Task Usuario_sem_unidade_e_sem_papel_nao_le_aluno()
    {
        await using var context = NewContext(new FakeUser(null, null));
        IAppDbContext db = context;

        Assert.Equal(0, await db.Students.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Codigo_de_producao_nao_ignora_filtro_global_sem_justificativa()
    {
        var root = FindRepositoryRoot();
        var violations = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => UnjustifiedBypasses(File.ReadAllLines(f)).Select(n => $"{Path.GetRelativePath(root, f)}:{n}"))
            .ToList();

        Assert.True(violations.Count == 0, "IgnoreQueryFilters sem '// ignora-filtro: <motivo>': " + string.Join(", ", violations));
    }

    [Fact]
    public void Verificador_detecta_bypass_sem_justificativa()
    {
        Assert.Equal([1], UnjustifiedBypasses(["db.Students.IgnoreQueryFilters()"]));
        Assert.Equal([2], UnjustifiedBypasses(["// comentário qualquer", "db.Students.IgnoreQueryFilters();"]));
        Assert.Empty(UnjustifiedBypasses(["// ignora-filtro: job de limpeza do master", "db.Students.IgnoreQueryFilters();"]));
    }

    [GeneratedRegex(@"//\s*ignora-filtro:\s*\S")]
    private static partial Regex Justification();

    private static IEnumerable<int> UnjustifiedBypasses(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("IgnoreQueryFilters", StringComparison.Ordinal))
            {
                continue;
            }

            var justified = Justification().IsMatch(lines[i]) || (i > 0 && Justification().IsMatch(lines[i - 1]));
            if (!justified)
            {
                yield return i + 1;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Helinho.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    private AppDbContext NewContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, user);

    private static Student NewStudent(Guid userId, Guid unitId, string enrollment) =>
        new(userId, unitId, enrollment, "Aluno Fictício", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1", new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", Today);
}
