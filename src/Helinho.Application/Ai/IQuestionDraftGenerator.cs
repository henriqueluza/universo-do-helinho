using System.Globalization;
using System.Text;
using Helinho.Domain;

namespace Helinho.Application.Ai;

/// <summary>
/// Porta da geração de rascunhos de questões por IA (ADR-0004 §4). De propósito único: não há como mandar
/// texto livre ao provedor por ela. A falha chega como <see cref="QuestionDraftGenerationException"/>.
/// </summary>
public interface IQuestionDraftGenerator
{
    /// <summary>Devolve o que o gerador produziu, sem filtrar: quem decide o que presta é o validador (ADR-0004 §8).</summary>
    Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Tudo o que pode chegar a um provedor. É a fronteira de privacidade (ADR-0004 §11): cinco campos, nenhum
/// identificador de aluno, administrador ou unidade. O construtor público existe para os testes montarem casos
/// inválidos; o código de produção usa <see cref="Create"/>.
/// </summary>
public sealed record QuestionDraftRequest(
    QuizType QuizType, AgeBand AgeBand, int Count, string? Subject, IReadOnlyList<string> ExistingStatements)
{
    public const int MaxCount = 20;
    public const int SubjectMax = 120;
    private const int RawSubjectMax = SubjectMax * 4;
    private const int DigitRunMax = 5;

    /// <exception cref="DomainException">Dado inválido (mensagem em português, sem repetir o que foi digitado).</exception>
    public static QuestionDraftRequest Create(
        QuizType quizType, AgeBand ageBand, int count, string? subject, IReadOnlyList<string> existingStatements)
    {
        ArgumentNullException.ThrowIfNull(existingStatements);
        if (!Enum.IsDefined(quizType) || !Enum.IsDefined(ageBand))
        {
            throw new DomainException("Escolha o tipo de quiz e a faixa etária.");
        }

        if (count is < 1 or > MaxCount)
        {
            throw new DomainException($"A quantidade de sugestões vai de 1 a {MaxCount}.");
        }

        return new QuestionDraftRequest(quizType, ageBand, count, SanitizeSubject(subject), [.. existingStatements]);
    }

    /// <summary>
    /// Saneia o assunto digitado (ADR-0004 §11 e §12): quebras, controles e invisíveis viram espaço, <c>&lt;</c> e
    /// <c>&gt;</c> saem; acima do limite, com <c>@</c> ou com seis dígitos seguidos, o pedido é rejeitado, não cortado.
    /// </summary>
    /// <returns><c>null</c> quando não sobra assunto.</returns>
    /// <exception cref="DomainException">Assunto longo demais ou com cara de e-mail, telefone ou documento.</exception>
    public static string? SanitizeSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // Texto colado muito acima do teto é recusado antes de ser normalizado e copiado.
        if (subject.Length > RawSubjectMax)
        {
            throw new DomainException($"O assunto tem até {SubjectMax} caracteres.");
        }

        // Antes de normalizar: Normalize lança com par substituto quebrado. Depois: a forma KC desdobra variantes de largura.
        var normalized = Spaced(subject).Normalize(NormalizationForm.FormKC);
        var clean = string.Join(' ', Spaced(normalized.Replace("<", "", StringComparison.Ordinal).Replace(">", "", StringComparison.Ordinal))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > SubjectMax)
        {
            throw new DomainException($"O assunto tem até {SubjectMax} caracteres.");
        }

        var digitRun = 0;
        foreach (var c in clean)
        {
            digitRun = char.IsDigit(c) ? digitRun + 1 : 0;
            if (c == '@' || digitRun > DigitRunMax)
            {
                throw new DomainException("Não escreva e-mail, telefone nem número de documento no assunto.");
            }
        }

        return clean.Length == 0 ? null : clean;
    }

    private static string Spaced(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            span[i] = IsSeparatorOrHidden(source[i]) ? ' ' : source[i];
        }
    });

    /// <summary>Controle, espaço de qualquer tipo, invisível ou de direção, substituto, uso privado ou não atribuído. Também usado pelo montador.</summary>
    public static bool IsSeparatorOrHidden(char c) => char.IsControl(c) || char.IsWhiteSpace(c)
        || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;

    /// <summary>Só o nome do tipo: o assunto e os enunciados não vazam por log nem por interpolação.</summary>
    public override string ToString() => nameof(QuestionDraftRequest);
}

/// <summary>Um rascunho como o gerador o devolveu. O esperado são 4 alternativas e índice de 0 a 3, mas nada aqui garante.</summary>
public sealed record QuestionDraft(string Statement, IReadOnlyList<string> Options, int CorrectIndex, string Explanation)
{
    /// <summary>Só o nome do tipo: texto gerado não vaza por log nem por interpolação.</summary>
    public override string ToString() => nameof(QuestionDraft);
}

public sealed record QuestionDraftBatch(IReadOnlyList<QuestionDraft> Drafts, string PromptVersion, AiUsage Usage);

/// <summary>Uso de uma geração. <paramref name="Model"/> é o modelo que respondeu, como o provedor informou.</summary>
public sealed record AiUsage(
    string Provider, string Model, int InputTokens, int OutputTokens, TimeSpan Duration, string? ProviderRequestId = null);

/// <summary>Motivo fechado de uma falha de geração (ADR-0004 §4). <see cref="Disabled"/>: a IA está desligada.</summary>
public enum QuestionDraftFailure
{
    Timeout,
    ProviderUnavailable,
    RateLimited,
    Refused,
    InvalidOutput,
    Misconfigured,
    Disabled,
}

/// <summary>A mensagem só traz o motivo: nunca prompt, assunto, texto gerado nem chave.</summary>
public sealed class QuestionDraftGenerationException(QuestionDraftFailure reason)
    : Exception($"A geração de rascunhos falhou: {reason}.")
{
    public QuestionDraftFailure Reason { get; } = reason;
}
