using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #236: a deduplicação consulta o banco por tipo e faixa, em qualquer estado (ADR-0004 §8).</summary>
[Collection(PostgresCollection.Name)]
public class AiDraftDeduplicationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Question NewQuestion(QuizType type, AgeBand band, string statement) =>
        new(type, band, statement, "Explicação sintética.", [("Um", true), ("Dois", false), ("Três", false), ("Quatro", false)]);

    [Fact]
    public async Task Rascunho_igual_a_questao_do_mesmo_tipo_e_faixa_e_duplicata_e_o_de_outra_faixa_nao()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var approved = NewQuestion(QuizType.Cultural, AgeBand.Age11To12, "Qual é o sintético aprovado número 236?");
        approved.Approve();
        db.Questions.AddRange(
            approved,
            NewQuestion(QuizType.Cultural, AgeBand.Age11To12, "Qual é o sintético em rascunho número 236?"),
            NewQuestion(QuizType.Cultural, AgeBand.Age13To14, "Qual é o sintético de outra faixa número 236?"),
            NewQuestion(QuizType.Esportivo, AgeBand.Age11To12, "Qual é o sintético de outro tipo número 236?"));
        await db.SaveChangesAsync(Ct);

        var existing = await QuestionDraftValidator.ListExistingStatementsAsync(db, QuizType.Cultural, AgeBand.Age11To12, Ct);
        QuestionDraft Draft(string statement) => new(statement, ["Um", "Dois", "Três", "Quatro"], 0, "Explicação sintética.");
        var result = QuestionDraftValidator.Validate(
            [
                Draft("QUAL E O SINTETICO APROVADO, NUMERO 236"), Draft("qual é o sintético em rascunho número 236"),
                Draft("Qual é o sintético de outra faixa número 236?"), Draft("Qual é o sintético de outro tipo número 236?"),
            ],
            existing, limit: 20);

        Assert.Contains(approved.Statement, existing);
        Assert.DoesNotContain(existing, s => s.Contains("outra faixa", StringComparison.Ordinal) || s.Contains("outro tipo", StringComparison.Ordinal));
        Assert.Equal((2, 0), (result.DuplicateCount, result.InvalidCount));
        Assert.Equal(2, result.Accepted.Count);
    }
}
