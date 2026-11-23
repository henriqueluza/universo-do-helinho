using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helinho.IntegrationTests;

/// <summary>Acesso ao jogo na ficha do aluno (#267, ADR-0003 §11.4): botão, situação do envio, recusas e auditoria. Dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public class AcessoAlunoTelaTests(PostgresFixture postgres, AcessoAlunoTelaTests.SharedHost shared) : IClassFixture<AcessoAlunoTelaTests.SharedHost>
{
    private const string AccessType = "email.student-access";
    private const string Handler = "?handler=EnviarAcesso";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class LogCollector : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add(formatter(state, exception));
            }
        }

        public void Dispose() { }
    }

    /// <summary>Um host para a classe (#215). Cada teste usa alunos próprios, criados aqui: sem senha, pendente, sem e-mail e um para o limite.</summary>
    public sealed class SharedHost : IAsyncDisposable
    {
        private readonly LogCollector _logs = new();
        private HelinhoWebFactory? _factory;

        internal Guid UnitId { get; private set; }
        internal Guid OtherUnitId { get; private set; }
        internal Guid WithPassword { get; private set; }
        internal Guid NoPassword { get; private set; }

        /// <summary>Sem senha e sem nenhum envio: só é lido. O teste que envia usa <see cref="NoPassword"/>, e a ordem dos testes não é fixa.</summary>
        internal Guid NeverSent { get; private set; }
        internal Guid Pending { get; private set; }
        internal Guid NoEmail { get; private set; }
        internal Guid Limited { get; private set; }

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is not null)
            {
                return _factory;
            }

            var factory = new HelinhoWebFactory(postgres, configure: s => s.AddLogging(b => b.AddProvider(_logs)));
            await factory.EnsureDatabaseAsync();
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seed = await db.Students.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Enrollment == "2026001", Ct); // aprovado, com senha
            (UnitId, WithPassword) = (seed.UnitId, seed.Id);
            OtherUnitId = await db.Units.Where(u => u.Id != seed.UnitId).Select(u => u.Id).FirstAsync(Ct);
            NoPassword = Add(db, "TELA-01", "tela01.responsavel@example.org", approved: true);
            Pending = Add(db, "TELA-02", "tela02.responsavel@example.org", approved: false);
            NoEmail = Add(db, "TELA-03", null, approved: true);
            Limited = Add(db, "TELA-04", "tela04.responsavel@example.org", approved: true);
            NeverSent = Add(db, "TELA-05", "tela05.responsavel@example.org", approved: true);
            await db.SaveChangesAsync(Ct);
            return _factory = factory;
        }

        internal IReadOnlyList<string> LogLines()
        {
            lock (_logs.Lines)
            {
                return [.. _logs.Lines];
            }
        }

        private Guid Add(AppDbContext db, string enrollment, string? guardianEmail, bool approved)
        {
            var user = new AppUser { UserName = enrollment.ToLowerInvariant(), UnitId = UnitId };
            db.Users.Add(user);
            var student = new Student(user.Id, UnitId, enrollment, "Aluno Sintético da Tela", "22.222.222-2", "222.222.222-22",
                "Rua Sintética, 2", new DateOnly(2017, 3, 1), "Responsável Fictício", "5A", new DateOnly(2026, 11, 22));
            if (guardianEmail is not null)
            {
                student.SetGuardianEmail(guardianEmail);
            }

            student.SetGuardianApproval(approved);
            db.Students.Add(student);
            return student.Id;
        }

        public async ValueTask DisposeAsync()
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }
        }
    }

    private async Task<HttpClient> AdminAsync(Guid? unitId = null) =>
        (await shared.GetAsync(postgres)).CreateFormClientAs(RoleNames.Admin, unitId ?? shared.UnitId);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid studentId, string tokenPage) =>
        PostFormAsync(client, $"/Alunos/Editar/{studentId}{Handler}", tokenPage, []);

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string tokenPage, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(tokenPage, Ct);
        fields["__RequestVerificationToken"] = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields), Ct);
    }

    private async Task<int> JobsAsync(Guid studentId, string? setStatus = null)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (setStatus is not null)
        {
            DateTimeOffset finishedAt = new(2026, 11, 22, 18, 30, 0, TimeSpan.Zero); // 15:30 em Brasília
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE "QueueJobs" SET "Status" = {setStatus}, "FinishedAt" = {finishedAt} WHERE "SubjectId" = {studentId}""", Ct);
        }

        return await db.QueueJobs.CountAsync(j => j.Type == AccessType && j.SubjectId == studentId, Ct);
    }

    [Fact]
    public async Task Ficha_oferece_o_botao_pelo_estado_da_conta_e_nao_repete_o_email_do_responsavel()
    {
        using var client = await AdminAsync();

        var first = await client.GetStringAsync($"/Alunos/Editar/{shared.NeverSent}", Ct);
        var change = await client.GetStringAsync($"/Alunos/Editar/{shared.WithPassword}", Ct);

        Assert.Contains("<h2 id=\"acesso-titulo\">Acesso ao jogo</h2>", first, StringComparison.Ordinal);
        Assert.Contains($"action=\"/Alunos/Editar/{shared.NeverSent}{Handler}\"", first, StringComparison.Ordinal);
        Assert.Contains("<button class=\"btn btn-primary\" type=\"submit\">Enviar acesso</button>", first, StringComparison.Ordinal);
        Assert.Contains("válido por 72 horas", first, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag tag-neutral\" id=\"acesso-situacao\">Nenhum envio ainda</span>", first, StringComparison.Ordinal);
        Assert.Contains("<button class=\"btn btn-primary\" type=\"submit\">Enviar troca de senha</button>", change, StringComparison.Ordinal);
        Assert.Contains("válido por 1 hora", change, StringComparison.Ordinal);
        Assert.DoesNotContain(">Enviar acesso<", change, StringComparison.Ordinal);
        // O endereço aparece só no campo do formulário; o painel de acesso não o repete.
        Assert.Equal(1, Regex.Count(first, "tela05\\.responsavel@example\\.org"));
    }

    [Theory]
    [InlineData(false, "Para enviar, marque a aprova&#xE7;&#xE3;o do respons&#xE1;vel e salve o cadastro.")]
    [InlineData(true, "Para enviar, preencha o e-mail do respons&#xE1;vel e salve o cadastro.")]
    public async Task Sem_aprovacao_ou_sem_email_a_ficha_explica_o_que_falta_e_o_post_forjado_nao_enfileira(bool noEmail, string explanation)
    {
        using var client = await AdminAsync();
        var studentId = noEmail ? shared.NoEmail : shared.Pending;

        var html = await client.GetStringAsync($"/Alunos/Editar/{studentId}", Ct);
        var forged = await PostAsync(client, studentId, $"/Alunos/Editar/{studentId}");

        Assert.Contains($"<div class=\"note info\" id=\"acesso-falta\"><svg aria-hidden=\"true\"><use href=\"#i-info\"/></svg><span>{explanation}</span></div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Handler, html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Equal(0, await JobsAsync(studentId));
    }

    [Fact]
    public async Task Enviar_enfileira_volta_para_a_ficha_com_a_situacao_e_registra_auditoria_so_com_ids()
    {
        using var client = await AdminAsync();
        var url = $"/Alunos/Editar/{shared.NoPassword}";

        var response = await PostAsync(client, shared.NoPassword, url);
        var queued = await client.GetStringAsync(url, Ct);
        var reloaded = await client.GetStringAsync(url, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(url, response.Headers.Location?.OriginalString);
        Assert.Equal(1, await JobsAsync(shared.NoPassword));
        Assert.Contains("<div class=\"note ok\" role=\"status\">", queued, StringComparison.Ordinal);
        Assert.Matches("<span class=\"tag tag-draft\" id=\"acesso-situacao\">Na fila desde \\d{2}/\\d{2}/\\d{4} &#xE0;s \\d{2}:\\d{2}</span>", queued);
        Assert.DoesNotContain("role=\"status\"", reloaded, StringComparison.Ordinal); // o aviso é só da resposta ao envio

        var userId = client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserHeader).Single();
        Assert.Single(shared.LogLines(), line => line == $"Acesso do aluno {shared.NoPassword} enfileirado pelo usuário {userId}.");
        Assert.DoesNotContain(shared.LogLines(), line => Regex.IsMatch(line, "tela01|Aluno Sintético|TELA-01"));

        // A situação acompanha o trabalho na fila (estados gravados pelo trabalhador; aqui, direto no banco).
        foreach (var (status, tag) in new[]
        {
            ("Succeeded", "<span class=\"tag tag-ok\" id=\"acesso-situacao\">Enviado em 22/11/2026 &#xE0;s 15:30</span>"),
            ("Failed", "<span class=\"tag tag-bad\" id=\"acesso-situacao\">Envio falhou em 22/11/2026 &#xE0;s 15:30</span>"),
            ("Cancelled", "<span class=\"tag tag-neutral\" id=\"acesso-situacao\">Envio cancelado em 22/11/2026 &#xE0;s 15:30</span>"),
        })
        {
            await JobsAsync(shared.NoPassword, setStatus: status);
            var html = await client.GetStringAsync(url, Ct);
            Assert.Contains(tag, html, StringComparison.Ordinal);
            Assert.Equal(status == "Failed", html.Contains("id=\"acesso-falhou\"", StringComparison.Ordinal));
            Assert.Contains(">Enviar acesso</button>", html, StringComparison.Ordinal); // dá para enviar de novo
        }
    }

    private static Dictionary<string, string> Fields(string fullName, string guardianEmail) => new()
    {
        ["Input.FullName"] = fullName,
        ["Input.Rg"] = "33.333.333-3",
        ["Input.Cpf"] = "529.982.247-25",
        ["Input.Address"] = "Rua Sintética, 3",
        ["Input.BirthDate"] = "2017-03-01",
        ["Input.ClassName"] = "5A",
        ["Input.GuardianName"] = "Responsável Fictício",
        ["Input.GuardianEmail"] = guardianEmail,
        ["Input.GuardianApproved"] = "true",
    };

    /// <summary>Cadastra pelo formulário e devolve o id do aluno criado.</summary>
    private async Task<Guid> CreateAsync(HttpClient client, string fullName, string guardianEmail)
    {
        var response = await PostFormAsync(client, "/Alunos/Novo", "/Alunos/Novo", Fields(fullName, guardianEmail));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Students.IgnoreQueryFilters()
            .Where(s => s.FullName == fullName).Select(s => s.Id).SingleAsync(Ct);
    }

    [Fact]
    public async Task Cadastrar_aluno_ja_aprovado_enfileira_o_acesso_e_registra_a_auditoria()
    {
        using var client = await AdminAsync();

        var studentId = await CreateAsync(client, "Caio Gatilho Sintético", "gatilho.responsavel@example.org");

        var userId = client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserHeader).Single();
        Assert.Equal(1, await JobsAsync(studentId));
        Assert.Contains($"Acesso do aluno {studentId} enfileirado pelo usuário {userId}.", shared.LogLines());
        Assert.DoesNotContain(shared.LogLines(), line => Regex.IsMatch(line, "gatilho\\.responsavel|Caio Gatilho"));
    }

    [Fact]
    public async Task Trocar_o_email_do_responsavel_registra_auditoria_so_com_ids_e_avisa_que_os_links_deixaram_de_valer()
    {
        using var client = await AdminAsync();
        var studentId = await CreateAsync(client, "Dora Troca Sintética", "troca.antes@example.org");
        var url = $"/Alunos/Editar/{studentId}";

        var response = await PostFormAsync(client, url, url, Fields("Dora Troca Sintética", "troca.depois@example.org"));
        var list = await client.GetStringAsync("/Alunos", Ct);
        var ficha = await client.GetStringAsync(url, Ct);

        var userId = client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserHeader).Single();
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("os links j&#xE1; enviados deixaram de valer", list, StringComparison.Ordinal);
        Assert.Contains("id=\"acesso-situacao\">Envio cancelado em ", ficha, StringComparison.Ordinal); // o acesso do cadastro foi cancelado
        Assert.Contains($"E-mail do responsável do aluno {studentId} trocado pelo usuário {userId}.", shared.LogLines());
        Assert.DoesNotContain(shared.LogLines(), line => Regex.IsMatch(line, "troca\\.(antes|depois)|Dora Troca"));
    }

    [Fact]
    public async Task Post_sem_token_e_recusado_e_aluno_de_outra_unidade_responde_404()
    {
        using var client = await AdminAsync();
        using var other = await AdminAsync(shared.OtherUnitId);
        using var master = (await shared.GetAsync(postgres)).CreateFormClientAs(RoleNames.Master);

        var withoutToken = await client.PostAsync($"/Alunos/Editar/{shared.WithPassword}{Handler}", new FormUrlEncodedContent([]), Ct);
        var otherGet = await other.GetAsync($"/Alunos/Editar/{shared.WithPassword}", Ct);
        var otherPost = await PostAsync(other, shared.WithPassword, "/Alunos/Novo");
        var masterPost = await master.PostAsync($"/Alunos/Editar/{shared.WithPassword}{Handler}", new FormUrlEncodedContent([]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherPost.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, masterPost.StatusCode); // a ficha é só do administrador da unidade
        Assert.Equal(0, await JobsAsync(shared.WithPassword));
    }

    [Fact]
    public async Task Limite_atingido_troca_o_botao_pela_explicacao_e_o_post_nao_enfileira_mais()
    {
        using var client = await AdminAsync();
        await using (var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
            for (var i = 0; i < 3; i++)
            {
                await queue.EnqueueAsync(new JobRequest(AccessType, shared.Limited, new JobPayload(), TimeSpan.FromHours(72)), Ct);
            }

            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
        }

        var html = await client.GetStringAsync($"/Alunos/Editar/{shared.Limited}", Ct);
        var forged = await PostAsync(client, shared.Limited, $"/Alunos/Editar/{shared.Limited}");

        Assert.Contains("Limite de 3 envios por hora atingido para este aluno. Tente de novo mais tarde.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Handler, html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Equal(3, await JobsAsync(shared.Limited));
    }
}
