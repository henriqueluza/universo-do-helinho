using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public sealed record QuestionListQuery(QuizType? QuizType, AgeBand? AgeBand, QuestionStatus? Status, int Page);

public sealed record QuestionRow(Guid Id, string Statement, QuizType QuizType, AgeBand AgeBand, QuestionStatus Status);

public sealed record QuestionListResult(IReadOnlyList<QuestionRow> Items, int Total, int Page, int TotalPages);

/// <summary>Listagem paginada de questões do painel. Os filtros são opcionais e se combinam com E.</summary>
public static class ListQuestions
{
    public const int PageSize = 20;

    private const int SummaryLength = 90;

    public static async Task<QuestionListResult> HandleAsync(IAppDbContext db, QuestionListQuery query, CancellationToken cancellationToken)
    {
        var questions = db.Questions.AsNoTracking();
        if (query.QuizType is { } type)
        {
            questions = questions.Where(q => q.QuizType == type);
        }

        if (query.AgeBand is { } band)
        {
            questions = questions.Where(q => q.AgeBand == band);
        }

        if (query.Status is { } status)
        {
            questions = questions.Where(q => q.Status == status);
        }

        var total = await questions.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var rows = await questions
            .OrderBy(q => q.QuizType).ThenBy(q => q.AgeBand).ThenBy(q => q.Statement).ThenBy(q => q.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(q => new { q.Id, q.Statement, q.QuizType, q.AgeBand, q.Status })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new QuestionRow(r.Id, Summarize(r.Statement), r.QuizType, r.AgeBand, r.Status)).ToList();
        return new QuestionListResult(items, total, page, totalPages);
    }

    public static string Summarize(string statement) =>
        statement.Length <= SummaryLength ? statement : statement[..(SummaryLength - 1)].TrimEnd() + "…";
}
