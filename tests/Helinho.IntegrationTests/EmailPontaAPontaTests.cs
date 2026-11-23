using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Fluxos de e-mail de ponta a ponta (#293, item 16 do plano do ADR-0003): páginas reais com o cookie real do painel,
/// processador real da fila e tratador real; só o envio é um capturador. O link sai do e-mail capturado, como o
/// destinatário o recebe. Contas, alunos e endereços sintéticos.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class EmailPontaAPontaTests(PostgresFixture postgres, EmailPontaAPontaTests.Host shared) : IClassFixture<EmailPontaAPontaTests.Host>
{
    private const string SenhaAntiga = "Senha-Sintetica-1";
    private const string SenhaNova = "Nova-Sintetica-7";
    private const string LinkInvalido = "Este link não vale mais";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Um host para a classe inteira (#215). O mesmo relógio serve de <see cref="IClock"/> (casos de uso) e de
    /// <see cref="TimeProvider"/> (fila e processador): <see cref="Avancar"/> adianta os dois juntos. É a hora real mais
    /// um adiantamento, com temporizadores de verdade: o piso de tempo do pedido de redefinição espera num deles.
    /// </summary>
    public sealed class Host : IAsyncDisposable, IClock
    {
        private readonly Relogio _time = new();
        private HelinhoWebFactory? _factory;

        public EmailCapturado Email { get; } = new();

        public DateTimeOffset UtcNow => _time.GetUtcNow();

        public void Avancar(TimeSpan quanto) => _time.Adiantado += quanto;

        private sealed class Relogio : TimeProvider
        {
            public TimeSpan Adiantado { get; set; }

            public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Adiantado;
        }

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres, clock: this, realCookieAuth: true, configure: services =>
                {
                    Email.Registrar(services);
                    services.AddSingleton<TimeProvider>(_time);
                });
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public ValueTask DisposeAsync() => _factory?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    [GeneratedRegex(@"^https://painel\.example\.org(/conta/definir-senha\?usuario=[0-9a-f-]{36}&token=[A-Za-z0-9_-]+)$", RegexOptions.Multiline)]
    private static partial Regex LinkNoTexto();

    [GeneratedRegex("name=\"(__RequestVerificationToken|Usuario|Token)\"[^>]*value=\"([^\"]*)\"")]
    private static partial Regex CampoOculto();

    private static string Tag() => $"{Guid.NewGuid():N}"[..10];

    private async Task<T> ComAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider);
    }

    /// <summary>Conta do painel com senha, numa unidade nova quando é administrador.</summary>
    private Task<AppUser> ContaAsync(string papel) => ComAsync(async services =>
    {
        var (users, db, tag) = (services.GetRequiredService<UserManager<AppUser>>(), services.GetRequiredService<AppDbContext>(), Tag());
        var unit = new Unit($"Unidade {tag} (fictícia)");
        db.Units.Add(unit);
        await db.SaveChangesAsync(Ct);
        var user = new AppUser { UserName = $"p{tag}", Email = $"p{tag}@example.com", UnitId = papel == RoleNames.Admin ? unit.Id : null };
        Assert.True((await users.CreateAsync(user, SenhaAntiga)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, papel)).Succeeded);
        return user;
    });

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string paginaDoToken, Dictionary<string, string> campos)
    {
        var pagina = await client.GetStringAsync(paginaDoToken, Ct);
        campos["__RequestVerificationToken"] = CampoOculto().Matches(pagina).First(m => m.Groups[1].Value == "__RequestVerificationToken").Groups[2].Value;
        return await client.PostAsync(url, new FormUrlEncodedContent(campos), Ct);
    }

    /// <summary>Login pela página real; o cliente devolvido guarda o cookie do painel.</summary>
    private async Task<(HttpClient Client, HttpResponseMessage Resposta)> EntrarAsync(string usuario, string senha)
    {
        var client = (await shared.GetAsync(postgres)).CreateAnonymousClient();
        return (client, await PostFormAsync(client, "/entrar", "/entrar", new() { ["Input.UserName"] = usuario, ["Input.Password"] = senha }));
    }

    private async Task<HttpClient> PainelAsync(AppUser conta)
    {
        var (client, resposta) = await EntrarAsync(conta.UserName!, SenhaAntiga);
        Assert.Equal(HttpStatusCode.Redirect, resposta.StatusCode);
        return client;
    }

    /// <summary>Uma passada do trabalhador e os e-mails daquele tipo que já saíram para o endereço.</summary>
    private async Task<List<EmailMessage>> EmailsAsync(string destinatario, string tipo)
    {
        await EmailCapturado.ProcessarFilaAsync((await shared.GetAsync(postgres)).Services, Ct);
        return [.. shared.Email.Enviados.Where(m => m.Tag == tipo && string.Equals(m.Recipient, destinatario, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>O caminho do link, tirado do texto do e-mail; o HTML leva o mesmo destino no botão.</summary>
    private static string Link(EmailMessage email)
    {
        var link = Assert.Single(LinkNoTexto().Matches(email.TextBody)).Groups[1].Value;
        Assert.Contains($"href=\"{WebUtility.HtmlEncode(EmailCapturado.BaseUrl + link)}\"", email.HtmlBody, StringComparison.Ordinal);
        return link;
    }

    /// <summary>Abre o link como o destinatário e define a senha pelo formulário da página.</summary>
    private async Task DefinirSenhaAsync(string link, string senha)
    {
        using var client = (await shared.GetAsync(postgres)).CreateAnonymousClient();
        var formulario = await client.GetStringAsync(link, Ct);
        var campos = CampoOculto().Matches(formulario).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        campos["Input.Password"] = campos["Input.Confirmation"] = senha;

        using var resposta = await client.PostAsync("/conta/definir-senha", new FormUrlEncodedContent(campos), Ct);

        Assert.Equal((HttpStatusCode.Redirect, "/conta/definir-senha?definida=True"), (resposta.StatusCode, resposta.Headers.Location?.OriginalString));
    }

    private async Task<string> AbrirAsync(string link)
    {
        using var client = (await shared.GetAsync(postgres)).CreateAnonymousClient();
        return WebUtility.HtmlDecode(await client.GetStringAsync(link, Ct));
    }

    private static Dictionary<string, string> Aluno(string nome, string responsavel, bool aprovado)
    {
        Dictionary<string, string> campos = new()
        {
            ["Input.FullName"] = nome,
            ["Input.Rg"] = "33.333.333-3",
            ["Input.Cpf"] = "529.982.247-25",
            ["Input.Address"] = "Rua Sintética, 3",
            ["Input.BirthDate"] = "2017-03-01",
            ["Input.ClassName"] = "5A",
            ["Input.GuardianName"] = "Responsável Fictício",
            ["Input.GuardianEmail"] = responsavel,
        };
        if (aprovado)
        {
            campos["Input.GuardianApproved"] = "true";
        }

        return campos;
    }

    /// <summary>Cadastra o aluno pelo formulário do painel e devolve o que o banco guardou dele.</summary>
    private async Task<(Guid Id, string Matricula)> CadastrarAlunoAsync(HttpClient painel, string nome, string responsavel, bool aprovado)
    {
        using var resposta = await PostFormAsync(painel, "/Alunos/Novo", "/Alunos/Novo", Aluno(nome, responsavel, aprovado));
        Assert.Equal(HttpStatusCode.Redirect, resposta.StatusCode);
        // ignora-filtro: a conferência do teste roda sem usuário de painel.
        return await ComAsync(async services => await services.GetRequiredService<AppDbContext>().Students.IgnoreQueryFilters()
            .Where(s => s.FullName == nome).Select(s => new ValueTuple<Guid, string>(s.Id, s.Enrollment)).SingleAsync(Ct));
    }

    /// <summary>Login do jogo, no contrato de sempre: <c>token</c>, <c>expires_at</c> e <c>user_id</c>.</summary>
    private async Task EntrarNoJogoAsync(string matricula, string senha, Guid studentId)
    {
        using var client = (await shared.GetAsync(postgres)).CreateClient();
        using var resposta = await client.PostAsJsonAsync("/api/login", new { login = matricula, password = senha }, Ct);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(["token", "expires_at", "user_id"], corpo.EnumerateObject().Select(p => p.Name));
        Assert.Equal(studentId.ToString(), corpo.GetProperty("user_id").GetString());
    }

    [Fact]
    public async Task Acesso_do_aluno_aprovar_envia_o_link_ao_responsavel_e_a_senha_definida_entra_no_jogo()
    {
        using var painel = await PainelAsync(await ContaAsync(RoleNames.Admin));
        var (nome, responsavel) = ($"Ana Ponta {Tag()}", $"resp{Tag()}@example.com");

        var (id, matricula) = await CadastrarAlunoAsync(painel, nome, responsavel, aprovado: false);
        var antesDeAprovar = await EmailsAsync(responsavel, EmailJobTypes.StudentAccess);
        using var aprovado = await PostFormAsync(painel, $"/Alunos/Editar/{id}", $"/Alunos/Editar/{id}", Aluno(nome, responsavel, aprovado: true));
        var acesso = Assert.Single(await EmailsAsync(responsavel, EmailJobTypes.StudentAccess));
        await DefinirSenhaAsync(Link(acesso), SenhaNova);

        Assert.Empty(antesDeAprovar); // sem aprovação nada sai
        Assert.Equal(HttpStatusCode.Redirect, aprovado.StatusCode);
        Assert.All(["Ana", matricula, "72 horas"], essencial => Assert.Contains(essencial, acesso.TextBody, StringComparison.Ordinal));
        await EntrarNoJogoAsync(matricula, SenhaNova, id);
    }

    [Fact]
    public async Task Troca_do_email_do_responsavel_avisa_o_endereco_anterior_apaga_o_e_mata_o_link_ja_enviado()
    {
        using var painel = await PainelAsync(await ContaAsync(RoleNames.Admin));
        var (nome, antes, depois) = ($"Dora Ponta {Tag()}", $"antes{Tag()}@example.com", $"depois{Tag()}@example.com");
        var (id, matricula) = await CadastrarAlunoAsync(painel, nome, antes, aprovado: true);
        var ficha = $"/Alunos/Editar/{id}";
        // ignora-filtro: a conferência do teste roda sem usuário de painel.
        Task<string?> AnteriorAsync() => ComAsync(services => services.GetRequiredService<AppDbContext>().Students.IgnoreQueryFilters()
            .Where(s => s.Id == id).Select(s => s.PreviousGuardianEmail).SingleAsync(Ct));
        var linkAntigo = Link(Assert.Single(await EmailsAsync(antes, EmailJobTypes.StudentAccess)));

        using var trocado = await PostFormAsync(painel, ficha, ficha, Aluno(nome, depois, aprovado: true));
        var guardado = await AnteriorAsync();
        var aviso = Assert.Single(await EmailsAsync(antes, EmailJobTypes.GuardianEmailChanged));
        var apagado = await AnteriorAsync();
        var paginaDoLinkAntigo = await AbrirAsync(linkAntigo);

        // Outra janela de idempotência: sem adiantar o relógio, o reenvio cairia na chave do acesso do cadastro.
        shared.Avancar(TimeSpan.FromMinutes(3));
        using var reenviado = await PostFormAsync(painel, $"{ficha}?handler=EnviarAcesso", ficha, []);
        var acessoNovo = Assert.Single(await EmailsAsync(depois, EmailJobTypes.StudentAccess));
        await DefinirSenhaAsync(Link(acessoNovo), SenhaNova);

        Assert.Equal(HttpStatusCode.Redirect, trocado.StatusCode);
        Assert.Equal((antes, null), (guardado, apagado));
        Assert.DoesNotContain(depois, aviso.TextBody + aviso.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", aviso.TextBody + aviso.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(LinkInvalido, paginaDoLinkAntigo, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, reenviado.StatusCode);
        Assert.Single(await EmailsAsync(antes, EmailJobTypes.StudentAccess)); // nada mais vai ao endereço anterior
        await EntrarNoJogoAsync(matricula, SenhaNova, id);
    }
}
