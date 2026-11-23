using System.Text.Json;
using Helinho.Application.Ai;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Infrastructure.Fakes;
using Microsoft.Extensions.Options;

namespace Helinho.UnitTests;

/// <summary>Issue #236: validação e deduplicação da saída do modelo, que é dado não confiável (ADR-0004 §8). O analisador é coberto em <c>QuestionDraftOutputParserTests</c>.</summary>
public class QuestionDraftValidationTests
{
    private const string Statement = "Qual é a cor do céu em um dia sem nuvens?";

    private const string Backslash = "\\";

    private static string J(string text) => JsonSerializer.Serialize(text);

    private static string Item(
        string statement = Statement, string a = "Azul", string b = "Verde", string c = "Roxo", string d = "Cinza",
        string correct = "a", string explanation = "A luz do sol se espalha no ar e o céu parece azul.", string extra = "") =>
        $$"""{"enunciado":{{J(statement)}},"a":{{J(a)}},"b":{{J(b)}},"c":{{J(c)}},"d":{{J(d)}},"correta":{{J(correct)}},"explicacao":{{J(explanation)}}{{extra}}}""";

    private static string Batch(params string[] items) => $$"""{"questoes":[{{string.Join(',', items)}}]}""";

    private static QuestionDraftValidation Run(string json, int limit = 20, params string[] existing) =>
        QuestionDraftValidator.Validate(QuestionDraftOutputParser.Parse(json), existing, limit);

    [Fact]
    public void Lote_valido_e_aceito_aparado_com_o_indice_da_letra_correta()
    {
        var result = Run(Batch(Item(statement: $"  {Statement} ", b: " Verde ", correct: " B ")));

        var draft = Assert.Single(result.Accepted);
        Assert.Equal((Statement, 1, 0, 0), (draft.Statement, draft.CorrectIndex, result.InvalidCount, result.DuplicateCount));
        Assert.Equal(["Azul", "Verde", "Roxo", "Cinza"], draft.Options);
    }

    [Fact]
    public void Texto_malicioso_ou_fora_das_regras_e_rejeitado_com_o_motivo_e_nunca_limpo()
    {
        var invisible = "texto " + (char)0x202E + "invertido";
        (string Item, DraftRejection Reason)[] cases =
        [
            (Item(statement: "<script>alert(1)</script>"), DraftRejection.UnsafeText),
            (Item(b: "<b>Verde</b>"), DraftRejection.UnsafeText),
            (Item(explanation: "Fim <!-- comentário -->"), DraftRejection.UnsafeText),
            (Item(explanation: "Veja em https://example.org"), DraftRejection.UnsafeText),
            (Item(d: "WWW.example.org"), DraftRejection.UnsafeText),
            (Item(statement: invisible), DraftRejection.UnsafeText),
            (Item(explanation: "linha um\nlinha dois"), DraftRejection.UnsafeText),
            (Item(statement: new string('x', SaveQuestion.StatementMax + 1)), DraftRejection.TooLong),
            (Item(c: new string('x', SaveQuestion.OptionMax + 1)), DraftRejection.TooLong),
            (Item(explanation: new string('x', SaveQuestion.ExplanationMax + 1)), DraftRejection.TooLong),
            (Item(correct: "e"), DraftRejection.InvalidCorrectIndex),
            (Item(correct: "a,b"), DraftRejection.InvalidCorrectIndex),
            (Item(a: "Água", b: "agua!"), DraftRejection.DuplicateOptions),
            (Item(a: "   "), DraftRejection.Incomplete),
            (Item(explanation: ""), DraftRejection.Incomplete),
        ];

        foreach (var (item, reason) in cases)
        {
            var result = Run(Batch(item, Item(statement: "Quantos lados tem um triângulo?", a: "Três", b: "Dois", c: "Cinco", d: "Seis")));

            Assert.Equal("Quantos lados tem um triângulo?", Assert.Single(result.Accepted).Statement);
            Assert.Equal(KeyValuePair.Create(reason, 1), Assert.Single(result.Rejections));
        }
    }

    [Fact]
    public void Menor_que_e_emoji_em_texto_comum_nao_sao_marcacao()
    {
        var result = Run(Batch(Item(statement: "Qual número é < 5 e > 3? 🙂", a: "4", b: "6", c: "2", d: "9")));

        Assert.Single(result.Accepted);
    }

