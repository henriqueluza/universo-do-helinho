namespace Helinho.Application.Ai;

/// <summary>
/// <c>Ai:Provider</c>, <c>Ai:ApiKey</c>, <c>Ai:Model</c>, <c>Ai:TimeoutSeconds</c>, <c>Ai:DailyQuotaPerAdmin</c> e
/// <c>Ai:DailyGlobalLimit</c> (ADR-0004 §13 e §14). O padrão do código é IA desligada. É classe, não record: não
/// tem <c>ToString</c> que imprima a chave.
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";
    public const string Off = "Off";
    public const string Fake = "Fake";
    public const string Anthropic = "Anthropic";
    public const int TimeoutMin = 5;
    public const int TimeoutMax = 300;

    public string Provider { get; set; } = Off;
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 90;
    public int DailyQuotaPerAdmin { get; set; } = 60;
    public int DailyGlobalLimit { get; set; } = 300;

    public bool UsesFake => Is(Fake);
    public bool UsesAnthropic => Is(Anthropic);

    /// <summary>Falso com <c>Off</c>: botões ausentes, rotas 404 e casos de uso que recusam (ADR-0004 §14).</summary>
    public bool IsEnabled => UsesFake || UsesAnthropic;

    /// <summary>Erros de configuração. Cada mensagem começa pela variável e nunca traz o valor.</summary>
    public IEnumerable<string> Errors()
    {
        (bool Invalid, string Message)[] rules =
        [
            (!IsEnabled && !Is(Off), "Ai:Provider deve ser Off, Fake ou Anthropic."),
            // Só ASCII visível: a chave vai num cabeçalho HTTP.
            (UsesAnthropic && !IsVisibleAscii(ApiKey),
                "Ai:ApiKey é obrigatória com Ai:Provider=Anthropic e não pode ter espaço nem quebra de linha."),
            (UsesAnthropic && !IsVisibleAscii(Model),
                "Ai:Model é obrigatório com Ai:Provider=Anthropic e não pode ter espaço nem quebra de linha."),
            (TimeoutSeconds is < TimeoutMin or > TimeoutMax, $"Ai:TimeoutSeconds deve ser um número de {TimeoutMin} a {TimeoutMax}."),
            (DailyQuotaPerAdmin < 1, "Ai:DailyQuotaPerAdmin deve ser um número maior que zero."),
            (DailyGlobalLimit < 1, "Ai:DailyGlobalLimit deve ser um número maior que zero."),
        ];
        return rules.Where(rule => rule.Invalid).Select(rule => rule.Message);
    }

    private bool Is(string provider) => string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase);

    private static bool IsVisibleAscii(string value) => value.Length > 0 && value.All(c => c is > ' ' and <= '~');
}
