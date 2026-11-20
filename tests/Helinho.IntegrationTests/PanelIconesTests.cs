using System.Text.RegularExpressions;
using Helinho.Domain;

namespace Helinho.IntegrationTests;

/// <summary>Sprite de ícones do tema Recreio (Issue #126): presente em _Layout e _Bare, sem id repetido.</summary>
[Collection(PostgresCollection.Name)]
public partial class PanelIconesTests(PostgresFixture postgres)
{
    private static readonly string[] ExpectedIds =
    [
        "i-dash", "i-alunos", "i-unid", "i-adm", "i-quest", "i-event", "i-miss", "i-desemp", "i-alert", "i-x",
        "i-go", "i-menu", "i-star", "i-spark", "i-up", "i-down", "i-table", "i-clock", "i-target", "i-play",
        "i-moon", "i-plus", "i-back", "i-ok", "i-info", "i-lock",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<symbol\\b[^>]*\\bid=\"([^\"]+)\"")]
    private static partial Regex SymbolId();

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static void AssertSprite(string html)
    {
        var ids = SymbolId().Matches(html).Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ExpectedIds.Order(), ids.Order());
        Assert.Equal(1, Regex.Count(html, "data-icones"));
        Assert.Matches("""<svg[^>]*aria-hidden="true"[^>]*data-icones|<svg[^>]*data-icones[^>]*aria-hidden="true""", html);
    }

    [Fact]
    public async Task Entrar_contem_o_sprite_de_icones()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var html = await (await client.GetAsync("/entrar", Ct)).Content.ReadAsStringAsync(Ct);

        AssertSprite(html);
    }

    [Fact]
    public async Task Pagina_autenticada_contem_o_sprite_de_icones()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await (await client.GetAsync("/Dashboard", Ct)).Content.ReadAsStringAsync(Ct);

        AssertSprite(html);
    }
}
