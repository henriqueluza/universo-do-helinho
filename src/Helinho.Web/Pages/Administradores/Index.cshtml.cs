using Helinho.Application.Administradores;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Administradores;

public sealed class IndexModel(ListarAdministradoresHandler listar) : PageModel
{
    public IReadOnlyList<AdministradorRow> Rows { get; private set; } = [];

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

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Rows = await listar.HandleAsync(cancellationToken);
}
