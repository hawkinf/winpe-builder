namespace WinPEBuilder.Core.Build;

/// <summary>Pacote opcional do WinPE (arquivo .cab em WinPE_OCs).</summary>
public sealed record OptionalComponent(string Name, string DisplayName, string[] Requires, bool DefaultSelected = false);

public static class OptionalComponents
{
    /// <summary>
    /// Catálogo curado. A ORDEM importa: dependências vêm antes de quem depende delas, e a instalação
    /// segue esta ordem.
    /// </summary>
    public static IReadOnlyList<OptionalComponent> All { get; } =
    [
        new("WinPE-WMI", "WMI", [], true),
        new("WinPE-NetFx", ".NET Framework", ["WinPE-WMI"], true),
        new("WinPE-Scripting", "Windows Script Host (VBScript/JScript)", [], true),
        new("WinPE-PowerShell", "PowerShell", ["WinPE-WMI", "WinPE-NetFx", "WinPE-Scripting"], true),
        new("WinPE-StorageWMI", "Cmdlets de armazenamento (Storage WMI)", ["WinPE-WMI", "WinPE-NetFx", "WinPE-Scripting", "WinPE-PowerShell"], true),
        new("WinPE-DismCmdlets", "Cmdlets do DISM", ["WinPE-WMI", "WinPE-NetFx", "WinPE-Scripting", "WinPE-PowerShell"], true),
        new("WinPE-HTA", "HTML Applications (HTA)", ["WinPE-Scripting"]),
        new("WinPE-MDAC", "MDAC (ODBC/OLE DB)", []),
        new("WinPE-EnhancedStorage", "Enhanced Storage", []),
        new("WinPE-SecureStartup", "BitLocker (Secure Startup)", ["WinPE-WMI"]),
        new("WinPE-WDS-Tools", "Ferramentas WDS", []),
        new("WinPE-Dot3Svc", "Rede com fio 802.1X", []),
        new("WinPE-RNDIS", "Rede via USB (RNDIS)", []),
        new("WinPE-PPPoE", "PPPoE", []),
    ];

    public static IReadOnlyList<string> DefaultSelection { get; } =
        All.Where(c => c.DefaultSelected).Select(c => c.Name).ToList();

    /// <summary>
    /// Devolve os pacotes pedidos mais as dependências, na ordem correta de instalação, sem repetir.
    /// </summary>
    /// <exception cref="ArgumentException">Nome fora do catálogo.</exception>
    public static IReadOnlyList<OptionalComponent> Resolve(IEnumerable<string> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in requested)
        {
            var component = All.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Pacote opcional desconhecido: {name}", nameof(requested));
            wanted.Add(component.Name);
            foreach (var dependency in component.Requires)
            {
                wanted.Add(dependency);
            }
        }

        return All.Where(c => wanted.Contains(c.Name)).ToList();
    }
}
