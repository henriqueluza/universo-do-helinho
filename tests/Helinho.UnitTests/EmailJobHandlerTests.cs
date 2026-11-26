using System.Text.Json;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.UnitTests;

/// <summary>Tratador de e-mail (#270 e #272, ADR-0003 §4.2, §5.3, §11 e §15) contra portas falsas e um envio que só captura. Dados sintéticos.</summary>
public class EmailJobHandlerTests
{
    private const string RawToken = "CfDJ8+token/sintetico=="; // com os caracteres que o Base64 comum tem e a URL não aceita
    private const string GuardianEmail = "responsavel@example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 20, 30, TimeSpan.Zero);
    private static readonly FixedClock Clock = new();
    private static readonly Guid JobId = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StudentUserId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid StudentId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
    private static readonly EmailSubject Admin = new(AdminId, "marina.teste", "marina@example.com", "Unidade Norte (fictícia)", HasPassword: false, IsPanelAccount: true);
    private static readonly EmailStudent Ana = new("Ana", "2026-0042", GuardianEmail, "anterior@example.org", Approved: true);
    private static readonly EmailSubject Student = new(StudentUserId, "2026-0042", null, "Unidade Norte (fictícia)", HasPassword: false, IsPanelAccount: false, Ana);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Subjects(params EmailSubject[] known) : IEmailSubjects
    {
        public List<(Guid UserId, string Provider)> Tokens { get; } = [];

        /// <summary>Alunos cujo endereço anterior o tratador mandou apagar (§9).</summary>
        public List<Guid> Cleared { get; } = [];

        public Task<EmailSubject?> FindByUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(known.FirstOrDefault(s => s.UserId == userId));

        public Task<EmailSubject?> FindByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
            Task.FromResult(studentId == StudentId ? known.FirstOrDefault(s => s.Student is not null) : null);

        public Task<string> GenerateLinkTokenAsync(Guid userId, string provider, CancellationToken cancellationToken)
        {
            Tokens.Add((userId, provider));
            return Task.FromResult(RawToken);
        }

        public Task StagePreviousGuardianEmailRemovalAsync(Guid studentId, CancellationToken cancellationToken)
        {
            Cleared.Add(studentId);
            return Task.CompletedTask;
        }
    }

    private sealed class Sender(EmailSendResult result) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.FromResult(result);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Counter(int sent) : IEmailDailyCount
    {
        public List<DateTimeOffset> Asked { get; } = [];

