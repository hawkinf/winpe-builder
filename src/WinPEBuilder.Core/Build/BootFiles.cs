using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.Core.Build;

/// <summary>Arquivos de setor de boot da ISO: BIOS (etfsboot.com) e UEFI (efisys*.bin).</summary>
public sealed record BootFiles(string EtfsBoot, string EfiSys, IReadOnlyList<string> AllFiles)
{
    /// <summary>
    /// Procura os arquivos de boot. O WinPE do ADK os traz em fwfiles (copype) e em Deployment Tools\&lt;arq&gt;\Oscdimg;
    /// numa ISO do Windows eles também existem dentro da mídia. Prefere sempre os da arquitetura de destino.
    /// </summary>
    /// <exception cref="BuildException">Algum arquivo não foi encontrado.</exception>
    public static BootFiles Resolve(AdkInstall kit, BuildOptions options, BuildLayout layout)
    {
        var archOscdimg = Path.Combine(kit.DeploymentToolsRoot, options.Architecture.ToAdkName(), "Oscdimg");

        string[] etfsDirectories = [layout.FwFiles, archOscdimg, Path.Combine(layout.Media, "boot")];
        string[] efiDirectories = [layout.FwFiles, archOscdimg, Path.Combine(layout.Media, "efi", "microsoft", "boot")];
        string[] efiNames = options.BootWithoutPrompt ? ["efisys_noprompt.bin", "efisys.bin"] : ["efisys.bin"];

        var etfs = FirstExisting(etfsDirectories, ["etfsboot.com"])
            ?? throw new BuildException("etfsboot.com (boot BIOS) não encontrado no ADK nem na ISO de origem.");
        var efi = FirstExisting(efiDirectories, efiNames)
            ?? throw new BuildException($"{efiNames[0]} (boot UEFI) não encontrado no ADK nem na ISO de origem.");

        var all = new List<string> { etfs, efi };
        foreach (var name in new[] { "efisys.bin", "efisys_noprompt.bin" })
        {
            var extra = FirstExisting(efiDirectories, [name]);
            if (extra is not null && !all.Contains(extra, StringComparer.OrdinalIgnoreCase))
            {
                all.Add(extra);
            }
        }

        return new BootFiles(etfs, efi, all);
    }

    /// <summary>Procura por nome (na ordem pedida) e, dentro de cada nome, por diretório (na ordem pedida).</summary>
    private static string? FirstExisting(string[] directories, string[] names)
    {
        foreach (var name in names)
        {
            foreach (var directory in directories)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Argumentos do oscdimg para uma ISO UDF 1.02 inicializável em BIOS e UEFI (o mesmo que o
    /// MakeWinPEMedia usa). Os caminhos dentro de -bootdata vão entre aspas, como no MakeWinPEMedia.
    /// </summary>
    public static string BuildOscdimgArguments(BootFiles boot, string mediaDirectory, string outputIso, string volumeLabel)
    {
        ArgumentNullException.ThrowIfNull(boot);
        var bootdata = $"-bootdata:2#p0,e,b\"{boot.EtfsBoot}\"#pEF,e,b\"{boot.EfiSys}\"";
        return $"-m -o -u2 -udfver102 -l{volumeLabel} {bootdata} {CommandSpec.Quote(mediaDirectory)} {CommandSpec.Quote(outputIso)}";
    }
}
