namespace WinPEBuilder.Core.Adk;

/// <summary>Uma versão do ADK + WinPE add-on baixável da Microsoft.</summary>
/// <param name="Id">Identificador estável (usado em nomes de pasta/arquivo).</param>
/// <param name="DisplayName">Texto para a interface.</param>
/// <param name="AdkUrl">Link oficial (go.microsoft.com/fwlink) do adksetup.exe.</param>
/// <param name="WinPeAddonUrl">Link oficial do adkwinpesetup.exe.</param>
/// <param name="SupportsX86">Esta versão traz o WinPE de 32 bits?</param>
/// <param name="UsePrivateRoot">Instalar numa pasta do aplicativo para não conflitar com o ADK já instalado.</param>
public sealed record AdkRelease(
    string Id,
    string DisplayName,
    Uri AdkUrl,
    Uri WinPeAddonUrl,
    bool SupportsX86,
    bool UsePrivateRoot);

public static class AdkCatalog
{
    /// <summary>ADK atual (Windows 11 24H2, 10.1.26100): x64 e arm64; sem x86.</summary>
    public static AdkRelease Current { get; } = new(
        "adk-26100",
        "ADK para Windows 11 24H2 (10.1.26100) — WinPE 64 bits",
        new Uri("https://go.microsoft.com/fwlink/?linkid=2289980"),
        new Uri("https://go.microsoft.com/fwlink/?linkid=2289981"),
        SupportsX86: false,
        UsePrivateRoot: false);

    /// <summary>
    /// ADK do Windows 10 2004 (10.0.19041): a última versão com WinPE de 32 bits. Instalado em pasta
    /// privada para não mexer no ADK mais novo que o usuário já tenha.
    /// </summary>
    public static AdkRelease Legacy2004 { get; } = new(
        "adk-19041",
        "ADK para Windows 10 2004 (10.0.19041) — único com WinPE 32 bits",
        new Uri("https://go.microsoft.com/fwlink/?linkid=2120254"),
        new Uri("https://go.microsoft.com/fwlink/?linkid=2120253"),
        SupportsX86: true,
        UsePrivateRoot: true);

    public static AdkRelease For(WinPeArchitecture arch) => arch == WinPeArchitecture.X86 ? Legacy2004 : Current;
}
