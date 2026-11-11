using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Painel /Eventos: listagem, criação, término no passado rejeitado e fuso (UTC gravado, Brasília exibida).</summary>
[Collection(PostgresCollection.Name)]
public class PainelEventosTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().LiveEvents.ExecuteDeleteAsync(Ct);
        return factory;
    }

    private static async Task<HttpResponseMessage> PostEventAsync(HttpClient client, string title, string subtitle, string quizType, string endsAt)
    {
        var html = await client.GetStringAsync("/Eventos", Ct);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync("/Eventos", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Title"] = title,
            ["Input.Subtitle"] = subtitle,
            ["Input.QuizType"] = quizType,
            ["Input.EndsAt"] = endsAt,
        }), Ct);
    }

    [Fact]
    public async Task Sem_login_redireciona_para_entrar()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/Eventos", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Listagem_vazia_mostra_o_estado_vazio()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Eventos", Ct));

        Assert.Contains("Nenhum evento cadastrado.", html);
    }

    [Fact]
    public async Task Listagem_mostra_titulo_tipo_termino_em_Brasilia_e_situacao()
    {
        await using var factory = await NewFactoryAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LiveEvents.Add(new LiveEvent("Festival Ativo", "", QuizType.Esportivo, new DateTimeOffset(2090, 6, 15, 13, 0, 0, TimeSpan.Zero)));
            db.LiveEvents.Add(new LiveEvent("Festival Antigo", "", QuizType.Cultural, new DateTimeOffset(2020, 1, 10, 2, 30, 0, TimeSpan.Zero)));
            await db.SaveChangesAsync(Ct);
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());
        var html = await client.GetStringAsync("/Eventos", Ct);

        Assert.Contains("Festival Ativo", html);
        Assert.Contains("Esportivo", html);
        Assert.Contains("15/06/2090 10:00", html); // 13:00 UTC = 10:00 em Brasília
        Assert.Contains("Ativo</span>", html);
        Assert.Contains("Festival Antigo", html);
        Assert.Contains("09/01/2020 23:30", html); // atravessa a data ao converter
        Assert.Contains("Encerrado</span>", html);
        Assert.True(html.IndexOf("Festival Ativo", StringComparison.Ordinal) < html.IndexOf("Festival Antigo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cria_evento_gravando_UTC_a_partir_do_horario_de_Brasilia()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await PostEventAsync(client, "Semana do Planeta", "Missões em dobro", "Ambiental", "2090-06-15T10:00");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LiveEvents.SingleAsync(Ct);
        Assert.Equal("Semana do Planeta", saved.Title);
        Assert.Equal("Missões em dobro", saved.Subtitle);
        Assert.Equal(QuizType.Ambiental, saved.QuizType);
        Assert.Equal(new DateTimeOffset(2090, 6, 15, 13, 0, 0, TimeSpan.Zero), saved.EndsAt);
        Assert.Equal(TimeSpan.Zero, saved.EndsAt.Offset);
    }

    [Fact]
    public async Task Termino_no_passado_e_rejeitado_e_nada_e_gravado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await PostEventAsync(client, "Atrasado", "", "Cultural", "2020-01-01T10:00");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("O término deve estar no futuro.", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LiveEvents.AnyAsync(Ct));
    }

    [Fact]
    public async Task Titulo_e_tipo_sao_obrigatorios()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await PostEventAsync(client, "", "", "", "2090-06-15T10:00");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Informe o título.", html);
        Assert.Contains("Escolha o tipo de quiz.", html);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LiveEvents.AnyAsync(Ct));
    }

    [Fact]
    public async Task Post_sem_antiforgery_e_recusado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await client.PostAsync("/Eventos", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "x",
            ["Input.QuizType"] = "Cultural",
            ["Input.EndsAt"] = "2090-06-15T10:00",
        }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
