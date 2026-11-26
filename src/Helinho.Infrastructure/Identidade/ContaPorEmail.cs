using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Identidade;

public static class ContaPorEmail
{
    /// <summary>
    /// Conta dona do e-mail, ou <c>null</c> quando nenhuma ou mais de uma o usa (#184). O índice do e-mail não é único
    /// e <c>UserManager.FindByEmailAsync</c> lança exceção na duplicata; aqui o endereço ambíguo vale como desconhecido,
    /// e quem chama dá a resposta de sempre ("credencial inválida" ou a confirmação neutra).
    /// </summary>
    public static async Task<AppUser?> FindByUniqueEmailAsync(
        this UserManager<AppUser> users, string email, CancellationToken cancellationToken = default)
    {
        var normalized = users.NormalizeEmail(email);
        var matches = await users.Users.Where(u => u.NormalizedEmail == normalized).Take(2).ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }
}
