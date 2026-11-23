using System.Globalization;
using Helinho.Application.LiveEvents;
using Helinho.Infrastructure.Fakes;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Dev;

/// <summary>
/// Caixa de saída de desenvolvimento (ADR-0003 D8): os e-mails que o envio Fake guardou em memória.
/// Só o master, e só em <c>Development</c>: fora dele a área E-mail tira a rota das páginas de <c>/Dev</c>.
/// </summary>
public sealed class CaixaDeSaidaModel(DevEmailOutbox outbox) : PageModel
{
    /// <summary>
    /// O <c>iframe</c> com <c>srcdoc</c> herda a política da página: o corpo do e-mail não executa script nem busca
    /// nada fora da própria aplicação (imagem de rastreio, fonte ou folha de estilo externas).
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'none'; form-action 'self'";

    public IReadOnlyList<DevEmail> Emails { get; private set; } = [];

    public void OnGet()
    {
        Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
        Response.Headers.CacheControl = "no-store";
        Emails = outbox.Snapshot();
    }

    public static string LocalTime(DateTimeOffset at) =>
        BrasiliaTime.ToLocal(at).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
}
