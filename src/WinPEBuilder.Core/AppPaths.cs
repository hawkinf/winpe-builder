using WinPEBuilder.Core.Adk;

namespace WinPEBuilder.Core;

/// <summary>Pastas de trabalho do aplicativo (tudo fora do repositório, em %LOCALAPPDATA%).</summary>
public sealed class AppPaths(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public static AppPaths Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinPEBuilder"));

    /// <summary>Instaladores baixados (adksetup.exe, adkwinpesetup.exe).</summary>
    public string Downloads => Path.Combine(Root, "downloads");

    /// <summary>Raiz do ADK privado para a versão informada (usada como /installpath).</summary>
    public string PrivateAdkRoot(string releaseId) => Path.Combine(Root, "adk", releaseId);

    /// <summary>Áreas de montagem do WinPE por arquitetura.</summary>
    public string WorkDirectory(WinPeArchitecture arch) => Path.Combine(Root, "work", arch.ToAdkName());

    /// <summary>Raízes privadas de ADK que o localizador deve considerar.</summary>
    public IEnumerable<string> KnownPrivateAdkRoots() => [PrivateAdkRoot(AdkCatalog.Legacy2004.Id)];
}
