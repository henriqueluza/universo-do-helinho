namespace Helinho.Domain.Entities;

#pragma warning disable CA1720 // nome do ADR-0004 §9.2: pedido avulso ("Sugerir"), não o tipo numérico
public enum AiJobKind
{
    Batch,
    Single,
}
#pragma warning restore CA1720

public enum AiJobStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
}

public enum AiSuggestionStatus
{
    Pending,
    Approved,
    Edited,
    Discarded,
}

/// <summary>
/// Um pedido de geração por IA, em lote ou avulso (ADR-0004 §9.2). É também o registro de uso: quem pediu, quando,
/// modelo e tokens. Guarda só o identificador do usuário, nunca nome nem e-mail.
/// </summary>
public class AiGenerationJob
{
    public const int SubjectMax = 120;
    public const int FailureReasonMax = 30;
    public const int ProviderMax = 40;
    public const int ModelMax = 100;
    public const int PromptVersionMax = 60;

    private AiGenerationJob()
    {
    }

    /// <param name="subject">Já saneado por quem chama; vazio vira <c>null</c>. As regras de entrada e as transições são da Issue #263.</param>
    public AiGenerationJob(
        AiJobKind kind, QuizType quizType, AgeBand ageBand, int requestedCount, string? subject, Guid requestedByUserId, DateTimeOffset now)
    {
        Kind = kind;
        QuizType = quizType;
        AgeBand = ageBand;
        RequestedCount = requestedCount;
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject;
        RequestedByUserId = Guard.Required(requestedByUserId, "Quem pediu");
        CreatedAt = now.ToUniversalTime();
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public AiJobKind Kind { get; private set; }
    public QuizType QuizType { get; private set; }
    public AgeBand AgeBand { get; private set; }
    public int RequestedCount { get; private set; }

    /// <summary>Texto livre de quem pediu, já saneado. Hoje sem prazo de guarda (pendência IA-2); a coluna é anulável.</summary>
    public string? Subject { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public AiJobStatus Status { get; private set; } = AiJobStatus.Pending;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Código de vocabulário fechado (<c>ai_timeout</c>, <c>expired</c>), nunca texto livre.</summary>
    public string? FailureReason { get; private set; }
    public string? Provider { get; private set; }
    public string? Model { get; private set; }
    public string? PromptVersion { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public int? DurationMs { get; private set; }
    public int GeneratedCount { get; private set; }
    public int ValidCount { get; private set; }
    public int InvalidCount { get; private set; }
    public int DuplicateCount { get; private set; }
}

/// <summary>
/// Um rascunho válido de um pedido em lote (ADR-0004 §9.2). Não é questão: só vira <see cref="Question"/> quando uma
/// pessoa aprova ou edita e salva.
/// </summary>
public class AiQuestionSuggestion
{
    private AiQuestionSuggestion()
    {
    }

    public AiQuestionSuggestion(Guid jobId, int position, string statement, IReadOnlyList<string> options, int correctIndex, string explanation)
    {
        JobId = Guard.Required(jobId, "Pedido de IA");
        Position = position;
        Statement = Guard.Required(statement, "Enunciado");
        OptionA = Guard.Required(options[0], "Alternativa");
        OptionB = Guard.Required(options[1], "Alternativa");
        OptionC = Guard.Required(options[2], "Alternativa");
        OptionD = Guard.Required(options[3], "Alternativa");
        CorrectIndex = correctIndex;
        Explanation = Guard.Required(explanation, "Explicação");
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid JobId { get; private set; }
    public int Position { get; private set; }
    public string Statement { get; private set; } = "";
    public string OptionA { get; private set; } = "";
    public string OptionB { get; private set; } = "";
    public string OptionC { get; private set; } = "";
    public string OptionD { get; private set; } = "";
    public int CorrectIndex { get; private set; }
    public string Explanation { get; private set; } = "";
    public AiSuggestionStatus Status { get; private set; } = AiSuggestionStatus.Pending;
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>A questão criada a partir desta sugestão (aprovada ou editada).</summary>
    public Guid? QuestionId { get; private set; }

    /// <summary>Quando o texto foi apagado pela retenção (ADR-0004 D7): os campos de texto ficam vazios e a linha permanece.</summary>
    public DateTimeOffset? ContentPurgedAt { get; private set; }

    public IReadOnlyList<string> Options => [OptionA, OptionB, OptionC, OptionD];
}
