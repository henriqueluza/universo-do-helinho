using System.Globalization;
using Helinho.Application.Ai;

namespace Helinho.Web.Pages.Questoes.Ia;

/// <summary>Textos da tela de IA (ADR-0004 §15): o código fechado de falha vira frase para a pessoa que administra, sem jargão.</summary>
public static class IaTexto
{
    public const int PollSeconds = 5;
    public static readonly TimeSpan PollFor = TimeSpan.FromMinutes(2);
    public static readonly int[] Quantities = [5, 10, 20];
    public const string DemoNotice = "Modo de demonstração: sugestões fictícias.";
    public const string CheckNotice = "Este texto veio de uma IA e pode ter erros. Confira os fatos, a alternativa certa e a linguagem para a idade antes de aprovar.";

    /// <summary>Código gravado no pedido (<see cref="AiFailureCodes"/>) para a mensagem mostrada. Código desconhecido cai na genérica.</summary>
    public static string Failure(string? code) => code switch
    {
        AiFailureCodes.Timeout => "A IA demorou demais. Tente de novo.",
        "ai_unavailable" or "ai_rate_limited" => "A IA está indisponível agora. Tente de novo em alguns minutos.",
        "ai_refused" => "A IA não gerou sugestões para este assunto. Tente outro assunto.",
        "ai_invalid_output" => "As sugestões vieram com problema e foram descartadas. Tente de novo.",
        "ai_misconfigured" => "A IA não está configurada. Avise o responsável técnico.",
        AiFailureCodes.Expired or "max_attempts" => "O pedido não terminou a tempo. Tente de novo.",
        _ => "Não foi possível gerar as sugestões. Tente de novo.",
    };

    /// <summary>Hora de Brasília (UTC−3, sem horário de verão desde 2019) para dizer quando a cota volta.</summary>
    public static string LocalTime(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(-3)).ToString("HH:mm 'de' dd/MM", CultureInfo.GetCultureInfo("pt-BR"));

    public static string Quota(AiQuotaExceeded quota, int requested)
    {
        var who = quota.Global
            ? $"O limite diário de toda a equipe ({quota.Limit} sugestões) foi atingido."
            : $"Você chegou ao seu limite diário ({quota.Limit} sugestões).";
        var left = quota.Remaining == 0 ? "Não sobrou nenhuma." : $"Ainda dá para pedir {quota.Remaining}, e você pediu {requested}.";
        var back = quota.AvailableAt is { } at
            ? $"O saldo volta às {LocalTime(at)}."
            : "Peça uma quantidade menor.";
        return $"{who} {left} {back}";
    }
}
