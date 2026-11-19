using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Questoes;

public sealed class IndexModel(IAppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public QuizType? Tipo { get; set; }

    [BindProperty(SupportsGet = true)]
    public AgeBand? Faixa { get; set; }

    [BindProperty(SupportsGet = true)]
    public QuestionStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [TempData]
    public string? Saved { get; set; }

    public QuestionListResult Result { get; private set; } = null!;

    public bool HasFilters => Tipo is not null || Faixa is not null || Status is not null;

    public async Task OnGetAsync(CancellationToken ct) =>
        Result = await Application.Painel.ListQuestions.HandleAsync(db, new QuestionListQuery(Tipo, Faixa, Status, Pagina), ct);

    public async Task<IActionResult> OnPostAprovarAsync(Guid id, CancellationToken ct)
    {
        if (!await ApproveQuestion.HandleAsync(db, id, ct))
        {
            return NotFound();
        }

        Saved = "Questão aprovada.";
        return RedirectToPage(new { tipo = Tipo, faixa = Faixa, status = Status, pagina = Pagina });
    }

    public static string StatusLabel(QuestionStatus status) => status == QuestionStatus.Approved ? "Aprovada" : "Rascunho";

    public string PageLink(int page) => Url.Page("/Questoes/Index", new { tipo = Tipo, faixa = Faixa, status = Status, pagina = page })!;

    public static string QuizLabel(QuizType t) => t switch
    {
        QuizType.Ambiental => "Ambiental",
        QuizType.Conhecimento => "Conhecimento",
        QuizType.Cultural => "Cultural",
        QuizType.Esportivo => "Esportivo",
        _ => "Diversidade",
    };

    /// <summary>Personagem do tema (o nome vai junto do tema, nunca só a cor).</summary>
    public static string QuizCharacter(QuizType t) => t switch
    {
        QuizType.Ambiental => "Flora",
        QuizType.Conhecimento => "Byte",
        QuizType.Cultural => "Luna",
        QuizType.Esportivo => "Max",
        _ => "Lia",
    };

    /// <summary>Classe do tema no CSS do Recreio (define as cores do tema).</summary>
    public static string QuizClass(QuizType t) => t switch
    {
        QuizType.Ambiental => "amb",
        QuizType.Conhecimento => "con",
        QuizType.Cultural => "cul",
        QuizType.Esportivo => "esp",
        _ => "div",
    };

    /// <summary>Carinha do personagem em wwwroot/img, com as dimensões reais do arquivo (docs/handoff/124.md).</summary>
    public static (string File, int Width, int Height) QuizFace(QuizType t) => t switch
    {
        QuizType.Ambiental => ("cara-flora.webp", 320, 316),
        QuizType.Conhecimento => ("cara-byte.webp", 320, 314),
        QuizType.Cultural => ("cara-luna.webp", 313, 320),
        QuizType.Esportivo => ("cara-max.webp", 296, 320),
        _ => ("cara-lia.webp", 320, 320),
    };

    public static string BandLabel(AgeBand band) => band switch
    {
        AgeBand.Age6To7 => "6–7 anos",
        AgeBand.Age8To10 => "8–10 anos",
        AgeBand.Age11To12 => "11–12 anos",
        _ => "13–14 anos",
    };
}
