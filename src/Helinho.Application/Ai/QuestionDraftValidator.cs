using System.Globalization;
using System.Text;
using Helinho.Application.Abstractions;
using Helinho.Application.Questions;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Ai;

/// <summary>Motivo fechado do descarte de um rascunho. O texto descartado não é guardado nem vai para log (ADR-0004 §8).</summary>
public enum DraftRejection
{
    Malformed,
    OverLimit,
    Incomplete,
    WrongOptionCount,
    InvalidCorrectIndex,
    DuplicateOptions,
    TooLong,
    UnsafeText,
    Duplicate,
}

public sealed record QuestionDraftValidation(IReadOnlyList<QuestionDraft> Accepted, IReadOnlyDictionary<DraftRejection, int> Rejections)
{
    public int DuplicateCount => Rejections.GetValueOrDefault(DraftRejection.Duplicate);
    public int InvalidCount => Rejections.Values.Sum() - DuplicateCount;
}

/// <summary>
/// Valida a saída de qualquer gerador, inclusive do fake, como dado não confiável (ADR-0004 §8). Texto fora da
/// regra é rejeitado, nunca cortado nem limpo. Os aceitos saem aparados.
/// </summary>
public static class QuestionDraftValidator
{
    private static readonly string[] Links = ["http://", "https://", "www."];

    /// <param name="existingStatements">Enunciados do mesmo tipo e faixa já guardados (<see cref="ListExistingStatementsAsync"/>).</param>
    /// <param name="limit">Quantidade pedida: o que vier além dela é descartado e contado.</param>
    public static QuestionDraftValidation Validate(IReadOnlyList<QuestionDraft> drafts, IEnumerable<string> existingStatements, int limit)
    {
        var seen = existingStatements.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var accepted = new List<QuestionDraft>();
        var rejections = new Dictionary<DraftRejection, int>();
        for (var position = 0; position < drafts.Count; position++)
        {
            var draft = drafts[position];
            var rejection = position >= Math.Min(limit, QuestionDraftRequest.MaxCount) ? DraftRejection.OverLimit : Check(draft);
            // A duplicata é a última regra: só um rascunho que seria aceito ocupa o enunciado no lote.
            if (rejection is null && !seen.Add(Normalize(draft.Statement)))
            {
                rejection = DraftRejection.Duplicate;
            }

            if (rejection is { } reason)
            {
                rejections[reason] = rejections.GetValueOrDefault(reason) + 1;
                continue;
            }

            accepted.Add(new QuestionDraft(draft.Statement.Trim(), [.. draft.Options.Select(o => o.Trim())], draft.CorrectIndex, draft.Explanation.Trim()));
        }

        return new QuestionDraftValidation(accepted, rejections);
    }

    /// <summary>Forma de comparação: minúsculas, sem acentos, pontuação e símbolos viram espaço, espaços repetidos viram um.</summary>
    public static string Normalize(string statement)
    {
        // Pelas runas: um par substituto quebrado vira U+FFFD em vez de derrubar a normalização.
        var decomposed = string.Concat(statement.EnumerateRunes().Select(rune => rune.ToString())).Normalize(NormalizationForm.FormD);
        var letters = decomposed.Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return string.Join(' ', string.Concat(letters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Enunciados das questões do mesmo tipo e faixa, em qualquer estado: a base da deduplicação.</summary>
    public static async Task<IReadOnlyList<string>> ListExistingStatementsAsync(
        IAppDbContext db, QuizType quizType, AgeBand ageBand, CancellationToken cancellationToken) =>
        await db.Questions.AsNoTracking().Where(q => q.QuizType == quizType && q.AgeBand == ageBand)
            .Select(q => q.Statement).ToListAsync(cancellationToken);

    private static DraftRejection? Check(QuestionDraft draft)
    {
        if (ReferenceEquals(draft, QuestionDraftOutputParser.MalformedItem))
        {
            return DraftRejection.Malformed;
        }

        if (draft.Options is null || draft.Options.Count != 4)
        {
            return DraftRejection.WrongOptionCount;
        }

        string?[] texts = [draft.Statement, draft.Explanation, .. draft.Options];
        if (texts.Any(string.IsNullOrWhiteSpace))
        {
            return DraftRejection.Incomplete;
        }

        if (draft.CorrectIndex is < 0 or > 3)
        {
            return DraftRejection.InvalidCorrectIndex;
        }

        if (draft.Options.Select(Normalize).Distinct(StringComparer.Ordinal).Count() != 4)
        {
            return DraftRejection.DuplicateOptions;
        }

        if (draft.Statement.Trim().Length > SaveQuestion.StatementMax || draft.Explanation.Trim().Length > SaveQuestion.ExplanationMax
            || draft.Options.Any(o => o.Trim().Length > SaveQuestion.OptionMax))
        {
            return DraftRejection.TooLong;
        }

        return texts.OfType<string>().Any(IsUnsafe) ? DraftRejection.UnsafeText : null;
    }

    /// <summary>Controle, invisível ou de direção, par substituto quebrado, marcação (<c>&lt;</c> + letra, <c>/</c> ou <c>!</c>) ou endereço de site.</summary>
    private static bool IsUnsafe(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : ' ';
            var paired = char.IsHighSurrogate(c) && char.IsLowSurrogate(next) || char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]);
            if (char.IsControl(c) || (char.IsSurrogate(c) && !paired) || (c == '<' && (char.IsLetter(next) || next is '/' or '!'))
                || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                return true;
            }
        }

        return Links.Any(link => text.Contains(link, StringComparison.OrdinalIgnoreCase));
    }
}
