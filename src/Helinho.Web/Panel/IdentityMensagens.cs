using Microsoft.AspNetCore.Identity;

namespace Helinho.Web.Panel;

/// <summary>Mensagens em português para os erros do Identity (o texto padrão vem em inglês).</summary>
public static class IdentityMensagens
{
    public static (string Campo, string Mensagem) Traduzir(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" => ("Input.UserName", "Já existe um usuário com este nome de usuário."),
        "InvalidUserName" => ("Input.UserName", "O nome de usuário tem caracteres não permitidos."),
        "DuplicateEmail" => ("Input.Email", "Já existe um usuário com este e-mail."),
        "InvalidEmail" => ("Input.Email", "Informe um e-mail válido."),
        "PasswordTooShort" => ("Input.Password", "A senha deve ter pelo menos 6 caracteres."),
        "PasswordRequiresDigit" => ("Input.Password", "A senha deve ter pelo menos um número."),
        "PasswordRequiresLower" => ("Input.Password", "A senha deve ter pelo menos uma letra minúscula."),
        "PasswordRequiresUpper" => ("Input.Password", "A senha deve ter pelo menos uma letra maiúscula."),
        "PasswordRequiresNonAlphanumeric" => ("Input.Password", "A senha deve ter pelo menos um símbolo."),
        _ => (string.Empty, "Não foi possível salvar. Revise os dados e tente de novo."),
    };
}
