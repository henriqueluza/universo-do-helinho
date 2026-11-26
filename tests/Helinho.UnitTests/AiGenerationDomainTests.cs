using Helinho.Domain;
using Helinho.Domain.Entities;

namespace Helinho.UnitTests;

/// <summary>Issue #257: proveniência da questão (ADR-0004 §9.2 e §9.4).</summary>
public class AiGenerationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly (string, bool)[] Options = [("A", true), ("B", false), ("C", false), ("D", false)];

    [Fact]
    public void Questao_do_construtor_nasce_da_equipe_e_a_de_FromAi_nasce_com_a_proveniencia()
    {
        var team = new Question(QuizType.Ambiental, AgeBand.Age8To10, "Enunciado", "Explicação", Options);
        var jobId = Guid.NewGuid();
        var ai = Question.FromAi(QuizType.Ambiental, AgeBand.Age8To10, "Enunciado", "Explicação", Options, jobId, "gerar-questoes.v1", Now);

        Assert.Equal((QuestionOrigin.Team, null, null, null), (team.Origin, team.AiJobId, team.PromptVersion, team.GeneratedAt));
        Assert.Equal((QuestionOrigin.Ai, jobId, "gerar-questoes.v1", Now), (ai.Origin, ai.AiJobId, ai.PromptVersion, ai.GeneratedAt));
        Assert.Equal(QuestionStatus.Draft, ai.Status); // quem aprova é uma pessoa
        Assert.Throws<DomainException>(() => Question.FromAi(QuizType.Ambiental, AgeBand.Age8To10, "E", "X", Options, Guid.Empty, "v1", Now));
        Assert.Throws<DomainException>(() => Question.FromAi(QuizType.Ambiental, AgeBand.Age8To10, "E", "X", Options, jobId, " ", Now));
        Assert.Throws<DomainException>(() => Question.FromAi(QuizType.Ambiental, AgeBand.Age8To10, "E", "X", Options[..3], jobId, "v1", Now));
    }

    [Fact]
    public void Editar_nunca_muda_a_origem_e_so_a_questao_de_IA_registra_a_edicao()
    {
        var team = new Question(QuizType.Ambiental, AgeBand.Age8To10, "Enunciado", "Explicação", Options);
        var ai = Question.FromAi(QuizType.Ambiental, AgeBand.Age8To10, "Enunciado", "Explicação", Options, Guid.NewGuid(), "v1", Now);

        team.Update(QuizType.Cultural, AgeBand.Age6To7, "Outro", "Outra", Options, Now.AddHours(1));
        ai.Update(QuizType.Cultural, AgeBand.Age6To7, "Outro", "Outra", Options);
        Assert.Null(ai.AiEditedAt); // sem a hora da edição, nada é registrado
        ai.Update(QuizType.Cultural, AgeBand.Age6To7, "Outro", "Outra", Options, Now.AddHours(1));

        Assert.Equal((QuestionOrigin.Team, null), (team.Origin, team.AiEditedAt));
        Assert.Equal((QuestionOrigin.Ai, Now.AddHours(1)), (ai.Origin, ai.AiEditedAt));
        Assert.NotNull(ai.AiJobId);
    }
}
