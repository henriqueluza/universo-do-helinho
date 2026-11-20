using Helinho.Domain;
using Helinho.Web.Painel;

namespace Helinho.IntegrationTests;

/// <summary>A fonte única do mapeamento tema de quiz → classe, personagem e imagens (Issue #194). Não precisa de banco.</summary>
public class TemaQuizTests
{
    public static TheoryData<QuizType, string, string, string, string> Temas => new()
    {
        { QuizType.Ambiental, "amb", "Ambiental", "Flora", "flora" },
        { QuizType.Conhecimento, "con", "Conhecimento", "Byte", "byte" },
        { QuizType.Cultural, "cul", "Cultural", "Luna", "luna" },
        { QuizType.Esportivo, "esp", "Esportivo", "Max", "max" },
        { QuizType.Diversidade, "div", "Diversidade", "Lia", "lia" },
    };

    [Theory]
    [MemberData(nameof(Temas))]
    public void Cada_tipo_tem_classe_nome_personagem_e_imagens_com_as_dimensoes_reais(
        QuizType tipo, string css, string nome, string personagem, string slug)
    {
        var tema = TemaQuiz.Of(tipo);

        Assert.Equal(css, tema.Css);
        Assert.Equal(nome, tema.Nome);
        Assert.Equal(personagem, tema.Personagem);
        Assert.Equal(slug, tema.Slug);
        Assert.Equal($"cara-{slug}.webp", tema.Imagem);
        Assert.Equal((tema.Largura, tema.Altura), TamanhoWebp(tema.Imagem));
        Assert.Equal((160, 160), TamanhoWebp($"{slug}.webp"));
        Assert.Equal(tema.LarguraCorpo, TamanhoWebp($"{slug}-corpo.webp").Largura);
        Assert.StartsWith("com ", tema.Com);
        Assert.EndsWith(personagem, tema.Com);
    }

    [Fact]
    public void Todo_valor_do_enum_tem_tema_e_os_temas_nao_se_repetem()
    {
        var temas = Enum.GetValues<QuizType>().Select(TemaQuiz.Of).ToList();

        Assert.Equal(5, temas.Count);
        Assert.Equal(temas.Count, temas.Select(t => t.Css).Distinct().Count());
        Assert.Equal(temas.Count, temas.Select(t => t.Personagem).Distinct().Count());
        Assert.DoesNotContain(temas, t => t.Personagem == "Helinho");
    }

    [Fact]
    public void Tipo_sem_mapeamento_falha_de_forma_visivel_em_vez_de_cair_em_padrao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TemaQuiz.Of((QuizType)999));
    }

    // Lê largura e altura do cabeçalho WebP estendido (VP8X): 24 bits little-endian, valor menos um.
    private static (int Largura, int Altura) TamanhoWebp(string arquivo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Helinho.Web", "wwwroot", "img")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var bytes = File.ReadAllBytes(Path.Combine(dir.FullName, "src", "Helinho.Web", "wwwroot", "img", arquivo));
        Assert.Equal("VP8X", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        return (1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)), 1 + (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16)));
    }
}
