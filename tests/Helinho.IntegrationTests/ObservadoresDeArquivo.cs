using System.Runtime.CompilerServices;

namespace Helinho.IntegrationTests;

/// <summary>
/// Hosts de teste não observam arquivos pelo sistema operacional (Issue #215). No Linux cada host abria duas
/// instâncias de inotify (recarregamento dos appsettings e <c>asp-append-version</c> no wwwroot), a do wwwroot
/// sobrevivia ao descarte do host, e a suíte esgotava o limite de 128 por usuário do runner do CI.
/// </summary>
internal static class ObservadoresDeArquivo
{
    /// <summary>Lida por todo <c>PhysicalFileProvider</c> do processo: troca o <c>FileSystemWatcher</c> por sondagem.</summary>
    internal const string Sondagem = "DOTNET_USE_POLLING_FILE_WATCHER";

    /// <summary>Configuração de host <c>hostBuilder:reloadConfigOnChange</c>: os appsettings deixam de ser observados.</summary>
    internal const string RecarregarConfiguracao = "DOTNET_hostBuilder__reloadConfigOnChange";

    // Inicializador de módulo: roda antes de qualquer teste do assembly e, portanto, antes de o primeiro host nascer.
    [ModuleInitializer]
    internal static void Desligar()
    {
        Environment.SetEnvironmentVariable(Sondagem, "1");
        Environment.SetEnvironmentVariable(RecarregarConfiguracao, "false");
    }
}
