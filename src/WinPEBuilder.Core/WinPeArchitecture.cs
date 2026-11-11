namespace WinPEBuilder.Core;

/// <summary>Arquitetura do WinPE a ser gerado.</summary>
public enum WinPeArchitecture
{
    X64,
    X86,
}

public static class WinPeArchitectureExtensions
{
    /// <summary>Nome usado pelo copype e pelas pastas do ADK ("amd64" ou "x86").</summary>
    public static string ToAdkName(this WinPeArchitecture arch) => arch switch
    {
        WinPeArchitecture.X64 => "amd64",
        WinPeArchitecture.X86 => "x86",
        _ => throw new ArgumentOutOfRangeException(nameof(arch), arch, "Arquitetura desconhecida."),
    };

    /// <summary>Texto amigável para a interface.</summary>
    public static string ToDisplayName(this WinPeArchitecture arch) => arch switch
    {
        WinPeArchitecture.X64 => "64 bits (x64)",
        WinPeArchitecture.X86 => "32 bits (x86)",
        _ => throw new ArgumentOutOfRangeException(nameof(arch), arch, "Arquitetura desconhecida."),
    };
}
