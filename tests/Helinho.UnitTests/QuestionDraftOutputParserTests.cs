using System.Text;
using Helinho.Application.Ai;

namespace Helinho.UnitTests;

/// <summary>Issue #246: análise estrita do JSON devolvido por um modelo (ADR-0004 §6.3 e §8). O texto é dado não confiável.</summary>
public class QuestionDraftOutputParserTests
{
    private const string Backslash = "\\";
    private const string Valid =
        """{"enunciado":" Qual é a cor do céu? ","a":"Azul","b":"Verde","c":"Roxo","d":"Cinza","correta":"a","explicacao":"O céu parece azul."}""";

    private static string Batch(params string[] items) => $$"""{"questoes":[{{string.Join(',', items)}}]}""";

    [Fact]
    public void Item_no_formato_vira_rascunho_com_as_alternativas_na_ordem_e_o_indice_da_letra()
    {
        var drafts = QuestionDraftOutputParser.Parse(Batch(
            Valid,
            Valid.Replace("\"correta\":\"a\"", "\"correta\":\" C \"", StringComparison.Ordinal),
            Valid.Replace("\"correta\":\"a\"", "\"correta\":\"e\"", StringComparison.Ordinal),
            Valid.Replace("\"correta\":\"a\"", "\"correta\":\"a,b\"", StringComparison.Ordinal)));

        Assert.Equal([0, 2, -1, -1], drafts.Select(d => d.CorrectIndex)); // fora de a–d fica -1, para o validador recusar
        Assert.Equal((" Qual é a cor do céu? ", "O céu parece azul."), (drafts[0].Statement, drafts[0].Explanation)); // sem aparar nem limpar
        Assert.Equal(["Azul", "Verde", "Roxo", "Cinza"], drafts[0].Options);
        Assert.Equal(" Azul ", QuestionDraftOutputParser.Parse(Batch(Valid.Replace("\"Azul\"", "\" Azul \"", StringComparison.Ordinal)))[0].Options[0]);
        Assert.Empty(QuestionDraftOutputParser.Parse("""{"questoes":[]}"""));
    }

    [Fact]
    public void Item_fora_do_formato_vira_a_marca_e_nao_derruba_os_vizinhos()
    {
        string[] malformed =
        [
            """{"enunciado":"Três alternativas?","a":"x","b":"y","c":"z","correta":"a","explicacao":"e"}""",
            Valid.Replace("\"d\":", "\"dica\":\"campo a mais\",\"d\":", StringComparison.Ordinal),
            Valid.Replace("\"correta\":\"a\"", "\"correta\":[\"a\",\"b\"]", StringComparison.Ordinal),
            Valid.Replace("\"a\":\"Azul\"", "\"a\":5", StringComparison.Ordinal),
            Valid.Replace("\"a\":", "\"a\":\"repetida\",\"a\":", StringComparison.Ordinal),
            Valid.Replace("Azul", "par quebrado " + Backslash + "uD83D", StringComparison.Ordinal),
            Valid.Replace("\"enunciado\"", "\"" + Backslash + "uD83D\"", StringComparison.Ordinal), // escape quebrado na chave
            Valid.Replace("\"enunciado\"", "\"Enunciado\"", StringComparison.Ordinal), // a chave diferencia maiúsculas
            Valid.Replace("\"a\":\"Azul\"", "\"a\":{\"b\":\"x\"}", StringComparison.Ordinal),
            Valid.Replace("\"a\":\"Azul\"", "\"a\":[]", StringComparison.Ordinal),
            "\"só texto\"", "null", "[]",
        ];

        var drafts = QuestionDraftOutputParser.Parse(Batch([.. malformed, Valid]));

        Assert.Equal(malformed.Length + 1, drafts.Count);
        Assert.All(drafts.Take(malformed.Length), draft => Assert.Same(QuestionDraftOutputParser.MalformedItem, draft));
        Assert.Equal(0, drafts[^1].CorrectIndex);
        Assert.Equal(("", 0, ""), (QuestionDraftOutputParser.MalformedItem.Statement, QuestionDraftOutputParser.MalformedItem.Options.Count,
            QuestionDraftOutputParser.MalformedItem.Explanation)); // a marca não carrega texto recebido
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto solto com <script>segredo-do-lote</script>")]
    [InlineData("""{"questoes":[""")]
    [InlineData("""{"questoes":[{"enunciado":"segredo-do-lote" "a":"vírgula faltando"}]}""")]
    [InlineData("""[{"enunciado":"raiz é lista"}]""")]
    [InlineData("""{"questoes":"não é lista"}""")]
    [InlineData("""{"questoes":[],"outra":1}""")]
    [InlineData("""{"itens":[]}""")]
    [InlineData("""{"questoes":[{"a":{"b":{"c":"fundo demais"}}}]}""")]
    public void Saida_que_nao_e_o_JSON_esperado_invalida_o_lote_sem_repetir_o_texto(string? output)
    {
        var failure = Assert.Throws<QuestionDraftGenerationException>(() => QuestionDraftOutputParser.Parse(output));

        Assert.Equal(QuestionDraftFailure.InvalidOutput, failure.Reason);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("segredo-do-lote", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Saida_acima_de_64_KB_e_recusada_antes_de_analisar_e_no_limite_passa()
    {
        // O limite é em bytes UTF-8, não em caracteres: o texto tem acentos (dois bytes cada) e o enchimento é ASCII.
        var padding = Encoding.UTF8.GetByteCount(Batch(Valid));
        string Sized(int bytes) => Batch(Valid.Replace("O céu parece azul.", "O céu parece azul." + new string('x', bytes - padding), StringComparison.Ordinal));
        var atLimit = Sized(QuestionDraftOutputParser.MaxBytes);

        Assert.Equal(QuestionDraftOutputParser.MaxBytes, Encoding.UTF8.GetByteCount(atLimit));
        Assert.True(atLimit.Length < QuestionDraftOutputParser.MaxBytes);
        Assert.Single(QuestionDraftOutputParser.Parse(atLimit));
        var failure = Assert.Throws<QuestionDraftGenerationException>(() => QuestionDraftOutputParser.Parse(Sized(QuestionDraftOutputParser.MaxBytes + 1)));
        Assert.Equal(QuestionDraftFailure.InvalidOutput, failure.Reason);
    }
}
