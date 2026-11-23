using System.Buffers.Text;
using System.Text;

namespace Helinho.Application.Identidade;

/// <summary>
/// Links de acesso enviados por e-mail (ADR-0003 §10): provedores de token, finalidade, validades e formato do link.
/// Quem gera o token (tratador de e-mail) e quem o confere (definir senha) leem daqui.
/// </summary>
public static class LinkDeAcesso
{
    /// <summary>Nome do provedor de convite. É também a finalidade da chave no Data Protection: trocá-lo invalida os links já enviados.</summary>
    public const string ProvedorConvite = "HelinhoConvite";

    /// <summary>Nome do provedor de redefinição. Vale a mesma regra do nome do convite.</summary>
    public const string ProvedorRedefinicao = "HelinhoRedefinicao";

    /// <summary>Finalidade gravada no token; a mesma para os dois provedores, porque nada no link diz o tipo.</summary>
    public const string Finalidade = "DefinirSenha";

    public const string Caminho = "/conta/definir-senha";

    // Um token do Identity tem poucas centenas de caracteres; acima disso não é link nosso.
    private const int TamanhoMaximoDoToken = 2048;

    /// <summary>PROPOSTA (D4): convite de administrador e primeiro acesso do aluno.</summary>
    public static TimeSpan ValidadeConvite { get; } = TimeSpan.FromHours(72);

    /// <summary>PROPOSTA (D4): pedido de redefinição e novo envio para quem já tem senha.</summary>
    public static TimeSpan ValidadeRedefinicao { get; } = TimeSpan.FromHours(1);

    /// <summary>Caminho e query do link (§10.3). A base vem de <c>App__PublicBaseUrl</c>, nunca da requisição. Sem dado pessoal: só o id e o token.</summary>
    public static string Montar(Guid userId, string token) =>
        $"{Caminho}?usuario={userId:D}&token={Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token))}";

    /// <summary>Desfaz a codificação do token feita por <see cref="Montar"/>; nulo quando o valor não é Base64 para URL.</summary>
    public static string? LerToken(string? valor)
    {
        if (string.IsNullOrEmpty(valor) || valor.Length > TamanhoMaximoDoToken || !Base64Url.IsValid(valor))
        {
            return null;
        }

        return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(valor));
    }
}
