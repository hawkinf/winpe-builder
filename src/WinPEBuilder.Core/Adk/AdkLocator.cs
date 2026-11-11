using Microsoft.Win32;

namespace WinPEBuilder.Core.Adk;

/// <summary>Procura instalações do Windows ADK em uma lista de raízes candidatas.</summary>
public sealed class AdkLocator
{
    private readonly IReadOnlyList<string> _kitsRoots;

    public AdkLocator(IEnumerable<string> kitsRoots)
    {
        ArgumentNullException.ThrowIfNull(kitsRoots);
        _kitsRoots = kitsRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.GetFullPath(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Raízes padrão: registro do Windows, Program Files e pastas privadas do aplicativo.</summary>
    public static AdkLocator CreateDefault(IEnumerable<string>? privateRoots = null)
    {
        var roots = new List<string>();
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
                if (key?.GetValue("KitsRoot10") is string root)
                {
                    roots.Add(root);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Sem acesso ao registro: seguimos com os caminhos padrão.
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            roots.Add(Path.Combine(programFilesX86, "Windows Kits", "10"));
        }

        if (privateRoots is not null)
        {
            roots.AddRange(privateRoots);
        }

        return new AdkLocator(roots);
    }

    /// <summary>Instalação que tem DISM + oscdimg (Deployment Tools), ou null.</summary>
    public AdkInstall? FindDeploymentTools() =>
        _kitsRoots.Select(r => new AdkInstall(r)).FirstOrDefault(i => i.HasDeploymentTools);

    /// <summary>Instalação que tem o WinPE add-on para a arquitetura, ou null.</summary>
    public AdkInstall? FindWinPe(WinPeArchitecture arch) =>
        _kitsRoots.Select(r => new AdkInstall(r)).FirstOrDefault(i => i.HasWinPe(arch));

    public AdkStatus GetStatus() => new(FindDeploymentTools(), FindWinPe(WinPeArchitecture.X64), FindWinPe(WinPeArchitecture.X86));
}

/// <summary>Resumo do que já existe no PC.</summary>
public sealed record AdkStatus(AdkInstall? DeploymentTools, AdkInstall? WinPeX64, AdkInstall? WinPeX86)
{
    public AdkInstall? WinPeFor(WinPeArchitecture arch) => arch == WinPeArchitecture.X64 ? WinPeX64 : WinPeX86;
}
