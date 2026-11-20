using Helinho.Domain;

namespace Helinho.Web.Pages.Questoes;

/// <summary>Apresentação do tema de quiz no painel (Recreio): personagem, classe de cor e carinha. Só marcação, sem regra de negócio.</summary>
public static class QuizPresentation
{
    /// <summary>Personagem do tema (o nome vai junto do tema, nunca só a cor).</summary>
    public static string Character(QuizType t) => t switch
    {
        QuizType.Ambiental => "Flora",
        QuizType.Conhecimento => "Byte",
        QuizType.Cultural => "Luna",
        QuizType.Esportivo => "Max",
        _ => "Lia",
    };

    /// <summary>Classe do tema no CSS do Recreio (define as cores do tema).</summary>
    public static string CssClass(QuizType t) => t switch
    {
        QuizType.Ambiental => "amb",
        QuizType.Conhecimento => "con",
        QuizType.Cultural => "cul",
        QuizType.Esportivo => "esp",
        _ => "div",
    };

    /// <summary>Carinha do personagem em wwwroot/img, com as dimensões reais do arquivo (docs/handoff/124.md).</summary>
    public static (string File, int Width, int Height) Face(QuizType t) => t switch
    {
        QuizType.Ambiental => ("cara-flora.webp", 320, 316),
        QuizType.Conhecimento => ("cara-byte.webp", 320, 314),
        QuizType.Cultural => ("cara-luna.webp", 313, 320),
        QuizType.Esportivo => ("cara-max.webp", 296, 320),
        _ => ("cara-lia.webp", 320, 320),
    };
}
