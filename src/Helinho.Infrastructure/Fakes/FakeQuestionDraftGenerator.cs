using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.Fakes;

public enum FakeAiFailure
{
    None,
    Timeout,
    Unavailable,
    Refused,
    InvalidOutput,
    PartialInvalid,
}

/// <summary><c>Ai:Fake:DelayMilliseconds</c> e <c>Ai:Fake:Failure</c>: chaves de desenvolvimento e teste, fora do Terraform (ADR-0004 §5).</summary>
public sealed class FakeAiOptions
{
    public const string Section = "Ai:Fake";

    public int DelayMilliseconds { get; set; }
    public FakeAiFailure Failure { get; set; }
}

/// <summary>
/// Gerador fictício e determinístico (ADR-0004 §5): a mesma requisição dá o mesmo lote, em qualquer processo. Usa o
/// mesmo montador do adaptador real, então valida e saneia igual. Nenhuma falha é disparada pelo texto do assunto.
/// </summary>
public sealed class FakeQuestionDraftGenerator(IOptions<FakeAiOptions> options) : IQuestionDraftGenerator
{
    public const string Provider = "Fake";
    public const string Model = "fake-v1";
    private const int MaxDelayMilliseconds = 60_000;

    private sealed record Entry(string Question, string Correct, string[] Wrong, string Explanation);

