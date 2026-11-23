using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Infrastructure.Fakes;
using Microsoft.Extensions.Options;

namespace Helinho.UnitTests;

/// <summary>Issue #232: o gerador fake é determinístico, coerente com tipo e faixa e falha só por configuração (ADR-0004 §5).</summary>
public class FakeQuestionDraftGeneratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeQuestionDraftGenerator Fake(FakeAiFailure failure = FakeAiFailure.None, int delay = 0) =>
        new(Options.Create(new FakeAiOptions { Failure = failure, DelayMilliseconds = delay }));

    private static QuestionDraftRequest Request(
        int count = 5, string? subject = null, QuizType type = QuizType.Ambiental, AgeBand band = AgeBand.Age8To10, IReadOnlyList<string>? existing = null) =>
        QuestionDraftRequest.Create(type, band, count, subject, existing ?? []);

    private static string Flat(QuestionDraftBatch batch) =>
        string.Join('\n', batch.Drafts.Select(d => $"{d.Statement}|{string.Join(';', d.Options)}|{d.CorrectIndex}|{d.Explanation}"));

    [Fact]
    public async Task A_mesma_requisicao_gera_o_mesmo_lote_com_uso_e_versao_do_prompt()
    {
        var first = await Fake().GenerateAsync(Request(7, "Ciclo da água"), Ct);
        var second = await Fake().GenerateAsync(Request(7, "  ciclo   da ÁGUA "), Ct);

        Assert.Equal(Flat(first), Flat(second));
        Assert.Equal(first.Usage, second.Usage);
        Assert.Equal(("Fake", "fake-v1", "gerar-questoes.v1"), (first.Usage.Provider, first.Usage.Model, first.PromptVersion));
        Assert.True(first.Usage.InputTokens > 0 && first.Usage.OutputTokens > 0);
    }

    [Fact]
    public async Task Todo_tipo_e_faixa_recebe_a_quantidade_pedida_de_rascunhos_validos_e_distintos()
    {
        var statements = new HashSet<string>();
        foreach (var type in Enum.GetValues<QuizType>())
        {
            foreach (var band in Enum.GetValues<AgeBand>())
            {
                var batch = await Fake().GenerateAsync(Request(QuestionDraftRequest.MaxCount, null, type, band), Ct);

                Assert.Equal(QuestionDraftRequest.MaxCount, batch.Drafts.Count);
                Assert.All(batch.Drafts, d =>
                {
                    Assert.Equal(4, d.Options.Distinct().Count());
                    Assert.InRange(d.CorrectIndex, 0, 3);
                    Assert.False(string.IsNullOrWhiteSpace(d.Explanation));
                    Assert.True(statements.Add(d.Statement)); // distinto no lote e entre tipos e faixas
                });
            }
        }
    }

    [Fact]
    public async Task O_segundo_pedido_igual_pula_os_enunciados_que_ja_existem()
    {
        var first = await Fake().GenerateAsync(Request(6), Ct);
        var existing = first.Drafts.Select(d => d.Statement).ToList();

        var second = await Fake().GenerateAsync(Request(6, existing: existing), Ct);

        Assert.Equal(6, second.Drafts.Count);
        Assert.Empty(second.Drafts.Select(d => d.Statement).Intersect(existing));
    }

    [Theory]
    [InlineData(FakeAiFailure.Unavailable, QuestionDraftFailure.ProviderUnavailable)]
    [InlineData(FakeAiFailure.Refused, QuestionDraftFailure.Refused)]
    [InlineData(FakeAiFailure.InvalidOutput, QuestionDraftFailure.InvalidOutput)]
    public async Task Falha_simulada_por_configuracao_vira_excecao_tipada(FakeAiFailure configured, QuestionDraftFailure expected)
    {
        var failure = await Assert.ThrowsAsync<QuestionDraftGenerationException>(() => Fake(configured).GenerateAsync(Request(), Ct));

        Assert.Equal(expected, failure.Reason);
    }

    [Fact]
    public async Task Timeout_simulado_e_a_latencia_respeitam_o_token_de_quem_chamou()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fake(FakeAiFailure.Timeout).GenerateAsync(Request(), deadline.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fake(delay: 30_000).GenerateAsync(Request(), deadline.Token));
        Assert.Equal(TimeSpan.FromMilliseconds(20), (await Fake(delay: 20).GenerateAsync(Request(), Ct)).Usage.Duration);
    }

    [Fact]
    public async Task PartialInvalid_devolve_rascunhos_que_violam_as_regras_ao_lado_de_validos()
    {
        var drafts = (await Fake(FakeAiFailure.PartialInvalid).GenerateAsync(Request(5), Ct)).Drafts;

        Assert.Equal(3, drafts[0].Options.Count);
        Assert.Equal(4, drafts[1].CorrectIndex);
        Assert.Equal(2000, drafts[2].Statement.Length);
        Assert.Contains("<b>", drafts[3].Explanation, StringComparison.Ordinal);
        Assert.Equal((4, true), (drafts[4].Options.Count, drafts[4].CorrectIndex is >= 0 and <= 3));
    }

    [Fact]
    public async Task O_assunto_nao_dispara_falha_e_requisicao_invalida_e_recusada_antes_de_gerar()
    {
        var batch = await Fake().GenerateAsync(Request(3, "Timeout InvalidOutput Refused"), Ct);
        var invalid = new QuestionDraftRequest(QuizType.Ambiental, AgeBand.Age6To7, 500, null, []);

        Assert.Equal(3, batch.Drafts.Count);
        await Assert.ThrowsAsync<DomainException>(() => Fake().GenerateAsync(invalid, Ct));
    }
}
