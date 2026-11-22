using System.Globalization;
using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

/// <summary>Tipos de alerta de "Precisa de atenção". A ordem do enum é a ordem em que saem (a da preview).</summary>
public enum AttentionKind
{
    /// <summary>Turma com mais alunos sem jogar. <c>Value</c> = alunos; leva <c>UnitId</c> e <c>ClassName</c>.</summary>
    InactiveStudents,
    /// <summary>Tipo de quiz com acerto abaixo do limiar. <c>Value</c> = taxa do período (%); leva <c>QuizType</c>.</summary>
    LowAccuracy,
    /// <summary>Questões em rascunho (conteúdo global). <c>Value</c> = quantidade.</summary>
    DraftQuestions,
    /// <summary>Evento que termina em breve (conteúdo global). <c>Value</c> = dias civis até o fim (0 = hoje);
    /// leva <c>EventTitle</c> e <c>QuizType</c>.</summary>
    EventEnding,
}

/// <summary>Página do painel para onde o alerta leva. A rota e a query string quem monta é a tela.</summary>
public enum AttentionTarget { Students, Performance, Questions, Events }

/// <summary>
/// Um alerta: tipo, número principal, linha de apoio e o que a tela precisa para montar o título e o link. Sem HTML e
/// sem rota. Privacidade (T4 do ADR-0001; os alunos são crianças): nenhum alerta identifica aluno; de aluno só sai a
/// contagem por unidade e turma. <c>Detail</c> e <c>EventTitle</c> carregam texto cadastrado no painel (nome da
/// unidade, turma, título do evento) e saem crus: quem renderiza faz o encode.
/// </summary>
public sealed record AttentionAlert(
    AttentionKind Kind, int Value, string Detail,
    QuizType? QuizType = null, Guid? UnitId = null, string? ClassName = null, string? EventTitle = null)
{
    /// <summary>Alunos (filtros unidade e turma), Desempenho, Questões (status rascunho) ou Eventos.</summary>
    public AttentionTarget Target => Kind switch
    {
        AttentionKind.InactiveStudents => AttentionTarget.Students,
        AttentionKind.LowAccuracy => AttentionTarget.Performance,
        AttentionKind.DraftQuestions => AttentionTarget.Questions,
        _ => AttentionTarget.Events,
    };
}

/// <summary>Sem nada para alertar a lista vem vazia: a tela não desenha item nenhum.</summary>
public sealed record DashboardAttention(IReadOnlyList<AttentionAlert> Alerts)
{
    public bool IsEmpty => Alerts.Count == 0;
}

/// <summary>
/// Bloco "Precisa de atenção" do Painel Recreio. Mesmo período (dias civis de <c>America/Sao_Paulo</c>), filtros e
/// escopo de unidade do <see cref="GetDashboardTrends"/> para os alertas de aluno e de acerto: administrador só recebe
/// o que foi calculado com alunos e respostas da própria unidade (T1 do ADR-0001). Questões em rascunho e evento são
/// conteúdo global: aparecem iguais para master e administrador e ignoram os filtros. Quatro consultas agregadas no
/// banco, em número fixo. Fica de fora por falta de dado: "evento começa em…" e "N de M perguntas prontas"
/// (<c>LiveEvent</c> só tem <c>EndsAt</c> e não se relaciona com <c>Question</c>).
/// PROPOSTA (ordem de gravidade, a validar com a contratante): a da preview — alunos parados, tipos com acerto baixo
/// (o pior primeiro; no empate, a ordem do enum), rascunhos, evento. O corte de <see cref="MaxAlerts"/> vem depois.
/// </summary>
public static class GetDashboardAttention
{
    /// <summary>PROPOSTA (limiar da preview): sem jogar = sem resposta nos 7 dias civis que terminam hoje, qualquer
    /// que seja o período do filtro. Quem nunca respondeu conta. É a mesma regra do indicador "sem jogar há 7+ dias".</summary>
    public const int InactiveAfterDays = 7;

    /// <summary>PROPOSTA (limiar da preview): o tipo vira alerta quando a taxa do período, já arredondada como a tela
    /// mostra, fica abaixo disto. 65% exatos não alertam.</summary>
    public const int LowAccuracyPercent = 65;

    /// <summary>PROPOSTA: com menos respostas que isto no período o tipo não vira alerta, para uma única resposta
    /// errada não virar "0% de acerto". Mesmo número do mínimo de "Questões que mais confundem".</summary>
    public const int MinAnswers = 5;

