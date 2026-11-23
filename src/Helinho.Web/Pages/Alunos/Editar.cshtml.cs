using System.Globalization;
using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Application.Painel;
using Microsoft.AspNetCore.Mvc;

namespace Helinho.Web.Pages.Alunos;

public sealed class EditarModel(
    IAppDbContext db, ICurrentUser user, IClock clock, StudentAccessRequests requests, IStudentAccounts accounts, ILogger<EditarModel> logger)
    : StudentFormPage
{
    public const string AccessQueuedKey = "AcessoEnfileirado";

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    /// <summary>Estado do acesso ao jogo: o que falta, o que o botão envia e a situação do último pedido.</summary>
    public StudentAccessState Access { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // Aluno de outra unidade não passa pelo filtro global: 404, igual a um id que não existe.
        var form = await SaveStudent.LoadAsync(db, Id, cancellationToken);
        if (form is null || !await PrepareAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = StudentInput.From(form);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await SaveStudent.UpdateAsync(db, clock, requests, Id, Input.ToForm(), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        if (result.AccessQueued)
        {
            StudentAccessAudit.AccessQueued(logger, Id, user.UserId);
        }

        if (result.Succeeded)
        {
            TempData["Aviso"] = "Cadastro atualizado.";
            return RedirectToPage("/Alunos/Index");
        }

        Errors = result.Errors;
        return await PrepareAsync(cancellationToken) ? Page() : NotFound();
    }

    /// <summary>Botão "Enviar acesso". Qualquer recusa volta para a ficha, que explica o que falta; outra unidade é 404.</summary>
    public async Task<IActionResult> OnPostEnviarAcessoAsync(CancellationToken cancellationToken)
    {
        var result = await SendStudentAccess.HandleAsync(db, user, requests, Id, cancellationToken);
        if (result == SendStudentAccessResult.NotFound)
        {
            return NotFound();
        }

        if (result == SendStudentAccessResult.Queued)
        {
            StudentAccessAudit.AccessQueued(logger, Id, user.UserId);
            TempData[AccessQueuedKey] = true;
        }

        return RedirectToPage("/Alunos/Editar", new { id = Id });
    }

    /// <summary>Data e hora de Brasília, como a ficha mostra a situação do envio.</summary>
    public static string When(DateTimeOffset at) =>
        BrasiliaTime.ToLocal(at).ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.InvariantCulture);

    private async Task<bool> PrepareAsync(CancellationToken cancellationToken)
    {
        UnitName = await SaveStudent.UnitNameAsync(db, user, cancellationToken);
        if (await SendStudentAccess.StateAsync(db, user, requests, accounts, Id, cancellationToken) is not { } access)
        {
            return false;
        }

        Access = access;
        return true;
    }
}
