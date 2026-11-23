using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.UnitTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Issue #301 (Issue 14 do ADR-0004, §17): a IA de ponta a ponta com o gerador Fake, as páginas reais, o processador
/// real da fila chamado direto e a API do jogo. Um host para a classe; cada teste começa com o mundo da IA zerado.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class AiPontaAPontaTests(PostgresFixture postgres, AiHostDeTeste host) : IClassFixture<AiHostDeTeste>
{
    private const string Pedir = "/Questoes/Ia";
    private const string Criador = "/Questoes/Editar";
    private static readonly string[] ForaDaLista = ["Rascunhos de IA", "/Questoes/Ia", "Gerar com IA", "btn-ai", "ia-pendentes"];
    private static readonly string[] ForaDoCriador = ["ai-help", "btn-ai", "Sugerir", "Modo de demonstração"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<textarea\\b[^>]*\\sname=\"([^\"]+)\"[^>]*>([^<]*)</textarea>")]
    private static partial Regex TextArea();

    [GeneratedRegex("<input\\b[^>]*>")]
    private static partial Regex InputTag();

    [GeneratedRegex("<select\\b[^>]*\\sname=\"([^\"]+)\"[^>]*>(.*?)</select>", RegexOptions.Singleline)]
    private static partial Regex Select();

    private static string? Attr(string tag, string name) =>
        Regex.Match(tag, $"\\s{name}=\"([^\"]*)\"") is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;

    /// <summary>O formulário como o navegador o enviaria: campos de texto, ocultos, opção marcada e opção selecionada.</summary>
    private static Dictionary<string, string> ReadForm(string html)
    {
        var form = new Dictionary<string, string>();
        foreach (Match area in TextArea().Matches(html))
        {
            form[area.Groups[1].Value] = WebUtility.HtmlDecode(area.Groups[2].Value).TrimStart('\r', '\n');
        }

        foreach (var tag in InputTag().Matches(html).Select(m => m.Value))
        {
            if (Attr(tag, "name") is { } name && (Attr(tag, "type") is not "radio" || tag.Contains("checked=", StringComparison.Ordinal)))
            {
                form[name] = Attr(tag, "value") ?? "";
            }
        }

        foreach (Match select in Select().Matches(html))
        {
            form[select.Groups[1].Value] = Regex.Match(select.Groups[2].Value, "<option value=\"([^\"]*)\" selected") is { Success: true } o ? o.Groups[1].Value : "";
        }

        return form;
    }

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string page, string? url = null, params (string Key, string Value)[] changes)
    {
        var form = ReadForm(await client.GetStringAsync(page, Ct));
        foreach (var (key, value) in changes)
        {
            form[key] = value;
        }

        return await client.PostAsync(url ?? page, new FormUrlEncodedContent(form), Ct);
    }

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, QuizType type, AgeBand band) =>
        SubmitAsync(client, Pedir, null, ("Input.QuizType", type.ToString()), ("Input.AgeBand", band.ToString()), ("Input.Count", "5"));

    private static async Task<string> PageAsync(HttpClient client, string url) => WebUtility.HtmlDecode(await client.GetStringAsync(url, Ct));

    /// <summary>Host da classe com a IA no modo pedido e sem questões, pedidos, sugestões nem trabalhos de testes anteriores.</summary>
    private async Task<HelinhoWebFactory> StartAsync(string provider = AiOptions.Fake, int quota = 60, int global = 300)
    {
        var factory = await host.GetAsync(postgres);
        host.Configure(provider, quota: quota, global: global);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.QuizAnswers.ExecuteDeleteAsync(Ct);
        await db.Questions.ExecuteDeleteAsync(Ct);
        await db.AiQuestionSuggestions.ExecuteDeleteAsync(Ct);
        await db.QueueJobs.ExecuteDeleteAsync(Ct);
        await db.AiGenerationJobs.ExecuteDeleteAsync(Ct);
        return factory;
    }

    private static async Task<T> DbAsync<T>(HelinhoWebFactory factory, Func<AppDbContext, Task<T>> read)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Aluno sintético e a faixa etária dele hoje: é a faixa das questões que o jogo lhe serve.</summary>
    private static Task<(Guid Id, AgeBand Band)> SeedStudentAsync(HelinhoWebFactory factory) => DbAsync(factory, async db =>
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = new Unit($"Unidade {suffix}");
        var user = new AppUser { UserName = $"aluno{suffix}", UnitId = unit.Id };
        db.Users.Add(user); // o Add gera o id do usuário
        db.Units.Add(unit);
        var student = new Student(user.Id, unit.Id, $"P-{suffix}", "Aluno Fictício", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", new DateOnly(2026, 11, 10));
        db.Students.Add(student);
        await db.SaveChangesAsync(Ct);
        return (student.Id, student.AgeBandAt(DateOnly.FromDateTime(DateTime.UtcNow)));
    });

    private static async Task<(HttpStatusCode Status, string Json, JsonElement Body)> RandomAsync(HttpClient game)
    {
        using var response = await game.GetAsync("/api/quiz/random?quiz_type=esportivo", Ct);
        var json = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, json, JsonSerializer.Deserialize<JsonElement>(json));
    }

    [Fact]
    public async Task Pedir_5_processar_revisar_e_so_as_aprovadas_chegam_ao_jogo()
    {
        var factory = await StartAsync();
        var (student, band) = await SeedStudentAsync(factory);
        using var admin = factory.CreateFormClientAs(RoleNames.Admin);
        using var game = factory.CreateStudentClient(student);

        // 1. pedir → na fila → o trabalhador processa → pronto com 5 sugestões
        using var asked = await AskAsync(admin, QuizType.Esportivo, band);
        Assert.Equal(HttpStatusCode.SeeOther, asked.StatusCode);
        var url = asked.Headers.Location!.OriginalString;
        Assert.Contains("Na fila para gerar", await PageAsync(admin, url), StringComparison.Ordinal);
        Assert.Equal((1, 0), ((await host.ProcessAsync(Ct)).Captured, (await host.ProcessAsync(Ct)).Captured));
        var ready = await admin.GetStringAsync(url, Ct);
        var ids = Regex.Matches(ready, "href=\"[^\"]*/editar/([0-9a-f-]{36})\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Contains("0 de 5 revisadas", ready, StringComparison.Ordinal);
        Assert.Contains("Modo de demonstração: sugestões fictícias.", WebUtility.HtmlDecode(ready), StringComparison.Ordinal);
        var list = await PageAsync(admin, "/Questoes");
        Assert.Contains("<b>5 sugestões da IA esperando revisão.</b>", list, StringComparison.Ordinal);
        Assert.Contains($"href=\"{url}\">Revisar agora</a>", list, StringComparison.Ordinal);

        // 2. sugestão pendente nunca chega ao jogo (ADR §17)
        Assert.Equal((HttpStatusCode.NotFound, "no_questions"), ((await RandomAsync(game)).Status, (await RandomAsync(game)).Body.GetProperty("error").GetString()));

        // 3. aprova 2, edita e salva 1, descarta 2
        var edited = "Qual esporte se joga com raquete e peteca, na versão editada pela equipe?";
        using var a1 = await SubmitAsync(admin, url, $"{url}?handler=Aprovar&id={ids[0]}");
        using var a2 = await SubmitAsync(admin, url, $"{url}?handler=Aprovar&id={ids[1]}");
        using var e1 = await SubmitAsync(admin, $"{url}/editar/{ids[2]}", null, ("Input.Statement", edited));
        using var d1 = await SubmitAsync(admin, url, $"{url}?handler=Descartar&id={ids[3]}");
        using var d2 = await SubmitAsync(admin, url, $"{url}?handler=Descartar&id={ids[4]}");
        Assert.All(new[] { a1, a2, e1, d1, d2 }, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));

        var questions = await DbAsync(factory, db => db.Questions.AsNoTracking().ToListAsync(Ct));
        var suggestions = await DbAsync(factory, db => db.AiQuestionSuggestions.AsNoTracking().OrderBy(s => s.Position).ToListAsync(Ct));
        var jobId = Guid.Parse(url.Split('/')[^1]);
        Assert.Equal(3, questions.Count);
        Assert.All(questions, q => Assert.Equal((QuestionOrigin.Ai, jobId, "gerar-questoes.v1", true, null),
            (q.Origin, q.AiJobId.GetValueOrDefault(), q.PromptVersion, q.GeneratedAt is not null, q.AiEditedAt)));
        var approved = questions.Where(q => q.Status == QuestionStatus.Approved).Select(q => q.Statement).ToList();
        var draft = Assert.Single(questions, q => q.Status == QuestionStatus.Draft);
        Assert.Equal([suggestions[0].Statement, suggestions[1].Statement], approved.OrderBy(s => s == suggestions[0].Statement ? 0 : 1));
        Assert.Equal(edited, draft.Statement);
        Assert.Equal(
            [AiSuggestionStatus.Approved, AiSuggestionStatus.Approved, AiSuggestionStatus.Edited, AiSuggestionStatus.Discarded, AiSuggestionStatus.Discarded],
            suggestions.Select(s => s.Status));
        var done = await PageAsync(admin, url);
        Assert.Contains("5 de 5 revisadas", done, StringComparison.Ordinal);
        list = await PageAsync(admin, "/Questoes?origem=Ai");
        Assert.Equal(3, Regex.Count(list, "<span class=\"tag tag-ai\">"));
        Assert.DoesNotContain("ia-pendentes", list, StringComparison.Ordinal);
        Assert.DoesNotContain("· editada", list, StringComparison.Ordinal); // editar na entrada não é edição posterior (ADR §9.4)

        // 4. o jogo (contrato inalterado) só serve as 2 aprovadas: nem a editada (rascunho) nem as descartadas
        var first = await RandomAsync(game);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Empty(JsonUtilityContract.Verify(first.Json));
        Assert.Equal(["question_id", "quiz_type", "statement", "options", "position", "total", "time_limit"], first.Body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, first.Body.GetProperty("total").GetInt32());
        using var answer = await game.PostAsJsonAsync($"/api/quiz/{first.Body.GetProperty("question_id").GetString()}/answer", new { option_id = "a" }, Ct);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var second = await RandomAsync(game);
        Assert.Equal(approved.Order(), new[] { first, second }.Select(r => r.Body.GetProperty("statement").GetString()).Order());

        // 5. edição posterior no criador: a questão continua de origem IA e ganha a marca de editada
        using var saved = await SubmitAsync(admin, $"{Criador}/{draft.Id}", null, ("Input.Explanation", "Explicação revista pela equipe."));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var again = await DbAsync(factory, db => db.Questions.AsNoTracking().SingleAsync(q => q.Id == draft.Id, Ct));
        Assert.Equal((QuestionOrigin.Ai, true), (again.Origin, again.AiEditedAt is not null));
        Assert.Contains("IA · editada", await PageAsync(admin, "/Questoes?origem=Ai"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cota_por_administrador_e_limite_global_recusam_o_pedido_alem_do_limite_com_a_mensagem_certa()
    {
        var factory = await StartAsync(quota: 5, global: 10);
        using var ana = factory.CreateFormClientAs(RoleNames.Admin);
        using var bia = factory.CreateFormClientAs(RoleNames.Admin);
        using var caio = factory.CreateFormClientAs(RoleNames.Master);

        using var anaFirst = await AskAsync(ana, QuizType.Cultural, AgeBand.Age8To10);
        using var anaSecond = await AskAsync(ana, QuizType.Cultural, AgeBand.Age11To12); // outro pedido: a cota dela (5) acabou
        using var biaFirst = await AskAsync(bia, QuizType.Ambiental, AgeBand.Age8To10); // 10 no total: o limite global acabou
        using var caioFirst = await AskAsync(caio, QuizType.Esportivo, AgeBand.Age8To10);

        Assert.Equal((HttpStatusCode.SeeOther, HttpStatusCode.SeeOther), (anaFirst.StatusCode, biaFirst.StatusCode));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (anaSecond.StatusCode, caioFirst.StatusCode));
        var own = WebUtility.HtmlDecode(await anaSecond.Content.ReadAsStringAsync(Ct));
        var everyone = WebUtility.HtmlDecode(await caioFirst.Content.ReadAsStringAsync(Ct));
        Assert.Matches("Você chegou ao seu limite diário \\(5 sugestões\\)\\. Não sobrou nenhuma\\. O saldo volta às \\d\\d:\\d\\d de \\d\\d/\\d\\d\\.", own);
        Assert.Matches("O limite diário de toda a equipe \\(10 sugestões\\) foi atingido\\. Não sobrou nenhuma\\. O saldo volta às \\d\\d:\\d\\d de \\d\\d/\\d\\d\\.", everyone);
        Assert.Equal((2, 2), await DbAsync(factory, async db => (await db.AiGenerationJobs.CountAsync(Ct), await db.QueueJobs.CountAsync(Ct)))); // recusado não cria pedido nem trabalho
    }

    [Fact]
    public async Task Com_a_IA_desligada_as_rotas_respondem_404_os_botoes_somem_e_o_selo_de_origem_continua()
    {
        var factory = await StartAsync();
        using var admin = factory.CreateFormClientAs(RoleNames.Master);
        using var sugerida = await SubmitAsync(admin, Criador, $"{Criador}?handler=Sugerir", ("Input.QuizType", "Cultural"), ("Input.AgeBand", "Age8To10"));
        using var salva = await admin.PostAsync(Criador, new FormUrlEncodedContent(ReadForm(await sugerida.Content.ReadAsStringAsync(Ct))), Ct);
        using var pedido = await AskAsync(admin, QuizType.Cultural, AgeBand.Age8To10);
        Assert.Equal((HttpStatusCode.Redirect, HttpStatusCode.SeeOther), (salva.StatusCode, pedido.StatusCode));

        host.Configure(AiOptions.Off);
        using var pedir = await admin.GetAsync(Pedir, Ct);
        using var acompanhar = await admin.GetAsync(pedido.Headers.Location!.OriginalString, Ct);
        using var sugerir = await admin.PostAsync($"{Criador}?handler=Sugerir", new FormUrlEncodedContent(ReadForm(await admin.GetStringAsync(Criador, Ct))), Ct);
        var list = await PageAsync(admin, "/Questoes");
        var maker = await PageAsync(admin, Criador);

        Assert.All(new[] { pedir, acompanhar, sugerir }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.All(ForaDaLista, s => Assert.DoesNotContain(s, list, StringComparison.Ordinal));
        Assert.All(ForaDoCriador, s => Assert.DoesNotContain(s, maker, StringComparison.Ordinal));
        Assert.Contains("<span class=\"tag tag-ai\">", list, StringComparison.Ordinal); // a origem é fato sobre o texto: o selo fica
    }

    [Fact]
    public async Task Sugerir_no_criador_preenche_o_formulario_salva_com_origem_IA_e_o_texto_do_modelo_sai_codificado()
    {
        var factory = await StartAsync();
        using var admin = factory.CreateFormClientAs(RoleNames.Admin);
        const string Hostile = "Quem disse \"3 < 5\" & 'tchau' na aula de matemática?";
        QuestionDraftBatch Model(string statement) => new([new QuestionDraft(statement, ["A&B", "C\"D", "E'F", "7 < 9"], 1, "Porque 3 < 5 & 5 > 3.")],
            "gerar-questoes.v1", new AiUsage("Stub", "modelo-sintetico", 1, 1, TimeSpan.Zero));

        // texto hostil do "modelo": aparece só codificado, e volta inteiro ao salvar
        host.Probe.Script = _ => Model(Hostile);
        using var suggested = await SubmitAsync(admin, Criador, $"{Criador}?handler=Sugerir", ("Input.QuizType", "Cultural"), ("Input.AgeBand", "Age8To10"));
        var raw = await suggested.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Quem disse &quot;3 &lt; 5&quot; &amp; &#x27;tchau&#x27; na aula de ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"3 < 5\"", raw, StringComparison.Ordinal);
        Assert.Contains("value=\"7 &lt; 9\"", raw, StringComparison.Ordinal);
        Assert.Contains("Sugerida pela IA · confira antes de salvar", WebUtility.HtmlDecode(raw), StringComparison.Ordinal);
        Assert.Equal(0, await DbAsync(factory, db => db.Questions.CountAsync(Ct))); // "Sugerir" nunca salva

        using var saved = await admin.PostAsync(Criador, new FormUrlEncodedContent(ReadForm(raw)), Ct);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var question = await DbAsync(factory, db => db.Questions.AsNoTracking().Include(q => q.Options).SingleAsync(Ct));
        var job = await DbAsync(factory, db => db.AiGenerationJobs.AsNoTracking().SingleAsync(Ct));
        Assert.Equal((QuestionOrigin.Ai, QuestionStatus.Draft, Hostile, job.Id), (question.Origin, question.Status, question.Statement, question.AiJobId.GetValueOrDefault()));
        Assert.Equal((AiJobKind.Single, 1), (job.Kind, job.RequestedCount));
        Assert.Equal(["A&B", "C\"D", "E'F", "7 < 9"], question.Options.OrderBy(o => o.Key).Select(o => o.Label));
        var list = await admin.GetStringAsync("/Questoes", Ct);
        Assert.Contains("&quot;3 &lt; 5&quot; &amp;", list, StringComparison.Ordinal);
        Assert.DoesNotContain("\"3 < 5\"", list, StringComparison.Ordinal);

        // marcação: o validador descarta antes de chegar ao formulário, e nada dela aparece na página
        host.Probe.Script = _ => Model("Qual é a pergunta <script>alert(1)</script> do modelo?");
        using var refused = await SubmitAsync(admin, Criador, $"{Criador}?handler=Sugerir", ("Input.QuizType", "Cultural"), ("Input.AgeBand", "Age8To10"));
        var page = await refused.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("alert(1)", page, StringComparison.Ordinal);
        Assert.Contains("A sugestão veio com problema e foi descartada.", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
    }
}
