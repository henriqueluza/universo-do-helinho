namespace Helinho.Domain.Entities;

public enum QuestionStatus
{
    Draft,
    Approved,
}

/// <summary>De onde veio o conteúdo (ADR-0004 §9.2). Definida na criação e nunca alterada.</summary>
public enum QuestionOrigin
{
    Team,
    Ai,
}

public class Question
{
    public const int PromptVersionMax = 60;

    private readonly List<QuestionOption> _options = [];

    private Question()
    {
    }

    /// <summary>Exatamente 4 alternativas, uma correta. As chaves a–d são atribuídas na ordem.</summary>
    public Question(QuizType quizType, AgeBand ageBand, string statement, string explanation, IReadOnlyList<(string Label, bool IsCorrect)> options)
    {
        Validate(options);
        QuizType = quizType;
        AgeBand = ageBand;
        Statement = Guard.Required(statement, "Enunciado");
        Explanation = Guard.Required(explanation, "Explicação");
        _options.AddRange(options.Select((o, i) => new QuestionOption(Id, ((char)('a' + i)).ToString(), o.Label, o.IsCorrect)));
    }

    /// <summary>
    /// Questão criada a partir de conteúdo gerado por IA, por decisão de uma pessoa (aprovar ou editar e salvar).
    /// A proveniência aponta para o pedido (<c>AiGenerationJobs</c>), copia a versão do prompt e a hora da geração.
    /// </summary>
    public static Question FromAi(
        QuizType quizType, AgeBand ageBand, string statement, string explanation, IReadOnlyList<(string Label, bool IsCorrect)> options,
        Guid aiJobId, string promptVersion, DateTimeOffset generatedAt)
    {
        var version = Guard.Required(promptVersion, "Versão do prompt");
        return new Question(quizType, ageBand, statement, explanation, options)
        {
            Origin = QuestionOrigin.Ai,
            AiJobId = Guard.Required(aiJobId, "Pedido de IA"),
            PromptVersion = version.Length > PromptVersionMax ? version[..PromptVersionMax] : version,
            GeneratedAt = generatedAt.ToUniversalTime(),
        };
    }

    /// <summary>
    /// Edita a questão (mesmas regras da criação). Qualquer edição devolve a questão a rascunho. A origem não muda;
    /// em questão de origem IA, <paramref name="editedAt"/> registra a edição humana (ADR-0004 §9.4).
    /// </summary>
    public void Update(
        QuizType quizType, AgeBand ageBand, string statement, string explanation, IReadOnlyList<(string Label, bool IsCorrect)> options,
        DateTimeOffset? editedAt = null)
    {
        Validate(options);
        var newStatement = Guard.Required(statement, "Enunciado");
        var newExplanation = Guard.Required(explanation, "Explicação");
        foreach (var option in options)
        {
            Guard.Required(option.Label, "Alternativa");
        }

        // Casa por chave (a–d): a ordem em que o EF carrega a lista não é garantida.
        var ordered = _options.OrderBy(o => o.Key, StringComparer.Ordinal).ToList();
        for (var i = 0; i < 4; i++)
        {
            ordered[i].Set(options[i].Label, options[i].IsCorrect);
        }

        QuizType = quizType;
        AgeBand = ageBand;
        Statement = newStatement;
        Explanation = newExplanation;
        Status = QuestionStatus.Draft;
        if (Origin == QuestionOrigin.Ai && editedAt is { } at)
        {
            AiEditedAt = at.ToUniversalTime();
        }
    }

    private static void Validate(IReadOnlyList<(string Label, bool IsCorrect)> options)
    {
        if (options.Count != 4 || options.Count(o => o.IsCorrect) != 1)
        {
            throw new DomainException("A questão precisa de exatamente 4 alternativas, uma correta.");
        }
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public QuizType QuizType { get; private set; }

    public AgeBand AgeBand { get; private set; }

    public string Statement { get; private set; } = "";

    public string Explanation { get; private set; } = "";

    public QuestionStatus Status { get; private set; } = QuestionStatus.Draft;

    public QuestionOrigin Origin { get; private set; } = QuestionOrigin.Team;

    /// <summary>O pedido que gerou o conteúdo; nulo em questão da equipe.</summary>
    public Guid? AiJobId { get; private set; }

    public string? PromptVersion { get; private set; }

    public DateTimeOffset? GeneratedAt { get; private set; }

    /// <summary>Última edição humana de uma questão de origem IA; nulo se nunca foi editada depois de criada.</summary>
    public DateTimeOffset? AiEditedAt { get; private set; }

    public IReadOnlyList<QuestionOption> Options => _options;

    /// <summary>Só questão aprovada chega ao jogo.</summary>
    public void Approve() => Status = QuestionStatus.Approved;
}

public class QuestionOption
{
    private QuestionOption()
    {
    }

    internal QuestionOption(Guid questionId, string key, string label, bool isCorrect)
    {
        QuestionId = questionId;
        Key = key;
        Label = Guard.Required(label, "Alternativa");
        IsCorrect = isCorrect;
    }

    internal void Set(string label, bool isCorrect)
    {
        Label = Guard.Required(label, "Alternativa");
        IsCorrect = isCorrect;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid QuestionId { get; private set; }

    /// <summary>Identificador do contrato (`option_id`): a, b, c ou d.</summary>
    public string Key { get; private set; } = "";

    public string Label { get; private set; } = "";

    public bool IsCorrect { get; private set; }
}
