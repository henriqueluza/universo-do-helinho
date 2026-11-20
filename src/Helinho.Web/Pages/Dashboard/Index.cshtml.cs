using System.Globalization;
using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Dashboard;

public sealed class IndexModel(IAppDbContext db, ICurrentUser user, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public AgeBand? Faixa { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Dias { get; set; } = GetDashboard.DefaultDays;

    [BindProperty(SupportsGet = true)]
    public Guid? Unidade { get; set; }

    public bool IsMaster => user.IsInRole(RoleNames.Master);

    public DashboardResult Result { get; private set; } = null!;

    public DashboardTrends Trends { get; private set; } = null!;

    public DashboardEngagement Engagement { get; private set; } = null!;

    public DashboardRankings Rankings { get; private set; } = null!;

    public DashboardAttention Attention { get; private set; } = null!;

    public string Greeting { get; private set; } = "Olá";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Dias = GetDashboard.NormalizeDays(Dias);
        var query = new DashboardQuery(Faixa, Dias, Unidade);
        Result = await GetDashboard.HandleAsync(db, user, clock, query, cancellationToken);
        Trends = await GetDashboardTrends.HandleAsync(db, user, clock, query, cancellationToken);
        Engagement = await GetDashboardEngagement.HandleAsync(db, user, clock, query, cancellationToken);
        Rankings = await GetDashboardRankings.HandleAsync(db, user, clock, query, cancellationToken);
        Attention = await GetDashboardAttention.HandleAsync(db, user, clock, query, cancellationToken);
        Greeting = BrasiliaTime.ToLocal(clock.UtcNow).Hour switch { < 12 => "Bom dia", < 18 => "Boa tarde", _ => "Boa noite" };
    }

    public bool HasFilters => Faixa is not null || Unidade is not null || Dias != GetDashboard.DefaultDays;

    public static string BandLabel(AgeBand band) => band switch
    {
        AgeBand.Age6To7 => "6–7",
        AgeBand.Age8To10 => "8–10",
        AgeBand.Age11To12 => "11–12",
        _ => "13–14",
    };

    public static string TypeLabel(QuizType type) => type switch
    {
        QuizType.Ambiental => "Ambiental",
        QuizType.Conhecimento => "Conhecimento",
        QuizType.Esportivo => "Esportivo",
        QuizType.Cultural => "Cultural",
        _ => "Diversidade",
    };

    public static string TypeShort(QuizType type) => type switch
    {
        QuizType.Ambiental => "amb",
        QuizType.Conhecimento => "con",
        QuizType.Esportivo => "esp",
        QuizType.Cultural => "cul",
        _ => "div",
    };

    /// <summary>Personagem do tema: nome, artigo, arquivo sem extensão e largura da imagem de corpo inteiro.</summary>
    public static (string Name, string With, string Slug, int BodyWidth) Character(QuizType type) => type switch
    {
        QuizType.Ambiental => ("Flora", "com a Flora", "flora", 159),
        QuizType.Conhecimento => ("Byte", "com o Byte", "byte", 185),
        QuizType.Esportivo => ("Max", "com o Max", "max", 170),
        QuizType.Cultural => ("Luna", "com a Luna", "luna", 141),
        _ => ("Lia", "com a Lia", "lia", 125),
    };

    public static string TypeClass(QuizType type) => "quiz-" + type.ToString().ToLowerInvariant();

    /// <summary>Título do alerta, montado aqui porque os rótulos de tipo moram na tela. Texto cadastrado sai cru (o Razor codifica).</summary>
    public static string AlertTitle(AttentionAlert a) => a.Kind switch
    {
        AttentionKind.InactiveStudents => $"{a.Value} {(a.Value == 1 ? "aluno" : "alunos")} sem jogar há {GetDashboardAttention.InactiveAfterDays} dias",
        AttentionKind.LowAccuracy => $"{(a.QuizType is { } t ? TypeLabel(t) : "Acerto")} abaixo de {GetDashboardAttention.LowAccuracyPercent}%",
        AttentionKind.DraftQuestions => $"{a.Value} {(a.Value == 1 ? "questão" : "questões")} em rascunho",
        _ => $"{a.EventTitle} termina {(a.Value == 0 ? "hoje" : a.Value == 1 ? "amanhã" : $"em {a.Value} dias")}",
    };

    /// <summary>Destino do alerta: só rotas que existem (o caso de uso devolve o <see cref="AttentionTarget"/> e os filtros).</summary>
    public string AlertHref(AttentionAlert a) => a.Target switch
    {
        AttentionTarget.Students => Url.Page("/Alunos/Index", new { unidade = a.UnitId, turma = a.ClassName })!,
        AttentionTarget.Performance => Url.Page("/Desempenho/Index")!,
        AttentionTarget.Questions => Url.Page("/Questoes/Index", new { status = QuestionStatus.Draft })!,
        _ => Url.Page("/Eventos/Index")!,
    };

    public static string AlertIcon(AttentionAlert a) => a.Target switch
    {
        AttentionTarget.Students => "i-alunos",
        AttentionTarget.Performance => "i-desemp",
        AttentionTarget.Questions => "i-quest",
        _ => "i-event",
    };

    public static string AlertStyle(AttentionAlert a) => a.Kind switch
    {
        AttentionKind.InactiveStudents => "background:var(--warn-bg);color:var(--warn-ink)",
        AttentionKind.LowAccuracy => "background:var(--qb);color:var(--qi)",
        AttentionKind.DraftQuestions => "background:var(--neutral-bg);color:var(--neutral-ink)",
        _ => "background:var(--select-bg);color:var(--select-ink)",
    };

    public string Link(AgeBand? faixa, int? dias = null, Guid? unidade = null) =>
        Url.Page("/Dashboard/Index", new { faixa, dias = dias ?? Dias, unidade = unidade ?? Unidade })!;

    private static readonly string[] DayNames = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];
    private static readonly string[] DayFull = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];
    private static readonly string[] Months = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    /// <summary>Inteiro com ponto de milhar (a cultura do processo é invariante).</summary>
    public static string Num(int n) => n.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

    public static string Inv(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static string DayShort(DateOnly d) => DayNames[(int)d.DayOfWeek];

    public static string DayLabel(DateOnly d) => $"{DayNames[(int)d.DayOfWeek]}, {DateShort(d)}";

    public static string DayName(DateOnly d) => DayFull[(int)d.DayOfWeek];

    public static string DateShort(DateOnly d) => $"{d.Day:00}/{Months[d.Month - 1]}";

    /// <summary>Cor da variação: <c>upIsGood</c> nulo = neutra. Sem direção (sem base) também é neutra.</summary>
    public static string DeltaClass(Trend t, bool? upIsGood) => (t.Direction, upIsGood) switch
    {
        (TrendDirection.Up, { } good) => good ? "good" : "bad",
        (TrendDirection.Down, { } good) => good ? "bad" : "good",
        _ => "flat",
    };

    public static string? ArrowIcon(Trend t) => t.Direction switch { TrendDirection.Up => "i-up", TrendDirection.Down => "i-down", _ => null };

    /// <summary>Série do minigráfico: dias sem valor ficam de fora; com menos de dois pontos não há desenho.</summary>
    public static string? Spark(IEnumerable<double?> values)
    {
        var v = values.OfType<double>().Select(Inv).ToList();
        return v.Count >= 2 ? string.Join(',', v) : null;
    }
}

/// <summary>Um indicador do Painel (cartão <c>.kpi</c>): <c>Href</c> nulo = cartão sem link.</summary>
public sealed record KpiView(string Title, string Icon, string Value, string Unit, Trend Trend, bool? UpIsGood, string Context, string? Spark, string? Href = null);
