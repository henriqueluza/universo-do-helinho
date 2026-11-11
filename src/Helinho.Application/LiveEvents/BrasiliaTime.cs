namespace Helinho.Application.LiveEvents;

/// <summary>Datas são gravadas em UTC; o painel exibe e lê no fuso de Brasília.</summary>
public static class BrasiliaTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTime ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone).DateTime;

    public static DateTimeOffset ToUtc(DateTime brasiliaLocal) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(brasiliaLocal, DateTimeKind.Unspecified), Zone), TimeSpan.Zero);
}
