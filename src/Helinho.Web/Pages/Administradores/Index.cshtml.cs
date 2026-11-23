using Helinho.Application.Administradores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Administradores;

public sealed class IndexModel(ListarAdministradoresHandler listar, ConviteAbertoHandler convites) : PageModel
{
    public IReadOnlyList<AdministradorRow> Rows { get; private set; } = [];

    [TempData]
    public string? Aviso { get; set; }

    [TempData]
    public string? Erro { get; set; }

    /// <summary>Selo pelo significado (guia do tema): em andamento = aviso; problema = ruim. O texto sempre acompanha a cor.</summary>
    public static (string Css, string Texto) Selo(AdminSituacao situacao) => situacao switch
    {
        AdminSituacao.Ativo => ("tag-ok", "Ativo"),
        AdminSituacao.Inativo => ("tag-neutral", "Inativo"),
        AdminSituacao.AguardandoEnvio => ("tag-warn", "Aguardando envio"),
        AdminSituacao.ConviteEnviado => ("tag-warn", "Convite enviado"),
        AdminSituacao.ConviteExpirado => ("tag-bad", "Convite expirado"),
        _ => ("tag-bad", "Não entregue"),
    };

    /// <summary>Por que a ação não foi feita. Nenhuma mensagem leva dado da conta.</summary>
    public static string Recusa(ConviteStatus status) => status switch
    {
        ConviteStatus.RateLimited => $"Esta conta já teve {ConviteAbertoHandler.LimitePorHora} convites na última hora. Tente de novo mais tarde.",
        ConviteStatus.JustSent => "Um convite acabou de sair para esta conta. Aguarde 2 minutos para reenviar.",
        ConviteStatus.SendingNow => "O convite está sendo enviado neste momento. Tente de novo em instantes.",
        _ => "Este convite não está mais em aberto. Confira a situação na lista.",
    };

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Rows = await listar.HandleAsync(cancellationToken);

    public async Task<IActionResult> OnPostReenviarAsync(Guid id, CancellationToken cancellationToken)
    {
        var status = await convites.ReenviarAsync(id, cancellationToken);
        if (status == ConviteStatus.Done)
        {
            Aviso = "Novo convite na fila de envio. O link anterior deixou de valer.";
        }
        else
        {
            Erro = Recusa(status);
        }

        return RedirectToPage();
    }
}
