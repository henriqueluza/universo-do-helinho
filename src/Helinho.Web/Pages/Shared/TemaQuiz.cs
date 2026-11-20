using Helinho.Domain;

namespace Helinho.Web.Painel;

/// <summary>
/// Fonte única da apresentação do tema de quiz no Recreio: nome, personagem, classe de cor e imagens.
/// Só apresentação (vive no Web); a regra "tema ↔ personagem" é fixa: Flora/Ambiental, Byte/Conhecimento,
/// Luna/Cultural, Max/Esportivo, Lia/Diversidade. Helinho é o mascote e não tem tema.
/// </summary>
/// <param name="Css">Classe do tema no CSS (<c>amb</c>, <c>con</c>…), que define as cores.</param>
/// <param name="Nome">Nome de exibição do tema.</param>
/// <param name="Personagem">Nome do personagem do tema.</param>
/// <param name="Imagem">Carinha em /img com as dimensões reais de <paramref name="Largura"/> x <paramref name="Altura"/>.</param>
/// <param name="Slug">Arquivo sem extensão do personagem (<c>flora</c>): <c>/img/{Slug}.webp</c> é o retrato de 160 px, <c>/img/{Slug}-corpo.webp</c> o corpo inteiro.</param>
/// <param name="Com">"com a Flora", "com o Byte".</param>
/// <param name="LarguraCorpo">Largura real da imagem de corpo inteiro.</param>
public sealed record TemaQuiz(string Css, string Nome, string Personagem, string Imagem, int Largura, int Altura, string Slug, string Com, int LarguraCorpo)
{
    /// <summary>Tipo de quiz sem mapeamento: um valor novo do enum precisa de um tema aqui.</summary>
    public static TemaQuiz Of(QuizType type) => type switch
    {
        QuizType.Ambiental => new("amb", "Ambiental", "Flora", "cara-flora.webp", 320, 316, "flora", "com a Flora", 159),
        QuizType.Conhecimento => new("con", "Conhecimento", "Byte", "cara-byte.webp", 320, 314, "byte", "com o Byte", 185),
        QuizType.Cultural => new("cul", "Cultural", "Luna", "cara-luna.webp", 313, 320, "luna", "com a Luna", 141),
        QuizType.Esportivo => new("esp", "Esportivo", "Max", "cara-max.webp", 296, 320, "max", "com o Max", 170),
        QuizType.Diversidade => new("div", "Diversidade", "Lia", "cara-lia.webp", 320, 320, "lia", "com a Lia", 125),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de quiz sem tema no painel."),
    };
}
