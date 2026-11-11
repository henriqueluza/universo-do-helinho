namespace Helinho.Domain.Entities;

public enum QuestionStatus
{
    Draft,
    Approved,
}

public class Question
{
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

    /// <summary>Edita a questão (mesmas regras da criação). Qualquer edição devolve a questão a rascunho.</summary>
    public void Update(QuizType quizType, AgeBand ageBand, string statement, string explanation, IReadOnlyList<(string Label, bool IsCorrect)> options)
    {
        Validate(options);
        var newStatement = Guard.Required(statement, "Enunciado");
        var newExplanation = Guard.Required(explanation, "Explicação");
        for (var i = 0; i < 4; i++)
        {
            _options[i].Set(options[i].Label, options[i].IsCorrect);
        }

        QuizType = quizType;
        AgeBand = ageBand;
        Statement = newStatement;
        Explanation = newExplanation;
        Status = QuestionStatus.Draft;
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