    [Fact]
    public void Item_fora_do_formato_e_descartado_sozinho_e_contado()
    {
        var threeOptions = """{"enunciado":"Três alternativas?","a":"x","b":"y","c":"z","correta":"a","explicacao":"e"}""";
        var duplicatedKey = Item().Replace("\"a\":", "\"a\":\"outra\",\"a\":", StringComparison.Ordinal);
        string[] malformed =
        [
            threeOptions, Item(extra: ",\"dica\":\"campo a mais\""), Item().Replace("\"correta\":\"a\"", "\"correta\":[\"a\",\"b\"]", StringComparison.Ordinal),
            Item().Replace("\"a\":\"Azul\"", "\"a\":5", StringComparison.Ordinal), duplicatedKey, "\"só texto\"", "null", "[]",
            Item().Replace("Azul", "par quebrado " + Backslash + "uD83D", StringComparison.Ordinal),
        ];

        var result = Run(Batch([.. malformed, Item()]));

        Assert.Single(result.Accepted);
        Assert.Equal((malformed.Length, malformed.Length, 0), (result.Rejections[DraftRejection.Malformed], result.InvalidCount, result.DuplicateCount));
    }

    [Fact]
    public void Trinta_itens_perdem_os_excedentes_pela_quantidade_pedida_e_nunca_passam_de_vinte()
    {
        var thirty = Batch([.. Enumerable.Range(1, 30).Select(i => Item(statement: $"Pergunta sintética número {i}?"))]);

        Assert.Equal((20, 10), (Run(thirty, limit: 99).Accepted.Count, Run(thirty, limit: 99).Rejections[DraftRejection.OverLimit]));
        Assert.Equal((5, 25), (Run(thirty, limit: 5).Accepted.Count, Run(thirty, limit: 5).InvalidCount));
    }

    [Fact]
    public void Duplicata_por_enunciado_normalizado_contra_o_que_existe_e_dentro_do_lote()
    {
        var batch = Batch(
            Item(statement: "QUAL E A COR DO CEU, EM UM DIA SEM NUVENS"),
            Item(statement: "Pergunta nova?"),
            Item(statement: "pergunta   NOVA!"),
            Item(statement: "Outra pergunta?", a: "igual", b: "IGUAL"),
            Item(statement: "Outra pergunta?"));

        var result = Run(batch, 20, Statement);

        Assert.Equal(["Pergunta nova?", "Outra pergunta?"], result.Accepted.Select(d => d.Statement));
        Assert.Equal((2, 1), (result.DuplicateCount, result.InvalidCount)); // o inválido não ocupa o enunciado no lote
        Assert.Equal("acao coracao 2 0", QuestionDraftValidator.Normalize("  Ação, CORAÇÃO! 2.0 " + (char)0xD83D));
    }

    [Fact]
    public void Rascunho_que_nao_veio_do_analisador_passa_pelas_mesmas_regras()
    {
        var valid = new QuestionDraft(Statement, ["Azul", "Verde", "Roxo", "Cinza"], 0, "Explicação sintética.");
        QuestionDraft[] drafts = [valid with { Options = ["Azul", "Verde", "Roxo"] }, valid with { CorrectIndex = 4 }, valid with { CorrectIndex = -1 },
            valid with { Statement = "par quebrado " + (char)0xD83D }, valid];

        var result = QuestionDraftValidator.Validate(drafts, [], 20);

        Assert.Equal(valid.Statement, Assert.Single(result.Accepted).Statement);
        Assert.Equal((1, 2, 1), (result.Rejections[DraftRejection.WrongOptionCount],
            result.Rejections[DraftRejection.InvalidCorrectIndex], result.Rejections[DraftRejection.UnsafeText]));
    }

    [Fact]
    public async Task O_lote_PartialInvalid_do_gerador_fake_perde_os_quatro_invalidos_e_fica_com_o_valido()
    {
        var fake = new FakeQuestionDraftGenerator(Options.Create(new FakeAiOptions { Failure = FakeAiFailure.PartialInvalid }));
        var request = QuestionDraftRequest.Create(QuizType.Esportivo, AgeBand.Age8To10, 5, null, []);

        var batch = await fake.GenerateAsync(request, TestContext.Current.CancellationToken);
        var result = QuestionDraftValidator.Validate(batch.Drafts, request.ExistingStatements, request.Count);

        Assert.Equal(batch.Drafts[4].Statement, Assert.Single(result.Accepted).Statement);
        DraftRejection[] reasons = [DraftRejection.WrongOptionCount, DraftRejection.InvalidCorrectIndex, DraftRejection.TooLong, DraftRejection.UnsafeText];
        Assert.Equal(reasons.Order(), result.Rejections.Keys.Order());
        Assert.Equal((4, 0), (result.InvalidCount, result.DuplicateCount));
    }
}
