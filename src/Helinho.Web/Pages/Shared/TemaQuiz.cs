using Helinho.Domain;

namespace Helinho.Web.Painel;

/// <summary>Tema do quiz no Recreio: nome, personagem, classe de cor e imagem da carinha (dimensões reais de /img).</summary>
public sealed record TemaQuiz(string Css, string Nome, string Personagem, string Imagem, int Largura, int Altura)
{
    public static TemaQuiz Of(QuizType type) => type switch
    {
        QuizType.Ambiental => new("amb", "Ambiental", "Flora", "cara-flora.webp", 320, 316),
        QuizType.Conhecimento => new("con", "Conhecimento", "Byte", "cara-byte.webp", 320, 314),
        QuizType.Cultural => new("cul", "Cultural", "Luna", "cara-luna.webp", 313, 320),
        QuizType.Esportivo => new("esp", "Esportivo", "Max", "cara-max.webp", 296, 320),
        _ => new("div", "Diversidade", "Lia", "cara-lia.webp", 320, 320),
    };
}
