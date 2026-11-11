using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Painel /Questoes: criação como rascunho, validação, aprovação e edição que volta a rascunho.</summary>
[Collection(PostgresCollection.Name)]
public class PainelQuestoesTests(PostgresFixture postgres)
{
    private const string Marker = "Questão do painel ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.QuizAnswers.ExecuteDeleteAsync(Ct);
        await db.Questions.ExecuteDeleteAsync(Ct);
        return factory;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string formUrl, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(formUrl, Ct);
        fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields), Ct);
    }

    private static Dictionary<string, string> Form(string statement, string[] options, string correct) => new()
    {
        ["Input.QuizType"] = "Ambiental",
        ["Input.AgeBand"] = "Age8To10",
        ["Input.Statement"] = statement,
        ["Input.OptionA"] = options[0],
        ["Input.OptionB"] = options[1],
        ["Input.OptionC"] = options[2],
        ["Input.OptionD"] = options[3],
        ["Input.Correct"] = correct,
        ["Input.Explanation"] = "Porque sim.",
    };

    private static readonly string[] FourOptions = ["Um", "Dois", "Três", "Quatro"];

    private static async Task<Guid> SeedAsync(HelinhoWebFactory factory, bool approved)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var q = new Question(QuizType.Cultural, AgeBand.Age6To7, Marker + "semente", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]);
        if (approved)
        {
            q.Approve();
        }

        db.Questions.Add(q);
        await db.SaveChangesAsync(Ct);
        return q.Id;
    }

    [Fact]
    public async Task Sem_login_redireciona_para_entrar()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Questoes", Ct)).StatusCode);
    }

    [Fact]
    public async Task Listagem_vazia_mostra_o_estado_vazio()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        Assert.Contains("Nenhuma questão cadastrada.", WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes", Ct)));
    }

    [Fact]
    public async Task Cria_questao_como_rascunho_com_quatro_alternativas_e_uma_correta()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await PostAsync(client, "/Questoes/Editar", "/Questoes/Editar", Form(Marker + "nova", FourOptions, "2"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions.Include(q => q.Options).SingleAsync(Ct);
        Assert.Equal(QuestionStatus.Draft, saved.Status);
        Assert.Equal(QuizType.Ambiental, saved.QuizType);
        Assert.Equal(AgeBand.Age8To10, saved.AgeBand);
        Assert.Equal("c", Assert.Single(saved.Options, o => o.IsCorrect).Key);
        Assert.Equal(4, saved.Options.Count);
    }

    [Fact]
    public async Task Sem_alternativa_correta_ou_com_alternativa_vazia_nada_e_gravado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var semCorreta = await PostAsync(client, "/Questoes/Editar", "/Questoes/Editar", Form(Marker + "x", FourOptions, ""));
        var vazia = await PostAsync(client, "/Questoes/Editar", "/Questoes/Editar", Form(Marker + "x", ["Um", "", "Três", "Quatro"], "0"));

        Assert.Equal(HttpStatusCode.OK, semCorreta.StatusCode);
        Assert.Contains("Marque a alternativa correta.", WebUtility.HtmlDecode(await semCorreta.Content.ReadAsStringAsync(Ct)));
        Assert.Contains("Informe a alternativa B.", WebUtility.HtmlDecode(await vazia.Content.ReadAsStringAsync(Ct)));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions.AnyAsync(Ct));
    }

    [Fact]
    public async Task Aprovar_muda_rascunho_para_aprovada()
    {
        await using var factory = await NewFactoryAsync();
        var id = await SeedAsync(factory, approved: false);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var response = await PostAsync(client, $"/Questoes?handler=Aprovar&id={id}", "/Questoes", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(QuestionStatus.Approved, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task Editar_questao_aprovada_volta_para_rascunho()
    {
        await using var factory = await NewFactoryAsync();
        var id = await SeedAsync(factory, approved: true);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var form = await client.GetStringAsync($"/Questoes/Editar/{id}", Ct);
        Assert.Contains(Marker + "semente", WebUtility.HtmlDecode(form));
        var response = await PostAsync(client, $"/Questoes/Editar/{id}", $"/Questoes/Editar/{id}", Form(Marker + "editada", FourOptions, "3"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Questions.Include(q => q.Options).SingleAsync(Ct);
        Assert.Equal(QuestionStatus.Draft, saved.Status);
        Assert.Equal(Marker + "editada", saved.Statement);
        Assert.Equal("d", Assert.Single(saved.Options, o => o.IsCorrect).Key);
    }

    [Fact]
    public async Task Editar_questao_inexistente_retorna_404()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Questoes/Editar/{Guid.NewGuid()}", Ct)).StatusCode);
    }
}
