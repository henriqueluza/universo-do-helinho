using System.Net;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Missões diárias no painel (#15): listagem, edição com validação e missão genérica sem tipo.</summary>
[Collection(PostgresCollection.Name)]
public class MissoesPainelTests(PostgresFixture postgres)
{
    private static async Task<DailyQuest> SeedAsync(HelinhoWebFactory factory, string title, QuizType? type)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var quest = new DailyQuest(title, 3, 20, type);
        db.DailyQuests.Add(quest);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return quest;
    }

    [Fact]
    public async Task Listagem_mostra_titulo_meta_xp_e_tipo_ou_generica()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var title = $"Missao-{Guid.NewGuid():N}";
        await SeedAsync(factory, title + "-a", QuizType.Esportivo);
        await SeedAsync(factory, title + "-g", null);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Missoes", TestContext.Current.CancellationToken));

        Assert.Contains(title + "-a", html);
        Assert.Contains("20 XP", html);
        Assert.Contains("Esportivo", html);
        Assert.Contains("Genérica", html);
    }

    [Fact]
    public async Task Edicao_grava_novos_valores_e_missao_generica_fica_sem_tipo()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var quest = await SeedAsync(factory, "Original", QuizType.Cultural);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var found = await ManageDailyQuests.UpdateAsync(db, quest.Id, " Novo título ", 5, 40, null, TestContext.Current.CancellationToken);

        Assert.True(found);
        var saved = await ManageDailyQuests.FindAsync(db, quest.Id, TestContext.Current.CancellationToken);
        Assert.Equal(new QuestRow(quest.Id, "Novo título", 5, 40, null), saved);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(3, 0)]
    [InlineData(3, -5)]
    public void Meta_e_recompensa_precisam_ser_positivas(int target, int reward)
    {
        var quest = new DailyQuest("Missão", 3, 20, null);

        Assert.Throws<DomainException>(() => quest.Update("Missão", target, reward, null));
        Assert.Equal((3, 20), (quest.Target, quest.RewardXp));
    }

    [Fact]
    public async Task Tela_de_edicao_abre_e_missao_inexistente_da_404()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var quest = await SeedAsync(factory, "Editável", null);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var ok = await client.GetAsync($"/Missoes/Editar/{quest.Id}", TestContext.Current.CancellationToken);
        var missing = await client.GetAsync($"/Missoes/Editar/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
