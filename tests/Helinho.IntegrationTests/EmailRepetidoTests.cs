using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Helinho.Application.Administradores;
using Helinho.Application.Email;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// #184: duas contas com o mesmo e-mail normalizado. O índice <c>EmailIndex</c> não é único e o Identity roda com
/// <c>RequireUniqueEmail = false</c>, então a duplicata pode existir no banco. Contas sintéticas.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EmailRepetidoTests(PostgresFixture postgres, EmailRepetidoTests.Host shared) : IClassFixture<EmailRepetidoTests.Host>
{
    private const string Senha = "Senha-Sintetica-1";
    private const string Invalido = "Usuário ou senha inválidos.";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um host para a classe inteira (#215); cada teste cria as próprias contas, com e-mail próprio.</summary>
    public sealed class Host : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres);
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public ValueTask DisposeAsync() => _factory?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private static string NovoEmail() => $"dup{Guid.NewGuid():N}"[..14] + "@example.com";

    /// <summary>Cria a conta direto no Identity, como já pode existir no banco. Devolve o nome de usuário.</summary>
    private async Task<string> CriarContaAsync(string email, string role = RoleNames.Admin)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = $"t{Guid.NewGuid():N}"[..12], Email = email };
        Assert.True((await users.CreateAsync(user, Senha)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.UserName;
    }

    /// <summary>Duas contas de painel cujo e-mail só difere em maiúsculas (mesmo <c>NormalizedEmail</c>).</summary>
    private async Task<(string Email, string Usuario)> CriarDuplicataAsync()
    {
        var email = NovoEmail();
        var usuario = await CriarContaAsync(email);
        await CriarContaAsync(email.ToUpperInvariant());
        return (email, usuario);
    }

    /// <summary>Há pedido de redefinição na fila para alguma conta com este e-mail? Quem envia é a fila (ADR-0003 §11.1).</summary>
    private async Task<bool> EnfileirouParaAsync(string email)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalizado = email.ToUpperInvariant();
        return await db.QueueJobs.AnyAsync(
            j => j.Type == EmailJobTypes.PasswordReset && db.Users.Any(u => u.Id == j.SubjectId && u.NormalizedEmail == normalizado), Ct);
    }

    private async Task<Guid> CriarUnidadeAsync()
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = new Unit($"Unidade {Guid.NewGuid():N}"[..20]);
        db.Units.Add(unit);
        await db.SaveChangesAsync(Ct);
        return unit.Id;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await (await client.GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct);
        fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    private static async Task<string> TextoAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

    [Fact]
    public async Task Login_do_painel_com_e_mail_repetido_responde_credencial_invalida_e_nao_500()
    {
        var (email, usuario) = await CriarDuplicataAsync();
        using var client = (await shared.GetAsync(postgres)).CreateAnonymousClient();

        var porEmail = await PostFormAsync(client, "/entrar", new() { ["Input.UserName"] = email, ["Input.Password"] = Senha });
        var porUsuario = await PostFormAsync(client, "/entrar", new() { ["Input.UserName"] = usuario, ["Input.Password"] = Senha });

        Assert.Equal(HttpStatusCode.OK, porEmail.StatusCode);
        Assert.Contains(Invalido, await TextoAsync(porEmail));
        Assert.False(porEmail.Headers.Contains("Set-Cookie"));
        // A conta continua acessível pelo nome de usuário, que é único.
        Assert.Equal(HttpStatusCode.Redirect, porUsuario.StatusCode);
    }

    [Fact]
    public async Task Login_da_API_com_e_mail_repetido_responde_o_mesmo_401_de_credencial_invalida()
    {
        var (email, _) = await CriarDuplicataAsync();
        using var client = (await shared.GetAsync(postgres)).CreateClient();

        var repetido = await client.PostAsJsonAsync("/api/login", new { login = email, password = Senha }, Ct);
        var inexistente = await client.PostAsJsonAsync("/api/login", new { login = NovoEmail(), password = Senha }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
        Assert.Equal(await inexistente.Content.ReadAsStringAsync(Ct), await repetido.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Recuperar_senha_com_e_mail_repetido_da_a_resposta_neutra_e_nao_envia_nada()
    {
        var (email, _) = await CriarDuplicataAsync();
        var unico = NovoEmail();
        await CriarContaAsync(unico);
        var factory = await shared.GetAsync(postgres);
        using var client = factory.CreateAnonymousClient();

        var repetido = await PostFormAsync(client, "/recuperar-senha", new() { ["Input.Email"] = email });
        var inexistente = await PostFormAsync(client, "/recuperar-senha", new() { ["Input.Email"] = NovoEmail() });
        var comConta = await PostFormAsync(client, "/recuperar-senha", new() { ["Input.Email"] = unico });

        Assert.Equal(HttpStatusCode.OK, repetido.StatusCode);
        Assert.Equal(inexistente.StatusCode, repetido.StatusCode);
        Assert.Contains("Se o e-mail estiver cadastrado", await TextoAsync(repetido));
        Assert.Contains("Se o e-mail estiver cadastrado", await TextoAsync(inexistente));
        Assert.False(await EnfileirouParaAsync(email));
        Assert.Equal(HttpStatusCode.OK, comConta.StatusCode);
        Assert.True(await EnfileirouParaAsync(unico)); // a conta de e-mail único continua recebendo
    }

    [Theory]
    [InlineData(RoleNames.Master)]
    [InlineData(RoleNames.Admin)]
    public async Task Cadastro_de_administrador_recusa_e_mail_ja_usado_por_conta_do_painel(string papelExistente)
    {
        var email = NovoEmail();
        await CriarContaAsync(email, papelExistente);
        var factory = await shared.GetAsync(postgres);
        var unitId = await CriarUnidadeAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Master);
        var novo = $"adm{Guid.NewGuid():N}"[..12];

        var response = await PostFormAsync(client, "/Administradores/Novo", new()
        {
            ["Input.UserName"] = novo,
            ["Input.Email"] = email.ToUpperInvariant(), // só muda a caixa: é o mesmo endereço
            ["Input.UnitId"] = unitId.ToString(),
            ["Input.Password"] = "Segredo-Inicial9x",
        });

        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("id=\"Input_Email-erro\"[^>]*>[^<]*J(á|&#xE1;) existe um usu(á|&#xE1;)rio com este e-mail\\.", html);
        var normalizado = email.ToUpperInvariant();
        await using var check = factory.Services.CreateAsyncScope();
        var contas = check.ServiceProvider.GetRequiredService<AppDbContext>().Users;
        Assert.Equal(1, await contas.CountAsync(u => u.NormalizedEmail == normalizado, Ct));
        Assert.False(await contas.AnyAsync(u => u.UserName == novo, Ct));
    }

    [Fact]
    public async Task Dois_cadastros_paralelos_com_o_mesmo_e_mail_em_unidades_diferentes_nao_passam_juntos()
    {
        var factory = await shared.GetAsync(postgres);
        var (email, unidadeA, unidadeB) = (NovoEmail(), await CriarUnidadeAsync(), await CriarUnidadeAsync());
        await using var escopoA = factory.Services.CreateAsyncScope();
        await using var escopoB = factory.Services.CreateAsyncScope();
        var primeiro = escopoA.ServiceProvider.GetRequiredService<IAdminAccountGateway>();
        var segundo = escopoB.ServiceProvider.GetRequiredService<IAdminAccountGateway>();

        await using var lockA = await primeiro.BeginUnitLockAsync(unidadeA, Ct);
        Assert.False(await primeiro.EmailInUseAsync(email, Ct));
        await using var lockB = await segundo.BeginUnitLockAsync(unidadeB, Ct);
        var checagem = segundo.EmailInUseAsync(email.ToUpperInvariant(), Ct);

        // O segundo cadastro espera o primeiro terminar; sem o lock do e-mail, responderia "livre" na hora.
        Assert.NotSame(checagem, await Task.WhenAny(checagem, Task.Delay(TimeSpan.FromMilliseconds(500), Ct)));
        var nome = $"adm{Guid.NewGuid():N}"[..12];
        Assert.Empty(await primeiro.CreateAdminAsync(new NewAdminAccount(nome, email, unidadeA), Ct));
        await lockA.CommitAsync(Ct);
        Assert.True(await checagem);
    }
}
