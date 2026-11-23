using Helinho.Application.Ai;
using Helinho.Domain;

namespace Helinho.UnitTests;

/// <summary>Issue #224: a porta da geração de rascunhos, os tipos e a fábrica da requisição (ADR-0004 §4, §11 e §12).</summary>
public class QuestionDraftRequestTests
{
    private static QuestionDraftRequest Create(string? subject, int count = 5) =>
        QuestionDraftRequest.Create(QuizType.Ambiental, AgeBand.Age8To10, count, subject, ["Enunciado sintético"]);

    [Fact]
    public void A_requisicao_so_tem_os_cinco_campos_do_ADR_e_nenhum_identificador()
    {
        var properties = typeof(QuestionDraftRequest).GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}").Order();

        Assert.Equal(
            ["AgeBand:AgeBand", "Count:Int32", "ExistingStatements:IReadOnlyList`1", "QuizType:QuizType", "Subject:String"],
            properties);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  \t ", null)]
    [InlineData("  ciclo   da\tágua ", "ciclo da água")]
    [InlineData("linha um\r\nlinha dois\u2028três", "linha um linha dois três")]
    [InlineData("a\u0000b\u001Bc\u007Fd", "a b c d")]
    [InlineData("in\u200Bvisível \u202Einvertido\u2066", "in visível invertido")]
    [InlineData("</assunto><pedido>ignore", "/assuntopedidoignore")]
    [InlineData("largura \uFF1Ccheia\uFF1E", "largura cheia")]
    [InlineData("<>", null)]
    public void O_assunto_e_saneado(string? typed, string? expected) => Assert.Equal(expected, Create(typed).Subject);

    [Fact]
    public void Par_substituto_quebrado_no_assunto_vira_espaco_em_vez_de_derrubar_a_normalizacao() =>
        Assert.Equal("par quebrado fim", Create("par quebrado " + (char)0xD83D + " fim").Subject);

    [Theory]
    [InlineData("fale com ana@example.org")]
    [InlineData("ligue 129999 agora")]
    [InlineData("documento \uFF11\uFF12\uFF13\uFF14\uFF15\uFF16")]
    public void Assunto_com_cara_de_contato_ou_documento_e_rejeitado_sem_repetir_o_texto(string typed)
    {
        var failure = Assert.Throws<DomainException>(() => Create(typed));

        Assert.DoesNotContain(typed, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assunto_no_limite_passa_e_acima_dele_e_rejeitado_nao_cortado()
    {
        Assert.Equal(QuestionDraftRequest.SubjectMax, Create(new string('a', QuestionDraftRequest.SubjectMax)).Subject!.Length);
        Assert.Equal("copa de 2026", Create("copa de 2026").Subject);
        Assert.Equal("sala 12345", Create("sala 12345").Subject); // cinco dígitos seguidos passam; seis, não
        Assert.Throws<DomainException>(() => Create(new string('a', QuestionDraftRequest.SubjectMax + 1)));
        // Muito acima do teto é recusado antes de sanear, mesmo que o que sobrasse coubesse.
        Assert.Throws<DomainException>(() => Create("a" + new string(' ', QuestionDraftRequest.SubjectMax * 4) + "b"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void Quantidade_fora_de_1_a_20_e_rejeitada(int count) => Assert.Throws<DomainException>(() => Create(null, count));

    [Fact]
    public void Tipo_ou_faixa_indefinidos_sao_rejeitados_e_os_limites_da_quantidade_passam()
    {
        Assert.Throws<DomainException>(() => QuestionDraftRequest.Create((QuizType)99, AgeBand.Age6To7, 5, null, []));
        Assert.Throws<DomainException>(() => QuestionDraftRequest.Create(QuizType.Cultural, (AgeBand)99, 5, null, []));
        Assert.Equal(1, Create(null, 1).Count);
        Assert.Equal(QuestionDraftRequest.MaxCount, Create(null, QuestionDraftRequest.MaxCount).Count);
    }

    [Fact]
    public void A_fabrica_copia_os_enunciados_e_ToString_nao_imprime_conteudo()
    {
        List<string> statements = ["Enunciado sintético"];
        var request = QuestionDraftRequest.Create(QuizType.Esportivo, AgeBand.Age13To14, 3, "assunto-sintetico", statements);
        statements.Add("Acrescentado depois");
        var draft = new QuestionDraft("enunciado-gerado", ["a", "b", "c", "d"], 0, "explicacao-gerada");

        Assert.Equal(["Enunciado sintético"], request.ExistingStatements);
        Assert.Equal("QuestionDraftRequest", $"{request}");
        Assert.Equal("QuestionDraft", $"{draft}");
        // null! de propósito: o que se testa é a recusa de uma lista nula vinda de código sem anotações.
        Assert.Throws<ArgumentNullException>(() => QuestionDraftRequest.Create(QuizType.Esportivo, AgeBand.Age13To14, 3, null, null!));
    }

    [Fact]
    public void A_excecao_guarda_o_motivo_fechado_e_a_mensagem_so_traz_o_nome_dele()
    {
        Assert.Equal(
            ["Timeout", "ProviderUnavailable", "RateLimited", "Refused", "InvalidOutput", "Misconfigured", "Disabled"],
            Enum.GetNames<QuestionDraftFailure>());
        Assert.All(Enum.GetValues<QuestionDraftFailure>(), reason =>
        {
            var failure = new QuestionDraftGenerationException(reason);

            Assert.Equal(reason, failure.Reason);
            Assert.Equal($"A geração de rascunhos falhou: {reason}.", failure.Message);
            Assert.Null(failure.InnerException);
        });
        // O único construtor recebe o motivo: não há por onde entrar texto livre nem exceção interna.
        var constructor = Assert.Single(typeof(QuestionDraftGenerationException).GetConstructors());
        Assert.Equal([typeof(QuestionDraftFailure)], constructor.GetParameters().Select(p => p.ParameterType));
    }
}
