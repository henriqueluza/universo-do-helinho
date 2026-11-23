using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Infrastructure.Ai;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Issue #231, invariante de privacidade do ADR-0004 §11: o que seria enviado ao provedor, montado sobre o banco
/// semeado, não traz dado de aluno, de administrador nem de unidade. Nada é enviado: o teste só inspeciona o envelope.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AiPromptPrivacyTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task O_envelope_montado_sobre_o_seed_nao_traz_dado_de_aluno_administrador_nem_unidade()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var statements = await db.Questions.Where(q => q.QuizType == QuizType.Ambiental && q.AgeBand == AgeBand.Age8To10)
            .Select(q => q.Statement).ToListAsync(Ct);
        var students = await db.Students.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct); // todas as unidades
        var users = await db.Users.AsNoTracking().ToListAsync(Ct);
        var units = await db.Units.AsNoTracking().ToListAsync(Ct);

        var prompt = QuestionPromptBuilder.Build(QuestionDraftRequest.Create(QuizType.Ambiental, AgeBand.Age8To10, 10, "reciclagem", statements));
        var sent = prompt.SystemText + "\n" + prompt.UserText;

        string?[] values =
        [
            .. students.SelectMany(s => new[] { s.FullName, s.Enrollment, s.Rg, s.Cpf, s.Address, s.GuardianName, s.Id.ToString(), s.UserId.ToString() }),
            .. users.SelectMany(u => new[] { u.UserName, u.Email, u.PhoneNumber, u.Id.ToString() }),
            .. units.SelectMany(u => new[] { u.Name, u.Id.ToString() }),
        ];
        Assert.NotEmpty(statements);
        Assert.NotEmpty(students);
        Assert.NotEmpty(users);
        Assert.NotEmpty(units);
        Assert.Contains(statements[0], sent, StringComparison.Ordinal); // o envelope foi mesmo montado com dados do banco
        Assert.All(values.OfType<string>().Where(value => value.Trim().Length > 0),
            value => Assert.DoesNotContain(value, sent, StringComparison.OrdinalIgnoreCase));
    }
}
