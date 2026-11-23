using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// E-mail do responsável nas telas e na API (#235, ADR-0003 §9): só no formulário do aluno; fora da lista, do desempenho,
/// da API do jogo e do login. Dados do seed sintético (<c>responsavelNN@example.org</c>).
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class GuardianEmailTests(PostgresFixture postgres, GuardianEmailTests.SharedHost shared)
    : IClassFixture<GuardianEmailTests.SharedHost>
{
    private const string Password = "Senha-Sintetica-1";
    private const string SeedEmail = "responsavel01@example.org";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<input[^>]*id=\"Input_GuardianEmail\"[^>]*>")]
    private static partial Regex EmailInput();

    /// <summary>Um host para a classe inteira (#215): os testes só leem o seed; o POST inválido não grava.</summary>
    public sealed class SharedHost : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres, Password);
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public async ValueTask DisposeAsync()
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }
        }
    }

    /// <summary>O aluno 01 do seed: aprovado, com senha e com e-mail do responsável.</summary>
    private async Task<(HelinhoWebFactory Factory, Guid StudentId, Guid UnitId)> WorldAsync()
    {
        var factory = await shared.GetAsync(postgres);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var student = await db.Students.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Enrollment == "2026001", Ct);
        Assert.Equal(SeedEmail, student.GuardianEmail);
        return (factory, student.Id, student.UnitId);
    }

    [Fact]
    public async Task Formulario_tem_o_campo_obrigatorio_e_a_edicao_mostra_o_email_gravado()
    {
        var (factory, studentId, unitId) = await WorldAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);

        var novo = await client.GetStringAsync("/Alunos/Novo", Ct);
        var editar = await client.GetStringAsync($"/Alunos/Editar/{studentId}", Ct);

        var input = EmailInput().Match(novo).Value;
        Assert.Contains("name=\"Input.GuardianEmail\"", input, StringComparison.Ordinal);
        Assert.Contains("type=\"email\"", input, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"off\"", input, StringComparison.Ordinal);
        Assert.Contains(" required", input, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-invalid", input, StringComparison.Ordinal);
        Assert.Contains("<label for=\"Input_GuardianEmail\">E-mail do respons&#xE1;vel</label>", novo, StringComparison.Ordinal);
        Assert.Contains($"value=\"{SeedEmail}\"", EmailInput().Match(editar).Value, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviousGuardianEmail", novo + editar, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_invalido_entra_no_resumo_de_erros_e_marca_o_campo()
    {
        var (factory, _, unitId) = await WorldAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin, unitId);
        var page = await client.GetStringAsync("/Alunos/Novo", Ct);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync("/Alunos/Novo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.GuardianEmail"] = "nao-e-email",
        }), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("role=\"alert\" tabindex=\"-1\" id=\"resumo-erros\"", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#Input_GuardianEmail\">E-mail do respons&#xE1;vel: E-mail inv&#xE1;lido. Confira o endere&#xE7;o.</a>", html, StringComparison.Ordinal);
        var input = EmailInput().Match(html).Value;
        Assert.Contains("aria-invalid=\"true\"", input, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"Input_GuardianEmail-erro\"", input, StringComparison.Ordinal);
        Assert.Contains("<small id=\"Input_GuardianEmail-erro\" class=\"field-error\">E-mail inv&#xE1;lido. Confira o endere&#xE7;o.</small>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lista_de_alunos_e_desempenho_nao_mostram_o_email_do_responsavel()
    {
        var (factory, studentId, unitId) = await WorldAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin, unitId);

        foreach (var url in new[] { "/Alunos", "/Desempenho", $"/Desempenho/Aluno/{studentId}" })
        {
            var response = await client.GetAsync(url, Ct);
            var html = await response.Content.ReadAsStringAsync(Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Aluno Fict", html, StringComparison.Ordinal); // a página traz mesmo os alunos do seed
            Assert.DoesNotContain("@example.org", html, StringComparison.Ordinal);
            Assert.DoesNotContain("E-mail", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task API_do_jogo_nao_aceita_o_email_do_responsavel_como_login_nem_o_devolve()
    {
        var (factory, _, _) = await WorldAsync();
        using var client = factory.CreateClient();

        var byGuardian = await client.PostAsJsonAsync("/api/login", new { login = SeedEmail, password = Password }, Ct);
        var byStudent = await client.PostAsJsonAsync("/api/login", new { login = "aluno01@helinho.example", password = Password }, Ct);

        // Controle: a mesma senha entra com o e-mail do próprio aluno; a recusa é do identificador.
        Assert.Equal(HttpStatusCode.Unauthorized, byGuardian.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byStudent.StatusCode);
        var login = await byStudent.Content.ReadAsStringAsync(Ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JsonDocument.Parse(login).RootElement.GetProperty("token").GetString());
        var bodies = new List<string> { login };
        foreach (var url in new[] { "/api/profile", "/api/user/stats", "/api/user/badges?page=1", "/api/user/inventory", "/api/quizzes/progress", "/api/quests/daily" })
        {
            var response = await client.GetAsync(url, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync(Ct));
        }

        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain("example.org", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("guardian", body, StringComparison.OrdinalIgnoreCase);
        });
    }
}
