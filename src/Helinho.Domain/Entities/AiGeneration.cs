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

/// <summary>Uso de uma geração concluída. <paramref name="Model"/> é o modelo que respondeu, como o provedor informou.</summary>
public sealed record AiJobUsage(string Provider, string Model, string PromptVersion, int InputTokens, int OutputTokens, TimeSpan Duration);

public sealed record AiJobCounts(int Generated, int Valid, int Invalid, int Duplicate);

/// <summary>
/// Um pedido de geração por IA, em lote ou avulso (ADR-0004 §9.2). É também o registro de uso: quem pediu, quando,
/// modelo e tokens. Guarda só o identificador do usuário, nunca nome nem e-mail.
/// </summary>
public class AiGenerationJob
{
    public const int MaxCount = 20;
    public const int SubjectMax = 120;
    public const int FailureReasonMax = 30;
    public const int ProviderMax = 40;
    public const int ModelMax = 100;
    public const int PromptVersionMax = 60;

    private AiGenerationJob()
    {
    }

    /// <param name="subject">Já saneado por quem chama; vazio vira <c>null</c>.</param>
    public AiGenerationJob(
        AiJobKind kind, QuizType quizType, AgeBand ageBand, int requestedCount, string? subject, Guid requestedByUserId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(quizType) || !Enum.IsDefined(ageBand))
        {
            throw new DomainException("Escolha o tipo de quiz e a faixa etária.");
        }

        if (requestedCount is < 1 or > MaxCount || (kind == AiJobKind.Single && requestedCount != 1))
        {
            throw new DomainException($"A quantidade de sugestões vai de 1 a {MaxCount}.");
        }

        if (subject?.Length > SubjectMax)
        {
            throw new DomainException($"O assunto tem até {SubjectMax} caracteres.");
        }

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

    /// <summary>Texto livre de quem pediu, já saneado. Hoje sem prazo de guarda (pendência IA-2); <see cref="EraseSubject"/> é o ponto de corte.</summary>
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

    public bool IsFinished => Status is AiJobStatus.Succeeded or AiJobStatus.Failed;

    /// <summary>Marca "gerando". Pode ser repetida sem efeito: só age sobre pedido pendente.</summary>
    public void Start(DateTimeOffset now)
    {
        if (Status == AiJobStatus.Pending)
        {
            Status = AiJobStatus.Processing;
            StartedAt = now.ToUniversalTime();
        }
    }

    public void Succeed(AiJobUsage usage, AiJobCounts counts, DateTimeOffset now)
    {
        Finish(AiJobStatus.Succeeded, now);
        // O modelo vem do provedor: cortado no limite da coluna, para um valor inesperado não derrubar a gravação.
        Provider = Cap(usage.Provider, ProviderMax);
        Model = Cap(usage.Model, ModelMax);
        PromptVersion = Cap(usage.PromptVersion, PromptVersionMax);
        InputTokens = Math.Max(0, usage.InputTokens);
        OutputTokens = Math.Max(0, usage.OutputTokens);
        DurationMs = (int)Math.Clamp(usage.Duration.TotalMilliseconds, 0, int.MaxValue);
        GeneratedCount = Math.Max(0, counts.Generated);
        ValidCount = Math.Max(0, counts.Valid);
        InvalidCount = Math.Max(0, counts.Invalid);
        DuplicateCount = Math.Max(0, counts.Duplicate);
    }

    /// <param name="reasonCode">Minúsculas, dígitos e separadores, até 30 caracteres. Mensagem de exceção não entra aqui.</param>
    public void Fail(string reasonCode, DateTimeOffset now)
    {
        if (!IsCode(reasonCode))
        {
            throw new ArgumentException("Motivo de falha fora do vocabulário fechado.", nameof(reasonCode));
        }

        Finish(AiJobStatus.Failed, now);
        FailureReason = reasonCode;
    }

    public void EraseSubject() => Subject = null;

    private void Finish(AiJobStatus status, DateTimeOffset now)
    {
        if (IsFinished)
        {
            throw new InvalidOperationException("O pedido de IA já foi concluído.");
        }

        Status = status;
        FinishedAt = now.ToUniversalTime();
    }

    private static string Cap(string value, int max) => value.Length > max ? value[..max] : value;

    private static bool IsCode(string? code) =>
        code is { Length: > 0 and <= FailureReasonMax } && code.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.');
}

/// <summary>
/// Um rascunho válido de um pedido em lote (ADR-0004 §9.2). Não é questão: só vira <see cref="Question"/> quando uma
/// pessoa aprova ou edita e salva. A decisão só vale a partir de <see cref="AiSuggestionStatus.Pending"/>.
/// </summary>
public class AiQuestionSuggestion
{
    private AiQuestionSuggestion()
    {
    }

    public AiQuestionSuggestion(Guid jobId, int position, string statement, IReadOnlyList<string> options, int correctIndex, string explanation)
    {
        if (options.Count != 4 || correctIndex is < 0 or > 3 || position < 0)
        {
            throw new DomainException("A sugestão precisa de exatamente 4 alternativas, uma correta.");
        }

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

    public void Approve(Guid userId, Guid questionId, DateTimeOffset now) =>
        Decide(AiSuggestionStatus.Approved, userId, Guard.Required(questionId, "Questão"), now);

    public void MarkEdited(Guid userId, Guid questionId, DateTimeOffset now) =>
        Decide(AiSuggestionStatus.Edited, userId, Guard.Required(questionId, "Questão"), now);

    public void Discard(Guid userId, DateTimeOffset now) => Decide(AiSuggestionStatus.Discarded, userId, null, now);

    private void Decide(AiSuggestionStatus status, Guid userId, Guid? questionId, DateTimeOffset now)
    {
        if (Status != AiSuggestionStatus.Pending)
        {
            throw new DomainException("Esta sugestão já foi revisada.");
        }

        DecidedByUserId = Guard.Required(userId, "Quem decidiu");
        Status = status;
        QuestionId = questionId;
        DecidedAt = now.ToUniversalTime();
    }
}