    public async Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken)
    {
        var prompt = QuestionPromptBuilder.Build(request);
        var settings = options.Value;
        var delay = TimeSpan.FromMilliseconds(Math.Clamp(settings.DelayMilliseconds, 0, MaxDelayMilliseconds));
        // Timeout simulado é esperar além de qualquer prazo: quem encerra é o token de quem chamou (ADR-0004 §4).
        await Task.Delay(settings.Failure == FakeAiFailure.Timeout ? Timeout.InfiniteTimeSpan : delay, cancellationToken);

        QuestionDraftFailure? failure = settings.Failure switch
        {
            FakeAiFailure.Unavailable => QuestionDraftFailure.ProviderUnavailable,
            FakeAiFailure.Refused => QuestionDraftFailure.Refused,
            FakeAiFailure.InvalidOutput => QuestionDraftFailure.InvalidOutput,
            _ => null,
        };
        if (failure is { } reason)
        {
            throw new QuestionDraftGenerationException(reason);
        }

        var drafts = Compose(request);
        if (settings.Failure == FakeAiFailure.PartialInvalid)
        {
            drafts = [.. drafts.Select(Corrupt)];
        }

        var usage = new AiUsage(Provider, Model,
            InputTokens: (prompt.SystemText.Length + prompt.UserText.Length) / 3,
            OutputTokens: drafts.Sum(d => d.Statement.Length + d.Explanation.Length + d.Options.Sum(o => o.Length)) / 3,
            Duration: delay);
        return new QuestionDraftBatch(drafts, prompt.Version, usage);
    }

    private static List<QuestionDraft> Compose(QuestionDraftRequest request)
    {
        // SHA-256, não GetHashCode: o hash de string do .NET muda a cada processo.
        var subject = QuestionDraftRequest.SanitizeSubject(request.Subject)?.ToUpperInvariant();
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"{QuestionPromptBuilder.Version}|{request.QuizType}|{request.AgeBand}|{request.Count}|{subject}")));
        var bank = Bank[request.QuizType];
        var order = Enumerable.Range(0, bank.Length).OrderBy(i => seed[i]).ThenBy(i => i).ToList();
        var existing = request.ExistingStatements.Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var drafts = new List<QuestionDraft>(request.Count);
        // O banco é pequeno: quando esgota, as rodadas seguintes numeram variações, e sempre há Count rascunhos distintos.
        for (var round = 1; drafts.Count < request.Count; round++)
        {
            foreach (var entry in order.Select(i => bank[i]))
            {
                var statement = Frame(request.AgeBand) + entry.Question + (round == 1 ? "" : string.Create(CultureInfo.InvariantCulture, $" (variação {round})"));
                if (drafts.Count == request.Count || existing.Contains(statement))
                {
                    continue;
                }

                var correctAt = seed[(order.Count + drafts.Count) % seed.Length] % 4;
                List<string> answers = [.. entry.Wrong];
                answers.Insert(correctAt, entry.Correct);
                drafts.Add(new QuestionDraft(statement, answers, correctAt, entry.Explanation));
            }
        }

        return drafts;
    }

    /// <summary>Lote do <see cref="FakeAiFailure.PartialInvalid"/>: quatro em cada cinco rascunhos violam uma regra do validador.</summary>
    private static QuestionDraft Corrupt(QuestionDraft draft, int index) => (index % 5) switch
    {
        0 => draft with { Options = [.. draft.Options.Take(3)] },
        1 => draft with { CorrectIndex = draft.Options.Count },
        2 => draft with { Statement = new string('x', 2000) },
        3 => draft with { Explanation = "<b>marcação</b> " + draft.Explanation },
        _ => draft,
    };

    private static string Frame(AgeBand band) => band switch
    {
        AgeBand.Age6To7 => "",
        AgeBand.Age8To10 => "Pense e responda: ",
        AgeBand.Age11To12 => "Com o que você já estudou, responda: ",
        _ => "Relacione o que você sabe e responda: ",
    };

    private static readonly Dictionary<QuizType, Entry[]> Bank = new()
    {
        [QuizType.Ambiental] =
        [
            new("Qual destes materiais vai para a coleta de papel?", "Caixa de papelão", ["Casca de banana", "Garrafa de vidro", "Lata de alumínio"], "A caixa de papelão é feita de papel e pode virar papel novo."),
            new("O que as plantas precisam para crescer?", "Água e luz do sol", ["Só de sombra", "Apenas de vento", "Areia bem seca"], "As plantas usam água e luz do sol para produzir o próprio alimento."),
            new("Qual atitude ajuda a economizar água?", "Fechar a torneira ao escovar os dentes", ["Deixar a mangueira aberta no quintal", "Tomar banhos bem demorados", "Lavar a calçada todos os dias"], "Fechar a torneira evita que água limpa vá embora sem uso."),
            new("Onde devemos jogar o lixo quando estamos na rua?", "Na lixeira", ["No chão da praça", "No rio mais perto", "Na calçada"], "Lixo na lixeira não entope bueiros nem suja a cidade."),
        ],
        [QuizType.Conhecimento] =
        [
            new("Quanto é 7 mais 5?", "12", ["10", "13", "11"], "Somando 7 e 5 chegamos a 12."),
            new("Qual é o maior planeta do Sistema Solar?", "Júpiter", ["Marte", "Terra", "Vênus"], "Júpiter é o maior planeta do Sistema Solar."),
            new("Quantos dias tem uma semana?", "Sete", ["Cinco", "Dez", "Seis"], "A semana vai de domingo a sábado: sete dias."),
            new("Qual destas palavras é um substantivo?", "Casa", ["Correr", "Bonito", "Depressa"], "Casa dá nome a um lugar; palavras que dão nome são substantivos."),
        ],
        [QuizType.Cultural] =
        [
            new("Qual destes é um instrumento musical de percussão?", "Tambor", ["Violino", "Flauta", "Violão"], "O tambor produz som quando é batido, como os outros instrumentos de percussão."),
            new("Em que festa popular brasileira é comum dançar quadrilha?", "Festa junina", ["Carnaval", "Natal", "Páscoa"], "A quadrilha é a dança típica das festas juninas."),
            new("Como se chama a pessoa que escreve livros?", "Escritor", ["Pintor", "Escultor", "Maestro"], "Quem escreve livros é chamado de escritor ou escritora."),
            new("O que encontramos em um museu?", "Obras e objetos guardados para visitação", ["Remédios para comprar", "Passagens de ônibus", "Frutas e verduras"], "Museus guardam e mostram obras e objetos importantes."),
        ],
        [QuizType.Esportivo] =
        [
            new("Quantos jogadores de cada time ficam em campo no futebol?", "Onze", ["Cinco", "Sete", "Nove"], "No futebol de campo cada time joga com onze jogadores."),
            new("Em qual esporte se usa raquete?", "Tênis", ["Natação", "Judô", "Corrida"], "No tênis a bola é rebatida com uma raquete."),
            new("O que é importante fazer antes de praticar esporte?", "Aquecer o corpo", ["Ficar sem beber água", "Dormir pouco", "Pular o café da manhã"], "O aquecimento prepara os músculos e ajuda a evitar machucados."),
            new("O que é jogo limpo?", "Respeitar as regras e os colegas", ["Vencer de qualquer jeito", "Discutir com o juiz", "Esconder a bola do outro time"], "Jogo limpo é competir com respeito às regras e às pessoas."),
        ],
        [QuizType.Diversidade] =
        [
            new("O que fazer quando um colega novo chega à turma?", "Convidar para brincar junto", ["Fingir que não viu", "Rir do jeito dele", "Esconder os brinquedos"], "Convidar o colega mostra acolhimento e respeito."),
            new("As pessoas são todas iguais na aparência?", "Não, cada pessoa tem o seu jeito", ["Sim, todas são idênticas", "Só as crianças são diferentes", "Só os adultos são diferentes"], "Cada pessoa é única, e todas merecem respeito."),
            new("O que uma rampa na entrada da escola facilita?", "A entrada de quem usa cadeira de rodas", ["A entrada da chuva", "A saída mais cedo", "O barulho no recreio"], "A rampa permite que pessoas em cadeira de rodas entrem sem ajuda."),
            new("O que significa respeitar um colega?", "Tratar bem, mesmo quando ele pensa diferente", ["Concordar com tudo sempre", "Falar mais alto que ele", "Escolher os amigos dele"], "Respeitar é tratar bem as pessoas, inclusive quando pensam diferente."),
        ],
    };
}
