using System.Globalization;
using System.Text;
using Helinho.Application.Ai;
using Helinho.Domain;

namespace Helinho.Infrastructure.Ai;

/// <summary>
/// O que vai ao provedor, e nada mais: texto de sistema, texto do pedido e a versão do prompt (ADR-0004 §7).
/// Neutro de provedor. <c>ToString</c> só devolve o nome do tipo: o assunto não vaza por log.
/// </summary>
public sealed record QuestionPrompt(string SystemText, string UserText, string Version)
{
    public override string ToString() => nameof(QuestionPrompt);
}

/// <summary>
/// Montador da requisição, usado pelo gerador fake e pelo adaptador real (ADR-0004 §7, §11 e §12). Só recebe a
/// <see cref="QuestionDraftRequest"/>: não tem banco, usuário nem configuração de onde tirar outro dado.
/// </summary>
public static class QuestionPromptBuilder
{
    /// <summary>Prompt publicado não é editado: mudança de texto cria a v2, e a versão é gravada em cada geração.</summary>
    public const string Version = "gerar-questoes.v1";
    public const int ExistingMax = 80;
    public const int ExistingStatementMax = 160;

    private static readonly string V1SystemText = LoadSystemText();

    /// <exception cref="DomainException">Requisição inválida (quantidade, tipo, faixa ou assunto).</exception>
    public static QuestionPrompt Build(QuestionDraftRequest request)
    {
        // De novo pela fábrica: requisição montada pelo construtor público não fura a validação nem o saneamento.
        var safe = QuestionDraftRequest.Create(request.QuizType, request.AgeBand, request.Count, request.Subject, request.ExistingStatements);
        var existing = safe.ExistingStatements.Select(Neutralize).Where(s => s.Length > 0).Take(ExistingMax).ToList();

        var user = new StringBuilder()
            .Append("<pedido>\n")
            .Append("tipo de quiz: ").Append(Theme(safe.QuizType)).Append('\n')
            .Append("faixa etária: ").Append(Band(safe.AgeBand)).Append('\n')
            .Append("quantidade de questões: ").Append(safe.Count.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("</pedido>\n")
            .Append(safe.Subject is null ? "Sem assunto definido.\n" : $"<assunto>{safe.Subject}</assunto>\n")
            .Append("<enunciados_existentes>\n")
            .Append(existing.Count == 0 ? "(nenhum)\n" : string.Concat(existing.Select(s => $"- {s}\n")))
            .Append("</enunciados_existentes>");
        return new QuestionPrompt(V1SystemText, user.ToString(), Version);
    }

    /// <summary>Cópia que vai ao prompt: sem <c>&lt;</c> e <c>&gt;</c>, sem quebra, controle ou invisível, cortada no limite.</summary>
    private static string Neutralize(string statement)
    {
        var visible = statement.Where(c => c is not ('<' or '>')).Select(c => QuestionDraftRequest.IsSeparatorOrHidden(c) ? ' ' : c);
        var clean = string.Join(' ', string.Concat(visible).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > ExistingStatementMax ? clean[..ExistingStatementMax].TrimEnd() : clean;
    }

    private static string Theme(QuizType type) => type switch
    {
        QuizType.Ambiental => "ambiental (Flora)",
        QuizType.Conhecimento => "conhecimento (Byte)",
        QuizType.Cultural => "cultural (Luna)",
        QuizType.Esportivo => "esportivo (Max)",
        QuizType.Diversidade => "diversidade (Lia)",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string Band(AgeBand band) => band switch
    {
        AgeBand.Age6To7 => "6 a 7 anos",
        AgeBand.Age8To10 => "8 a 10 anos",
        AgeBand.Age11To12 => "11 a 12 anos",
        AgeBand.Age13To14 => "13 a 14 anos",
        _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

    private static string LoadSystemText()
    {
        using var stream = typeof(QuestionPromptBuilder).Assembly.GetManifestResourceStream(Version + ".md")
            ?? throw new InvalidOperationException($"Recurso {Version}.md ausente do assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
    }
}
