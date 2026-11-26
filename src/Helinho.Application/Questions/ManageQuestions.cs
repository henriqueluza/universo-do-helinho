using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Questions;

public sealed record QuestionRow(Guid Id, string Statement, QuizType QuizType, AgeBand AgeBand, QuestionStatus Status);

public sealed record QuestionDetail(
    Guid Id, QuizType QuizType, AgeBand AgeBand, string Statement, string Explanation,
    IReadOnlyList<string> Options, int CorrectIndex, QuestionStatus Status);

/// <summary>Listagem do painel: rascunhos primeiro (pendentes de revisão), depois as aprovadas.</summary>
public static class ListQuestions
{
    public static async Task<IReadOnlyList<QuestionRow>> HandleAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        var all = await db.Questions.AsNoTracking()
            .Select(q => new QuestionRow(q.Id, q.Statement, q.QuizType, q.AgeBand, q.Status))
            .ToListAsync(cancellationToken);
        return [.. all.OrderBy(q => q.Status).ThenBy(q => q.QuizType).ThenBy(q => q.AgeBand).ThenBy(q => q.Statement)];
    }
}

public static class GetQuestion
{
    public static async Task<QuestionDetail?> HandleAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var q = await db.Questions.AsNoTracking().Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (q is null)
        {
            return null;
        }

        var options = q.Options.OrderBy(o => o.Key).ToList();
        return new QuestionDetail(q.Id, q.QuizType, q.AgeBand, q.Statement, q.Explanation,
            [.. options.Select(o => o.Label)], options.FindIndex(o => o.IsCorrect), q.Status);
    }
}

/// <summary>
/// Cria (rascunho) ou edita (volta a rascunho) uma questão. A validação é do domínio. Com <c>editedAt</c>, a edição de
/// questão de origem IA registra <c>AiEditedAt</c> (ADR-0004 §9.4, D5); questão da equipe ignora a hora.
/// </summary>
public static class SaveQuestion
{
    public const int StatementMax = 500;
    public const int ExplanationMax = 1000;
    public const int OptionMax = 300;

    /// <returns>O id da questão, ou <c>null</c> se <paramref name="id"/> não existe.</returns>
    /// <exception cref="DomainException">Dado inválido (mensagem em português).</exception>
    public static async Task<Guid?> HandleAsync(
        IAppDbContext db, Guid? id, QuizType quizType, AgeBand ageBand, string? statement, string? explanation,
        IReadOnlyList<string?> options, int? correctIndex, CancellationToken cancellationToken, DateTimeOffset? editedAt = null)
    {
        var items = ValidatedOptions(quizType, ageBand, statement, explanation, options, correctIndex);
        if (id is null)
        {
            var created = new Question(quizType, ageBand, statement ?? "", explanation ?? "", items);
            db.Questions.Add(created);
            await db.SaveChangesAsync(cancellationToken);
            return created.Id;
        }

        var question = await db.Questions.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (question is null)
        {
            return null;
        }

        question.Update(quizType, ageBand, statement ?? "", explanation ?? "", items, editedAt);
        await db.SaveChangesAsync(cancellationToken);
        return question.Id;
    }

    /// <summary>As checagens de tipo, faixa e tamanho, e as alternativas com a correta marcada. Também usada ao criar questão a partir de sugestão da IA.</summary>
    /// <exception cref="DomainException">Dado inválido (mensagem em português).</exception>
    internal static List<(string Label, bool IsCorrect)> ValidatedOptions(
        QuizType quizType, AgeBand ageBand, string? statement, string? explanation, IReadOnlyList<string?> options, int? correctIndex)
    {
        if (!Enum.IsDefined(quizType) || !Enum.IsDefined(ageBand))
        {
            throw new DomainException("Escolha o tipo de quiz e a faixa etária.");
        }

        if (statement?.Trim().Length > StatementMax || explanation?.Trim().Length > ExplanationMax || options.Any(o => o?.Trim().Length > OptionMax))
        {
            throw new DomainException("Algum texto passou do tamanho máximo.");
        }

        return [.. options.Select((o, i) => (o ?? "", i == correctIndex))];
    }
}

public static class ApproveQuestion
{
    public static async Task<bool> HandleAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var question = await db.Questions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (question is null)
        {
            return false;
        }

        question.Approve();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
