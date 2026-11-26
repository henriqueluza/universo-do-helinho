using Helinho.Domain;
using Helinho.Domain.Entities;

namespace Helinho.UnitTests;

/// <summary>Issue #263: regras de entrada e transições do pedido de IA e da sugestão (ADR-0004 §9.2 e §9.3).</summary>
public class AiGenerationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly AiJobUsage Usage = new("Fake", "fake-v1", "gerar-questoes.v1", 120, 340, TimeSpan.FromMilliseconds(1500));

    private static AiGenerationJob NewJob(AiJobKind kind = AiJobKind.Batch, int count = 5, string? subject = "Reciclagem") =>
        new(kind, QuizType.Ambiental, AgeBand.Age8To10, count, subject, User, Now);

    private static AiQuestionSuggestion NewSuggestion() =>
        new(Guid.NewGuid(), 0, " Enunciado sintético? ", ["Um", "Dois", "Três", "Quatro"], 2, "Explicação sintética.");

    [Fact]
    public void Pedido_nasce_pendente_so_com_o_id_de_quem_pediu_e_valida_quantidade_e_assunto()
    {
        var job = NewJob();

        Assert.Equal((AiJobStatus.Pending, User, Now, "Reciclagem", 5), (job.Status, job.RequestedByUserId, job.CreatedAt, job.Subject, job.RequestedCount));
        Assert.Null(NewJob(subject: "  ").Subject);
        Assert.Throws<DomainException>(() => NewJob(count: 0));
        Assert.Throws<DomainException>(() => NewJob(count: AiGenerationJob.MaxCount + 1));
        Assert.Throws<DomainException>(() => NewJob(AiJobKind.Single, count: 2)); // o avulso pede uma
        Assert.Throws<DomainException>(() => NewJob(subject: new string('a', AiGenerationJob.SubjectMax + 1)));
        Assert.Throws<DomainException>(() => new AiGenerationJob(AiJobKind.Batch, (QuizType)99, AgeBand.Age8To10, 1, null, User, Now));
        Assert.Throws<DomainException>(() => new AiGenerationJob(AiJobKind.Batch, QuizType.Cultural, AgeBand.Age8To10, 1, null, Guid.Empty, Now));
    }

    [Fact]
    public void Pedido_passa_por_gerando_e_conclui_com_uso_e_contadores()
    {
        var job = NewJob();

        job.Start(Now.AddSeconds(1));
        job.Start(Now.AddSeconds(30)); // repetir não tem efeito
        job.Succeed(Usage with { Model = new string('m', 300) }, new AiJobCounts(5, 3, 1, 1), Now.AddSeconds(40));

        Assert.Equal((AiJobStatus.Succeeded, Now.AddSeconds(1), Now.AddSeconds(40)), (job.Status, job.StartedAt, job.FinishedAt));
        Assert.Equal(("Fake", "gerar-questoes.v1", 120, 340, 1500), (job.Provider, job.PromptVersion, job.InputTokens, job.OutputTokens, job.DurationMs));
        Assert.Equal(AiGenerationJob.ModelMax, job.Model?.Length); // valor do provedor cortado no limite da coluna
        Assert.Equal((5, 3, 1, 1), (job.GeneratedCount, job.ValidCount, job.InvalidCount, job.DuplicateCount));
        Assert.Null(job.FailureReason);
        job.EraseSubject();
        Assert.Null(job.Subject);
    }

    [Fact]
    public void Estado_final_nao_muda_e_o_motivo_da_falha_e_codigo_fechado()
    {
        var failed = NewJob();
        Assert.Throws<ArgumentException>(() => failed.Fail("A IA respondeu: texto livre", Now));
        Assert.Throws<ArgumentException>(() => failed.Fail(new string('a', AiGenerationJob.FailureReasonMax + 1), Now));
        Assert.Equal(AiJobStatus.Pending, failed.Status);

        failed.Fail("ai_timeout", Now.AddSeconds(90));
        failed.Start(Now.AddMinutes(2));

        Assert.Equal((AiJobStatus.Failed, "ai_timeout", Now.AddSeconds(90), null), (failed.Status, failed.FailureReason, failed.FinishedAt, failed.StartedAt));
        Assert.True(failed.IsFinished);
        Assert.Throws<InvalidOperationException>(() => failed.Succeed(Usage, new AiJobCounts(1, 1, 0, 0), Now));
        Assert.Throws<InvalidOperationException>(() => failed.Fail("expired", Now));
    }

    [Fact]
    public void Sugestao_so_e_decidida_a_partir_de_pendente_com_quem_e_quando()
    {
        var approved = NewSuggestion();
        var questionId = Guid.NewGuid();
        Assert.Equal((AiSuggestionStatus.Pending, "Enunciado sintético?", 2), (approved.Status, approved.Statement, approved.CorrectIndex));

        approved.Approve(User, questionId, Now);
        var edited = NewSuggestion();
        edited.MarkEdited(User, questionId, Now);
        var discarded = NewSuggestion();
        discarded.Discard(User, Now);

        Assert.Equal((AiSuggestionStatus.Approved, User, Now, questionId), (approved.Status, approved.DecidedByUserId, approved.DecidedAt, approved.QuestionId));
        Assert.Equal((AiSuggestionStatus.Edited, questionId), (edited.Status, edited.QuestionId));
        Assert.Equal((AiSuggestionStatus.Discarded, User, null), (discarded.Status, discarded.DecidedByUserId, discarded.QuestionId));
        Assert.Throws<DomainException>(() => approved.Discard(User, Now.AddMinutes(1)));
        Assert.Throws<DomainException>(() => discarded.Approve(User, questionId, Now));
        Assert.Throws<DomainException>(() => NewSuggestion().Discard(Guid.Empty, Now));
        Assert.Equal(Now, approved.DecidedAt);
    }

    [Fact]
    public void Sugestao_exige_quatro_alternativas_preenchidas_e_indice_de_0_a_3()
    {
        var job = Guid.NewGuid();
        Assert.Equal(["Um", "Dois", "Três", "Quatro"], NewSuggestion().Options);
        Assert.Throws<DomainException>(() => new AiQuestionSuggestion(job, 0, "E", ["Um", "Dois", "Três"], 0, "X"));
        Assert.Throws<DomainException>(() => new AiQuestionSuggestion(job, 0, "E", ["Um", "Dois", "Três", "Quatro"], 4, "X"));
        Assert.Throws<DomainException>(() => new AiQuestionSuggestion(job, 0, "E", ["Um", " ", "Três", "Quatro"], 0, "X"));
        Assert.Throws<DomainException>(() => new AiQuestionSuggestion(job, 0, " ", ["Um", "Dois", "Três", "Quatro"], 0, "X"));
        Assert.Throws<DomainException>(() => new AiQuestionSuggestion(Guid.Empty, 0, "E", ["Um", "Dois", "Três", "Quatro"], 0, "X"));
    }
}
