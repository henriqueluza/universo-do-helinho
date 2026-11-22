namespace Helinho.Web.Composition;

/// <summary>
/// Seção <c>Proxy</c>: confiança nos cabeçalhos do proxy de borda que termina o TLS (Railway).
/// Desligada por padrão: sem ela, <c>X-Forwarded-Proto</c> e o cabeçalho de IP são ignorados.
/// </summary>
public sealed class ProxyOptions
{
    public const string Section = "Proxy";

    /// <summary>Ligue só onde a aplicação é alcançável apenas pelo proxy; qualquer par de rede passa a ditar esquema e IP.</summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Cabeçalho com o IP do cliente. A Railway documenta <c>X-Real-IP</c> e o sobrescreve na borda.</summary>
    public string ClientIpHeader { get; set; } = "X-Real-IP";
}
