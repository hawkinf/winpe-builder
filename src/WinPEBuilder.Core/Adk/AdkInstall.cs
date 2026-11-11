using System.Runtime.InteropServices;

namespace WinPEBuilder.Core.Adk;

/// <summary>
/// Uma instalação do Windows ADK (raiz "Windows Kits\10"), possivelmente só com parte dos recursos
/// (Deployment Tools e/ou WinPE add-on). Só lê o disco; nunca altera nada.
/// </summary>
public sealed record AdkInstall(string KitsRoot)
{
    public string AssessmentRoot => Path.Combine(KitsRoot, "Assessment and Deployment Kit");
    public string DeploymentToolsRoot => Path.Combine(AssessmentRoot, "Deployment Tools");
    public string WinPeRoot => Path.Combine(AssessmentRoot, "Windows Preinstallation Environment");

    /// <summary>Pasta de ferramentas do ADK para o processador deste PC ("amd64", "x86" ou "arm64").</summary>
    public static string HostFolder => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "amd64",
    };

    public string DismDirectory => Path.Combine(DeploymentToolsRoot, HostFolder, "DISM");
    public string DismExe => Path.Combine(DismDirectory, "dism.exe");
    public string OscdimgDirectory => Path.Combine(DeploymentToolsRoot, HostFolder, "Oscdimg");
    public string OscdimgExe => Path.Combine(OscdimgDirectory, "oscdimg.exe");
    public string CopypeCmd => Path.Combine(WinPeRoot, "copype.cmd");
    public string MakeWinPeMediaCmd => Path.Combine(WinPeRoot, "MakeWinPEMedia.cmd");

    public string WinPeArchRoot(WinPeArchitecture arch) => Path.Combine(WinPeRoot, arch.ToAdkName());
    public string WinPeWim(WinPeArchitecture arch) => Path.Combine(WinPeArchRoot(arch), "en-us", "winpe.wim");
    public string OptionalComponentsDirectory(WinPeArchitecture arch) => Path.Combine(WinPeArchRoot(arch), "WinPE_OCs");

    /// <summary>DISM e oscdimg do ADK presentes (recurso "Deployment Tools").</summary>
    public bool HasDeploymentTools => File.Exists(DismExe) && File.Exists(OscdimgExe);

    /// <summary>WinPE add-on presente para a arquitetura pedida.</summary>
    public bool HasWinPe(WinPeArchitecture arch) =>
        File.Exists(CopypeCmd) && File.Exists(WinPeWim(arch)) && Directory.Exists(OptionalComponentsDirectory(arch));

    /// <summary>
    /// Variáveis de ambiente que o copype/MakeWinPEMedia esperam (o DandISetEnv.bat as define lendo o
    /// registro, o que não funciona para instalações em pasta privada — por isso as montamos aqui).
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WinPERoot"] = WinPeRoot,
            ["WinPERootNoArch"] = WinPeRoot,
            ["DandIRoot"] = DeploymentToolsRoot,
            ["DISMRoot"] = DismDirectory,
            ["OSCDImgRoot"] = OscdimgDirectory,
            ["PATH"] = string.Join(';', DismDirectory, OscdimgDirectory, WinPeRoot, path),
        };
    }
}
