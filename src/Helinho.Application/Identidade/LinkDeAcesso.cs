using System.Buffers.Text;
using System.Text;

namespace Helinho.Application.Identidade;

/// <summary>
/// Links de acesso enviados por e-mail (ADR-0003 §10): provedores de token, finalidade, validades e formato do link.
/// Quem gera o token (tratador de e-mail) e quem o confere (definir senha) leem daqui.
/// </summary>
public static class LinkDeAcesso
{
    // Os nomes dos provedores são também a finalidade da chave no Data Protection: trocá-los invalida os links já enviados.
    public const string ProvedorConvite = "HelinhoConvite";
    public const string ProvedorRedefinicao = "HelinhoRedefinicao";

    /// <summary>Finalidade gravada no token; a mesma para os dois provedores, porque nada no link diz o tipo.</summary>
    public const string Finalidade = "DefinirSenha";

    // Um token do Identity tem poucas centenas de caracteres; acima disso não é link nosso.
    private const int TamanhoMaximoDoToken = 2048;

    /// <summary>PROPOSTA (D4): convite de administrador e primeiro acesso do aluno, enquanto a conta não tem senha.</summary>
    public static TimeSpan ValidadeConvite { get; } = TimeSpan.FromHours(72);

    /// <summary>PROPOSTA (D4): pedido de redefinição e novo envio para quem já tem senha.</summary>
    public static TimeSpan ValidadeRedefinicao { get; } = TimeSpan.FromHours(1);

    /// <summary>Caminho e query do link (§10.3), sem dado pessoal: só o id e o token. A base vem de <c>App__PublicBaseUrl</c>, nunca da requisição.</summary>
    public static string Montar(Guid userId, string token) => $"/conta/definir-senha?usuario={userId:D}&token={TokenParaUrl(token)}";

    public static string TokenParaUrl(string token) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

    /// <summary>Desfaz <see cref="TokenParaUrl"/>; nulo quando o valor não é Base64 para URL.</summary>
    public static string? LerToken(string? valor) =>
        string.IsNullOrEmpty(valor) || valor.Length > TamanhoMaximoDoToken || !Base64Url.IsValid(valor)
            ? null
            : Encoding.UTF8.GetString(Base64Url.DecodeFromChars(valor));
}
