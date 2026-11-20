using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>
/// Imagens do tema Recreio (#124): o inventário da ENTREGA.md §1.1 está em <c>wwwroot/img</c>
/// e é servido sem login. Arquivo estático não consulta o banco, então a fábrica não roda o seed.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RecreioImagesTests(PostgresFixture postgres)
{
    private static readonly string[] Inventory =
    [
        "helinho-corpo", "helinho", "flora-corpo", "byte-corpo", "luna-corpo", "max-corpo", "lia-corpo",
        "cara-helinho", "cara-flora", "cara-byte", "cara-luna", "cara-max", "cara-lia",
        "flora", "byte", "luna", "max", "lia", "helinho-rosto",
        "logo-jogo", "fundhas", "fundhas-invertido",
        "selo-amigo-da-natureza", "selo-curioso-do-mes", "selo-leitor-da-lia",
        "selo-cem-perguntas", "selo-nivel-10-locked", "selo-visitou-o-atelie-locked",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cada_imagem_do_inventario_e_servida_como_image_webp_sem_login()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = factory.CreateAnonymousClient();

        foreach (var name in Inventory)
        {
            using var response = await client.GetAsync($"/img/{name}.webp", Ct);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"/img/{name}.webp respondeu {(int)response.StatusCode}");
            Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Pasta_img_tem_so_o_inventario_sem_vila_nem_originais()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        // Lista o disco: o WebRootFileProvider responde pelo manifesto do build, não pela pasta.
        var folder = Path.Combine(factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath, "img");

        Assert.Equal(Inventory.Select(name => $"{name}.webp").Order(), Directory.EnumerateFiles(folder).Select(Path.GetFileName).Order());
    }
}
