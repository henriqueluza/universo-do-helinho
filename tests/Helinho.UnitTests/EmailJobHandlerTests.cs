using System.Text.Json;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.UnitTests;

/// <summary>Tratador de e-mail (#270, ADR-0003 §4.2, §5.3 e §11) contra a porta falsa e um envio que só captura. Dados sintéticos.</summary>
public class EmailJobHandlerTests
{
    private const string RawToken = "CfDJ8+token/sintetico=="; // com os caracteres que o Base64 comum tem e a URL não aceita
    private const string GuardianEmail = "responsavel@example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
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

        public Task<EmailSubject?> FindByUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(known.FirstOrDefault(s => s.UserId == userId));

        public Task<EmailSubject?> FindByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
            Task.FromResult(studentId == StudentId ? known.FirstOrDefault(s => s.Student is not null) : null);

        public Task<string> GenerateLinkTokenAsync(Guid userId, string provider, CancellationToken cancellationToken)
        {
            Tokens.Add((userId, provider));
            return Task.FromResult(RawToken);
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

    private static async Task<(JobResult Result, Subjects Subjects, Sender Sender)> RunAsync(
        string type, Guid? subjectId, EmailSendResult? answer = null, params EmailSubject[] known)
    {
        var subjects = new Subjects(known);
        var sender = new Sender(answer ?? new(EmailSendStatus.Sent, "id-sintetico@example.org"));
        var handler = new EmailJobHandler(subjects, EmailTemplateRendererTests.Renderer, sender, new FixedClock());
        return (await handler.HandleAsync(Job(type, subjectId), Ct), subjects, sender);
    }

    private static JobContext Job(string type, Guid? subjectId) =>
        new(JobId, type, subjectId, JsonDocument.Parse("""{"v":1}""").RootElement, 1, Now, Now.AddHours(24));

    /// <summary>O token como a página de definir senha o lê do link (§10.3).</summary>
    private static string? TokenIn(string body) => LinkDeAcesso.LerToken(Regex.Match(body, "token=([A-Za-z0-9_-]+)").Groups[1].Value);

    [Fact]
    public void Declara_os_cinco_tipos_e_a_politica_de_email_do_ADR()
    {
        var handler = new EmailJobHandler(new Subjects(), EmailTemplateRendererTests.Renderer, new Sender(new(EmailSendStatus.Sent)), new FixedClock());

        Assert.Equal(
            ["email.admin-invite", "email.guardian-email-changed", "email.password-changed", "email.password-reset", "email.student-access"],
            handler.Types.Order());
        Assert.Equal(
            new JobPolicy(8, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), 10, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2)), handler.Policy);
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
    [InlineData(false, LinkDeAcesso.ProvedorConvite, "72 horas")]
    [InlineData(true, LinkDeAcesso.ProvedorRedefinicao, "1 hora")]
    public async Task Acesso_do_aluno_vai_para_o_responsavel_e_o_link_segue_o_estado_da_conta(bool hasPassword, string provider, string validity)
    {
        var (result, subjects, sender) = await RunAsync(EmailJobTypes.StudentAccess, StudentId, known: Student with { HasPassword = hasPassword });

        var message = Assert.Single(sender.Sent);
        Assert.IsType<JobResult.Succeeded>(result);
        Assert.Equal(GuardianEmail, message.Recipient);
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
    [InlineData(EmailJobTypes.GuardianEmailChanged, "aluno", "template_missing")] // o quinto modelo é da Issue 11 do plano
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
    public async Task Sem_base_publica_o_registro_nao_lanca_e_o_tratador_suspende_sem_ler_a_conta_nem_enviar()
    {
        var (subjects, sender) = (new Subjects(Admin), new Sender(new(EmailSendStatus.Sent)));
        await using var provider = new ServiceCollection()
            .AddEmailTemplates(_ => new(PublicBaseUrl.Parse("", allowHttpLoopback: false), "FUNDHAS", "contato@example.org"))
            .AddEmailJobs()
            .AddScoped<IEmailSubjects>(_ => subjects).AddSingleton<IEmailSender>(sender).AddSingleton<IClock>(new FixedClock())
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var handler = Assert.Single(scope.ServiceProvider.GetServices<IJobHandler>()); // é assim que o trabalhador os resolve na subida
        var result = await handler.HandleAsync(Job(EmailJobTypes.PasswordReset, AdminId), Ct);

        var suspended = Assert.IsType<JobResult.Suspend>(result);
        Assert.Equal(("base_url_missing", TimeSpan.FromMinutes(15)), (suspended.ErrorCode, suspended.PauseFor));
        Assert.Empty(subjects.Tokens);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Id_do_provedor_maior_que_a_coluna_nao_e_guardado()
    {
        var (result, _, _) = await RunAsync(EmailJobTypes.PasswordReset, AdminId, new(EmailSendStatus.Sent, new string('x', 201)), Admin);

        Assert.Null(Assert.IsType<JobResult.Succeeded>(result).ExternalId);
    }
}
