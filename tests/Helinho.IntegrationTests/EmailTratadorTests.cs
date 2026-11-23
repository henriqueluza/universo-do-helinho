using System.Text.Json;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Email;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Tratador de e-mail (#271 e #272, ADR-0003 item 8b) no host real: Identity, banco e provedores de token de verdade; só o
/// envio é um capturador. O escopo é o do trabalhador, sem usuário de painel. Contas e alunos sintéticos.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class EmailTratadorTests(PostgresFixture postgres, EmailTratadorTests.Host shared) : IClassFixture<EmailTratadorTests.Host>
{
    private const string Senha = "Nova-Sintetica-7";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um host para a classe inteira (#215); cada teste cria as próprias contas e acha a mensagem pelo id do trabalho.</summary>
    public sealed class Host : IAsyncDisposable, IEmailSender
    {
        private HelinhoWebFactory? _factory;

        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.FromResult(new EmailSendResult(EmailSendStatus.Sent, $"{message.IdempotencyKey}@example.org"));
        }

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres, configure: services => services
                    .AddSingleton<IEmailSender>(this)
                    .PostConfigure<EmailOptions>(o => o.PublicBaseUrl = "https://painel.example.org"));
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public ValueTask DisposeAsync() => _factory?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private async Task<T> ComAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider);
    }

    /// <summary>Conta e, com <paramref name="aluno"/>, o cadastro dela numa unidade nova, aprovado e com e-mail do responsável.</summary>
    private Task<(AppUser Conta, Guid StudentId, string Unidade)> CriarAsync(string papel, string? senha, bool aluno = false) => ComAsync(async services =>
    {
        var (users, db, tag) = (services.GetRequiredService<UserManager<AppUser>>(), services.GetRequiredService<AppDbContext>(), $"{Guid.NewGuid():N}"[..10]);
        var unit = new Unit($"Unidade {tag} (fictícia)");
        db.Units.Add(unit);
        await db.SaveChangesAsync(Ct);
        var user = new AppUser { UserName = $"t{tag}", Email = aluno ? null : $"t{tag}@example.com", UnitId = unit.Id };
        Assert.True((senha is null ? await users.CreateAsync(user) : await users.CreateAsync(user, senha)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, papel)).Succeeded);
        if (!aluno)
        {
            return (user, Guid.Empty, unit.Name);
        }

        var student = new Student(user.Id, unit.Id, $"M{tag}", "Ana Sintética Souza", $"rg{tag}", $"cpf{tag}", "Rua Fictícia, 1",
            new DateOnly(2016, 5, 5), "Responsável Sintético", "5A", new DateOnly(2026, 11, 22));
        student.SetGuardianEmail($"resp{tag}@example.com");
        student.ApproveByGuardian();
        db.Students.Add(student);
        await db.SaveChangesAsync(Ct);
        return (user, student.Id, unit.Name);
    });

    private Task<(JobResult Result, EmailMessage? Message)> TratarAsync(string type, Guid subjectId) => ComAsync(async services =>
    {
        var job = new JobContext(Guid.NewGuid(), type, subjectId, JsonDocument.Parse("""{"v":1}""").RootElement, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var result = await services.GetServices<IJobHandler>().Single(h => h.Types.Contains(type)).HandleAsync(job, Ct);
        return (result, shared.Sent.SingleOrDefault(m => m.IdempotencyKey == job.Id));
    });

    [GeneratedRegex(@"^https://painel\.example\.org/conta/definir-senha\?usuario=([0-9a-f-]{36})&token=([A-Za-z0-9_-]+)$", RegexOptions.Multiline)]
    private static partial Regex Link();

    [Fact]
    public async Task Host_tem_dois_registros_do_tratador_que_dividem_os_tipos_de_email_sem_repeticao()
    {
        var handlers = await ComAsync(services => Task.FromResult(services.GetServices<IJobHandler>().OfType<EmailJobHandler>().ToList()));
        var types = handlers.SelectMany(h => h.Types).Order().ToList();

        Assert.Equal(2, handlers.Count); // §15: urgentes e demais
        Assert.Equal(
            [EmailJobTypes.AdminInvite, EmailJobTypes.GuardianEmailChanged, EmailJobTypes.PasswordChanged, EmailJobTypes.PasswordReset, EmailJobTypes.StudentAccess],
            types);
    }

    [Fact]
    public async Task Teto_diario_conta_so_os_emails_enviados_dentro_da_janela()
    {
        // Datas longe das dos outros testes da classe, que dividem o banco.
        var now = new DateTimeOffset(2031, 1, 10, 12, 0, 0, TimeSpan.Zero);
        (string Type, string Status, int HoursAgo)[] rows =
        [
            (EmailJobTypes.StudentAccess, "Succeeded", 1), (EmailJobTypes.PasswordReset, "Succeeded", 23), // contam
            (EmailJobTypes.StudentAccess, "Succeeded", 25), (EmailJobTypes.AdminInvite, "Failed", 1),
            (EmailJobTypes.AdminInvite, "Pending", 0), ("ai.question-drafts", "Succeeded", 1),
        ];

        var sent = await ComAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            foreach (var (type, status, hoursAgo) in rows)
            {
                var job = new QueueJob(new JobRequest(type, Guid.NewGuid(), new JobPayload(), TimeSpan.FromHours(24)), now.AddHours(-hoursAgo));
                db.QueueJobs.Add(job);
                await db.SaveChangesAsync(Ct);
                DateTimeOffset? finished = status == "Pending" ? null : now.AddHours(-hoursAgo);
                await db.Database.ExecuteSqlAsync($"""UPDATE "QueueJobs" SET "Status" = {status}, "FinishedAt" = {finished} WHERE "Id" = {job.Id}""", Ct);
            }

            return await services.GetRequiredService<IEmailDailyCount>().SentSinceAsync(now.AddHours(-24), Ct);
        });

        Assert.Equal(2, sent);
    }

    [Fact]
    public async Task Convite_para_conta_sem_senha_leva_um_link_que_define_a_senha_e_depois_disso_nao_sai_de_novo()
    {
        var (conta, _, unidade) = await CriarAsync(RoleNames.Admin, senha: null);

        var (result, message) = await TratarAsync(EmailJobTypes.AdminInvite, conta.Id);

        Assert.StartsWith(message?.IdempotencyKey.ToString(), Assert.IsType<JobResult.Succeeded>(result).ExternalId);
        Assert.Equal((conta.Email, EmailJobTypes.AdminInvite), (message?.Recipient, message?.Tag));
        Assert.Contains(unidade, message?.TextBody);
        var link = Link().Match(message?.TextBody ?? "");
        Assert.Equal(conta.Id.ToString(), link.Groups[1].Value);
        var definida = await ComAsync(services =>
            services.GetRequiredService<DefinirSenhaHandler>().HandleAsync(link.Groups[1].Value, link.Groups[2].Value, Senha, Ct));
        Assert.Equal(DefinirSenhaStatus.Definida, definida.Status);

        // Conferido de novo na hora de enviar (§5.3): com senha, o convite de 72 horas não sai.
        var (again, none) = await TratarAsync(EmailJobTypes.AdminInvite, conta.Id);
        Assert.Equal(EmailJobHandler.NotEligible, Assert.IsType<JobResult.Fail>(again).ErrorCode);
        Assert.Null(none);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acesso_do_aluno_e_resolvido_so_pelo_id_do_trabalho_e_o_token_segue_o_estado_da_conta(bool comSenha)
    {
        // Aluno de uma unidade qualquer; o escopo não tem usuário de painel, como no trabalhador da fila.
        var (conta, studentId, _) = await CriarAsync(RoleNames.Student, comSenha ? Senha : null, aluno: true);

        var (result, message) = await TratarAsync(EmailJobTypes.StudentAccess, studentId);

        Assert.IsType<JobResult.Succeeded>(result);
        Assert.Equal($"resp{conta.UserName?[1..]}@example.com", message?.Recipient);
        Assert.All(["Ana", $"M{conta.UserName?[1..]}"], essential => Assert.Contains(essential, message?.TextBody));
        Assert.DoesNotContain("Souza", message?.TextBody + message?.HtmlBody); // §11.4: só o primeiro nome
        var token = LinkDeAcesso.LerToken(Link().Match(message?.TextBody ?? "").Groups[2].Value) ?? "";
        var (redefinicao, convite) = await ComAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(conta.Id.ToString()) ?? throw new InvalidOperationException("Conta de teste não encontrada.");
            return (await users.VerifyUserTokenAsync(user, LinkDeAcesso.ProvedorRedefinicao, LinkDeAcesso.Finalidade, token),
                await users.VerifyUserTokenAsync(user, LinkDeAcesso.ProvedorConvite, LinkDeAcesso.Finalidade, token));
        });
        // §11.4: com senha, o token só vale no provedor de 1 hora; sem senha, só no de 72 horas.
        Assert.Equal((comSenha, !comSenha), (redefinicao, convite));
    }

    [Fact]
    public async Task Aviso_de_senha_alterada_do_aluno_vai_para_o_responsavel_e_quem_nao_existe_mais_falha_sem_envio()
    {
        var (conta, _, _) = await CriarAsync(RoleNames.Student, Senha, aluno: true);

        var (changed, message) = await TratarAsync(EmailJobTypes.PasswordChanged, conta.Id);
        var (noAccount, noInvite) = await TratarAsync(EmailJobTypes.AdminInvite, Guid.NewGuid());
        var (noStudent, noAccess) = await TratarAsync(EmailJobTypes.StudentAccess, Guid.NewGuid());

        Assert.IsType<JobResult.Succeeded>(changed);
        Assert.Equal($"resp{conta.UserName?[1..]}@example.com", message?.Recipient);
        Assert.Contains("A senha de acesso de Ana ao", message?.TextBody);
        Assert.Equal(EmailJobHandler.SubjectMissing, Assert.IsType<JobResult.Fail>(noAccount).ErrorCode);
        Assert.Equal(EmailJobHandler.SubjectMissing, Assert.IsType<JobResult.Fail>(noStudent).ErrorCode);
        Assert.Null(noInvite ?? noAccess);
    }
}