        public Task<int> SentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            Asked.Add(since);
            return Task.FromResult(sent);
        }
    }

    /// <summary>O registro que atende o tipo, como o trabalhador o escolhe; sem teto diário, salvo quando o teste pede.</summary>
    private static EmailJobHandler Handler(string type, Subjects subjects, Sender sender, int limit = 0, Counter? counter = null) => new(
        EmailLane.Urgent.Types.Contains(type) ? EmailLane.Urgent : EmailLane.Others,
        new EmailDailyLimit(limit, counter ?? new Counter(0), Clock), subjects, EmailTemplateRendererTests.Renderer, sender, Clock);

    private static ServiceProvider Provider(string dailyLimit, Subjects subjects, Sender sender) => new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:DailyLimit"] = dailyLimit }).Build())
        .AddEmailTemplates(_ => new(PublicBaseUrl.Parse("", allowHttpLoopback: false), "FUNDHAS", "contato@example.org"))
        .AddEmailJobs()
        .AddScoped<IEmailSubjects>(_ => subjects).AddScoped<IEmailDailyCount>(_ => new Counter(0))
        .AddSingleton<IEmailSender>(sender).AddSingleton<IClock>(Clock)
        .BuildServiceProvider();

    private static async Task<(JobResult Result, Subjects Subjects, Sender Sender)> RunAsync(
        string type, Guid? subjectId, EmailSendResult? answer = null, params EmailSubject[] known)
    {
        var subjects = new Subjects(known);
        var sender = new Sender(answer ?? new(EmailSendStatus.Sent, "id-sintetico@example.org"));
        return (await Handler(type, subjects, sender).HandleAsync(Job(type, subjectId), Ct), subjects, sender);
    }

    private static JobContext Job(string type, Guid? subjectId, DateTimeOffset? createdAt = null) =>
        new(JobId, type, subjectId, JsonDocument.Parse("""{"v":1}""").RootElement, 1, createdAt ?? Now, (createdAt ?? Now).AddHours(24));

    /// <summary>O token como a página de definir senha o lê do link (§10.3).</summary>
    private static string? TokenIn(string body) => LinkDeAcesso.LerToken(Regex.Match(body, "token=([A-Za-z0-9_-]+)").Groups[1].Value);

    [Fact]
    public void Os_dois_registros_dividem_os_cinco_tipos_sem_repeticao_com_a_politica_de_email_do_ADR()
    {
        var handler = Handler(EmailJobTypes.PasswordReset, new Subjects(), new Sender(new(EmailSendStatus.Sent)));

        Assert.Equal(["email.admin-invite", "email.password-reset"], EmailLane.Urgent.Types.Order());
        Assert.Equal(["email.guardian-email-changed", "email.password-changed", "email.student-access"], EmailLane.Others.Types.Order());
        Assert.Same(EmailLane.Urgent.Types, handler.Types);
        Assert.Equal(
            new JobPolicy(8, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), 10, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2)), handler.Policy);
    }

    [Theory]
    [InlineData(0, 5000, false, false)] // sem teto, nem conta
    [InlineData(300, 239, false, false)]
    [InlineData(300, 240, false, true)] // de 80% em diante só os urgentes saem
    [InlineData(300, 299, false, true)]
    [InlineData(300, 300, true, true)] // no teto, os dois registros esperam
    [InlineData(300, 450, true, true)]
    public async Task Teto_diario_suspende_os_demais_em_80_por_cento_e_os_urgentes_no_teto_ate_a_hora_seguinte(int limit, int sent, bool urgentWaits, bool othersWait)
    {
        (string Type, Guid Id, bool Waits)[] cases =
        [
            (EmailJobTypes.PasswordReset, AdminId, urgentWaits), (EmailJobTypes.AdminInvite, AdminId, urgentWaits),
            (EmailJobTypes.StudentAccess, StudentId, othersWait), (EmailJobTypes.PasswordChanged, AdminId, othersWait),
        ];
        foreach (var (type, id, waits) in cases)
        {
            var (subjects, sender, counter) = (new Subjects(Admin, Student), new Sender(new(EmailSendStatus.Sent)), new Counter(sent));

            var result = await Handler(type, subjects, sender, limit, counter).HandleAsync(Job(type, id), Ct);

            Assert.Equal(limit == 0 ? [] : [Now.AddHours(-24)], counter.Asked);
            Assert.Equal(waits ? 0 : 1, sender.Sent.Count);
            (string, TimeSpan)? expected = waits ? ("daily_limit", new TimeSpan(0, 39, 30)) : null; // 12:20:30 até as 13:00
            Assert.Equal(expected, result is JobResult.Suspend s ? (s.ErrorCode, s.PauseFor) : null);
            Assert.True(!waits || subjects.Tokens.Count == 0, "o teto segura a mensagem antes de gerar o token");
        }
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("300", 300)]
    [InlineData("-1", null)]
    [InlineData("3,5", null)]
    [InlineData("trezentos", null)]
    public void Teto_diario_configurado_e_inteiro_maior_ou_igual_a_zero_e_vazio_vale_sem_teto(string value, int? limit)
    {
        Assert.Equal(limit, new EmailDailyLimitOptions { Value = value }.Limit);

        using var provider = Provider(value, new Subjects(), new Sender(new(EmailSendStatus.Sent)));
        using var scope = provider.CreateScope();
        if (limit is null)
        {
            // Com ValidateOnStart a aplicação nem sobe; a mensagem diz a variável e não repete o valor.
            var error = Assert.Throws<OptionsValidationException>(() => scope.ServiceProvider.GetServices<IJobHandler>().ToList());
            Assert.Contains("Email:DailyLimit", error.Message);
            Assert.DoesNotContain(value, error.Message);
        }
        else
        {
            Assert.Equal(2, scope.ServiceProvider.GetServices<IJobHandler>().Count());
        }
    }

    [Theory]
    [InlineData(EmailJobTypes.AdminInvite, LinkDeAcesso.ProvedorConvite, "72 horas")]
    [InlineData(EmailJobTypes.PasswordReset, LinkDeAcesso.ProvedorRedefinicao, "1 hora")]
    public async Task Convite_e_redefinicao_vao_para_o_email_da_conta_com_o_token_do_provedor_certo(string type, string provider, string validity)
    {
        var (result, subjects, sender) = await RunAsync(type, AdminId, known: Admin);

        var message = Assert.Single(sender.Sent);
        Assert.Equal("id-sintetico@example.org", Assert.IsType<JobResult.Succeeded>(result).ExternalId);
        Assert.Equal(("marina@example.com", type, JobId), (message.Recipient, message.Tag, message.IdempotencyKey));
        Assert.Equal([(AdminId, provider)], subjects.Tokens);
        Assert.Equal(RawToken, TokenIn(message.TextBody));
        Assert.Equal(RawToken, TokenIn(message.HtmlBody));
        Assert.Contains($"usuario={AdminId:D}&", message.TextBody);
        Assert.Contains(validity, message.TextBody);
    }

    [Theory]
    [InlineData(false, LinkDeAcesso.ProvedorConvite, "72 horas", "Acesso ao jogo Universo do Helinho")]
    [InlineData(true, LinkDeAcesso.ProvedorRedefinicao, "1 hora", "Troca de senha do jogo Universo do Helinho")]
    public async Task Acesso_do_aluno_vai_para_o_responsavel_e_o_link_e_o_texto_seguem_o_estado_da_conta(bool hasPassword, string provider, string validity, string title)
    {
        var (result, subjects, sender) = await RunAsync(EmailJobTypes.StudentAccess, StudentId, known: Student with { HasPassword = hasPassword });

        var message = Assert.Single(sender.Sent);
        Assert.IsType<JobResult.Succeeded>(result);
        Assert.Equal((GuardianEmail, title), (message.Recipient, message.Subject));
        Assert.Equal(hasPassword, message.TextBody.Contains("Esta é uma troca de senha.", StringComparison.Ordinal));
        Assert.Equal([(StudentUserId, provider)], subjects.Tokens);
        Assert.Equal(RawToken, TokenIn(message.TextBody));
        Assert.Contains($"usuario={StudentUserId:D}&", message.TextBody);
        Assert.All(["Ana", "2026-0042", validity], essential => Assert.Contains(essential, message.TextBody));
        Assert.DoesNotContain("anterior@example.org", message.TextBody + message.HtmlBody);
    }

    [Theory]
    [InlineData("conta", "marina@example.com", "marina.teste")]
    [InlineData("conta-do-aluno", GuardianEmail, "Ana")]
    [InlineData("id-do-aluno", GuardianEmail, "Ana")]
    public async Task Aviso_de_senha_alterada_vai_para_a_conta_ou_para_o_responsavel_sem_token(string subject, string recipient, string name)
    {
        var id = subject switch { "conta" => AdminId, "conta-do-aluno" => StudentUserId, _ => StudentId };

        var (result, subjects, sender) = await RunAsync(EmailJobTypes.PasswordChanged, id, known: [Admin, Student]);

        var message = Assert.Single(sender.Sent);
        Assert.IsType<JobResult.Succeeded>(result);
        Assert.Equal(recipient, message.Recipient);
        Assert.Contains($"A senha de acesso de {name} ao", message.TextBody);
        Assert.Empty(subjects.Tokens);
        Assert.DoesNotContain("token=", message.TextBody + message.HtmlBody);
    }

    [Theory]
    [InlineData(EmailJobTypes.AdminInvite, "sem-id", "subject_missing")]
    [InlineData(EmailJobTypes.AdminInvite, "conta-apagada", "subject_missing")]
    [InlineData(EmailJobTypes.StudentAccess, "conta", "subject_missing")] // o tipo pede o id do aluno, não o da conta
    [InlineData("email.desconhecido", "conta", "subject_missing")]
    [InlineData(EmailJobTypes.AdminInvite, "conta-com-senha", "not_eligible")]
    [InlineData(EmailJobTypes.AdminInvite, "conta-sem-unidade", "not_eligible")]
    [InlineData(EmailJobTypes.AdminInvite, "conta-do-aluno", "not_eligible")]
    [InlineData(EmailJobTypes.PasswordReset, "conta-do-aluno", "not_eligible")]
    [InlineData(EmailJobTypes.StudentAccess, "aluno-sem-aprovacao", "not_approved")]
    [InlineData(EmailJobTypes.StudentAccess, "aluno-sem-endereco", "recipient_missing")]
    [InlineData(EmailJobTypes.PasswordChanged, "aluno-sem-endereco", "recipient_missing")]
    [InlineData(EmailJobTypes.GuardianEmailChanged, "aluno-sem-endereco", "recipient_missing")]
    [InlineData(EmailJobTypes.PasswordReset, "conta-com-endereco-invalido", "recipient_invalid")]
    public async Task Condicao_que_nao_vale_mais_falha_com_codigo_fechado_sem_gerar_token_nem_enviar(string type, string scenario, string code)
    {
        Guid? id = scenario switch { "sem-id" => null, "conta-apagada" => Guid.NewGuid(), var s when s.StartsWith("aluno", StringComparison.Ordinal) => StudentId, "conta-do-aluno" => StudentUserId, _ => AdminId };
        var known = scenario switch
        {
            "conta-com-senha" => Admin with { HasPassword = true },
            "conta-sem-unidade" => Admin with { UnitName = null },
            "conta-com-endereco-invalido" => Admin with { Email = "Marina Teste <marina@example.com>\r\nBcc: outro@example.org" },
            "aluno-sem-aprovacao" => Student with { Student = Ana with { Approved = false } },
            "aluno-sem-endereco" => Student with { Student = Ana with { GuardianEmail = " ", PreviousGuardianEmail = null } },
            "conta-do-aluno" or "aluno" => Student,
            _ => Admin,
        };

        var (result, subjects, sender) = await RunAsync(type, id, known: known);

        Assert.Equal(code, Assert.IsType<JobResult.Fail>(result).ErrorCode);
        Assert.Empty(subjects.Tokens);
        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData(EmailSendStatus.Sent, null, null, "Succeeded", null)]
    [InlineData(EmailSendStatus.Sent, "duplicate_parameter", null, "Succeeded", null)]
    [InlineData(EmailSendStatus.PermanentFailure, "invalid_parameter", null, "Fail", "invalid_parameter")]
    [InlineData(EmailSendStatus.PermanentFailure, null, null, "Fail", "send_rejected")]
    [InlineData(EmailSendStatus.TransientFailure, "http_429", 90, "Retry", "http_429")]
    [InlineData(EmailSendStatus.TransientFailure, "timeout", null, "Retry", "timeout")]
    [InlineData(EmailSendStatus.TransientFailure, "network_extendedconnectnotsupported", null, "Retry", "send_failed")] // passa de 30 caracteres
    [InlineData(EmailSendStatus.TransientFailure, "Falha ao enviar para marina@example.com", null, "Retry", "send_failed")]
    [InlineData(EmailSendStatus.ProviderSuspended, "unauthorized", null, "Suspend", "unauthorized")]
    [InlineData(EmailSendStatus.ProviderSuspended, null, null, "Suspend", "provider_suspended")]
    public async Task Resposta_do_provedor_vira_resultado_da_fila(EmailSendStatus status, string? providerCode, int? retryAfterSeconds, string outcome, string? code)
    {
        var answer = new EmailSendResult(status, null, providerCode, retryAfterSeconds is { } s ? TimeSpan.FromSeconds(s) : null);

        var (result, _, sender) = await RunAsync(EmailJobTypes.PasswordReset, AdminId, answer, Admin);

        Assert.Single(sender.Sent);
        Assert.Equal(outcome, result.GetType().Name);
        Assert.Equal(code, result switch { JobResult.Fail f => f.ErrorCode, JobResult.Retry r => r.ErrorCode, JobResult.Suspend p => p.ErrorCode, _ => null });
        Assert.Equal(retryAfterSeconds is { } wait ? Now.AddSeconds(wait) : null, (result as JobResult.Retry)?.NotBefore);
        Assert.Equal(outcome == "Suspend" ? TimeSpan.FromMinutes(15) : null, (result as JobResult.Suspend)?.PauseFor);
        Assert.Null((result as JobResult.Succeeded)?.ExternalId);
    }

    [Fact]
    public async Task Sem_base_publica_os_dois_registros_sobem_sem_tipo_repetido_e_suspendem_sem_ler_a_conta_nem_enviar()
    {
        var (subjects, sender) = (new Subjects(Admin, Student), new Sender(new(EmailSendStatus.Sent)));
        await using var provider = Provider("300", subjects, sender);
        await using var scope = provider.CreateAsyncScope();

        var handlers = scope.ServiceProvider.GetServices<IJobHandler>().ToList(); // é assim que o trabalhador os resolve na subida
        var results = new[]
        {
            await handlers.Single(h => h.Types.Contains(EmailJobTypes.PasswordReset)).HandleAsync(Job(EmailJobTypes.PasswordReset, AdminId), Ct),
            await handlers.Single(h => h.Types.Contains(EmailJobTypes.StudentAccess)).HandleAsync(Job(EmailJobTypes.StudentAccess, StudentId), Ct),
        };

        Assert.Equal(2, handlers.Count);
        Assert.Equal(5, handlers.SelectMany(h => h.Types).Distinct().Count());
        Assert.All(results, result => Assert.Equal(
            ("base_url_missing", TimeSpan.FromMinutes(15)), Assert.IsType<JobResult.Suspend>(result) is var s ? (s.ErrorCode, s.PauseFor) : default));
        Assert.Empty(subjects.Tokens);
        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData("<id-sintetico@relay.example.org>", "id-sintetico@relay.example.org")] // como o webhook procura (§12)
    [InlineData(" id-sintetico@relay.example.org ", "id-sintetico@relay.example.org")]
    [InlineData("<>", null)]
    [InlineData("201x", null)] // maior que a coluna: não é guardado, e a gravação final não quebra
    public async Task Id_do_provedor_e_guardado_como_o_webhook_o_procura_e_nunca_maior_que_a_coluna(string providerId, string? stored)
    {
        var id = providerId == "201x" ? new string('x', 201) : providerId;

        var (result, _, _) = await RunAsync(EmailJobTypes.PasswordReset, AdminId, new(EmailSendStatus.Sent, id), Admin);

        Assert.Equal(stored, Assert.IsType<JobResult.Succeeded>(result).ExternalId);
        Assert.Equal(stored, EmailFeedback.NormalizeMessageId(id));
    }

    [Fact]
    public async Task Aviso_de_troca_vai_ao_endereco_anterior_com_a_data_de_Brasilia_sem_link_e_apaga_o_endereco()
    {
        var (subjects, sender) = (new Subjects(Student), new Sender(new(EmailSendStatus.Sent, "id-sintetico@example.org")));
        var trocaEm = new DateTimeOffset(2026, 11, 23, 2, 30, 0, TimeSpan.Zero); // 22/11 às 23h30 em Brasília

        var result = await Handler(EmailJobTypes.GuardianEmailChanged, subjects, sender)
            .HandleAsync(Job(EmailJobTypes.GuardianEmailChanged, StudentId, trocaEm), Ct);

        var message = Assert.Single(sender.Sent);
        Assert.IsType<JobResult.Succeeded>(result);
        Assert.Equal(("anterior@example.org", EmailJobTypes.GuardianEmailChanged), (message.Recipient, message.Tag));
        Assert.All([message.TextBody, message.HtmlBody], body => Assert.All(["Ana", "22/11/2026"], essential => Assert.Contains(essential, body)));
        Assert.DoesNotContain(GuardianEmail, message.TextBody + message.HtmlBody); // o endereço novo não vai no aviso
        Assert.DoesNotContain("token=", message.TextBody + message.HtmlBody);
        Assert.Empty(subjects.Tokens);
        Assert.Equal([StudentId], subjects.Cleared);
    }

    [Theory]
    [InlineData(EmailJobTypes.GuardianEmailChanged, "enviado", true)]
    [InlineData(EmailJobTypes.GuardianEmailChanged, "recusado", true)] // falha permanente: o aviso não sai mais
    [InlineData(EmailJobTypes.GuardianEmailChanged, "sem-endereco", true)]
    [InlineData(EmailJobTypes.GuardianEmailChanged, "abandonado", true)] // expired ou max_attempts (§6.7)
    [InlineData(EmailJobTypes.GuardianEmailChanged, "tentar-de-novo", false)]
    [InlineData(EmailJobTypes.GuardianEmailChanged, "suspenso", false)]
    [InlineData(EmailJobTypes.StudentAccess, "enviado", false)] // só o aviso de troca usa o endereço anterior
    [InlineData(EmailJobTypes.StudentAccess, "abandonado", false)]
    public async Task Endereco_anterior_do_responsavel_e_apagado_so_quando_o_aviso_de_troca_termina(string type, string scenario, bool cleared)
    {
        var student = scenario == "sem-endereco" ? Student with { Student = Ana with { PreviousGuardianEmail = null } } : Student;
        var status = scenario switch
        {
            "recusado" => EmailSendStatus.PermanentFailure,
            "tentar-de-novo" => EmailSendStatus.TransientFailure,
            "suspenso" => EmailSendStatus.ProviderSuspended,
            _ => EmailSendStatus.Sent,
        };
        var (subjects, sender) = (new Subjects(student), new Sender(new(status)));
        var (handler, job) = (Handler(type, subjects, sender), Job(type, StudentId));

        if (scenario == "abandonado")
        {
            await handler.OnAbandonedAsync(job, "expired", Ct);
        }
        else
        {
            var result = await handler.HandleAsync(job, Ct);
            Assert.Equal(scenario == "sem-endereco", (result as JobResult.Fail)?.ErrorCode == "recipient_missing");
        }

        Assert.Equal(cleared ? [StudentId] : [], subjects.Cleared);
        Assert.Equal(scenario is "sem-endereco" or "abandonado" ? 0 : 1, sender.Sent.Count);
    }
}