    /// <summary>PROPOSTA (antecedência): o evento vira alerta quando termina hoje ou nos próximos 3 dias civis.</summary>
    public const int EventWarningDays = 3;

    /// <summary>PROPOSTA: a preview mostra quatro itens; o que passar disso, na ordem de gravidade, não sai.</summary>
    public const int MaxAlerts = 4;

    public const string DraftsDetail = "Só questão aprovada chega ao jogo";

    public static async Task<DashboardAttention> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, DashboardQuery query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var days = GetDashboard.NormalizeDays(query.Days);
        var today = DateOnly.FromDateTime(BrasiliaTime.ToLocal(now));
        var from = today.AddDays(1 - days);
        var (start, previousStart, end) =
            (GetDashboardTrends.StartOf(from), GetDashboardTrends.StartOf(from.AddDays(-days)), GetDashboardTrends.StartOf(today.AddDays(1)));
        var activeSince = GetDashboardTrends.StartOf(today.AddDays(1 - InactiveAfterDays));
        var eventLimit = GetDashboardTrends.StartOf(today.AddDays(1 + EventWarningDays));
        var students = GetDashboard.ScopedStudents(db, user, query, today);

        var played = from a in db.QuizAnswers
                     where a.AnsweredAt >= activeSince && a.AnsweredAt < end
                     join qs in db.QuizSessions on a.SessionId equals qs.Id
                     select qs.StudentId;
        var idle = await (from s in students
                          where !played.Contains(s.Id)
                          join u in db.Units on s.UnitId equals u.Id
                          group s by new { u.Id, u.Name, s.ClassName } into g
                          orderby g.Count() descending, g.Key.Name, g.Key.ClassName, g.Key.Id
                          select new { g.Key.Id, g.Key.Name, g.Key.ClassName, Students = g.Count() })
            .FirstOrDefaultAsync(cancellationToken);
        var byType = await (from a in db.QuizAnswers
                            where a.AnsweredAt >= previousStart && a.AnsweredAt < end
                            join qs in db.QuizSessions on a.SessionId equals qs.Id
                            join s in students on qs.StudentId equals s.Id
                            group a by new { qs.QuizType, IsCurrent = a.AnsweredAt >= start } into g
                            select new { g.Key.QuizType, g.Key.IsCurrent, Total = g.Count(), Correct = g.Count(x => x.IsCorrect) })
            .ToListAsync(cancellationToken);
        var drafts = await db.Questions.CountAsync(q => q.Status == QuestionStatus.Draft, cancellationToken);
        var ending = await db.LiveEvents.Where(e => e.EndsAt > now && e.EndsAt < eventLimit)
            .OrderBy(e => e.EndsAt).ThenBy(e => e.Id)
            .Select(e => new { e.Title, e.QuizType, e.EndsAt })
            .FirstOrDefaultAsync(cancellationToken);

        var alerts = new List<AttentionAlert>();
        if (idle is not null)
        {
            alerts.Add(new(AttentionKind.InactiveStudents, idle.Students, $"{idle.Name}, turma {idle.ClassName}",
                UnitId: idle.Id, ClassName: idle.ClassName));
        }

        var previous = byType.Where(r => !r.IsCurrent).ToDictionary(r => r.QuizType, r => GetDashboardTrends.Percent(r.Correct, r.Total));
        alerts.AddRange(from r in byType
                        where r.IsCurrent && r.Total >= MinAnswers
                        let percent = GetDashboardTrends.Percent(r.Correct, r.Total) ?? 0
                        where percent < LowAccuracyPercent
                        orderby percent, r.QuizType
                        select new AttentionAlert(
                            AttentionKind.LowAccuracy, percent, Trend.OfPoints(percent, previous.GetValueOrDefault(r.QuizType)).Text, r.QuizType));
        if (drafts > 0)
        {
            alerts.Add(new(AttentionKind.DraftQuestions, drafts, DraftsDetail));
        }

        if (ending is not null)
        {
            var endsAt = BrasiliaTime.ToLocal(ending.EndsAt);
            alerts.Add(new(AttentionKind.EventEnding, DateOnly.FromDateTime(endsAt).DayNumber - today.DayNumber,
                $"até {endsAt.ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture)}", ending.QuizType, EventTitle: ending.Title));
        }

        return new DashboardAttention([.. alerts.Take(MaxAlerts)]);
    }
}
