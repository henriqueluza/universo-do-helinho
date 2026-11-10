namespace Helinho.Web.Panel;

/// <summary>Nomes das políticas de autorização do painel. O papel Student nunca entra.</summary>
public static class PanelPolicies
{
    /// <summary>Master ou Admin.</summary>
    public const string Panel = "Panel";

    /// <summary>Somente Master.</summary>
    public const string MasterOnly = "MasterOnly";
}
