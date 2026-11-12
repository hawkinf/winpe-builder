using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Core.Programs;

/// <summary>
/// Confere se o programa roda no WinPE escolhido. O WinPE de 64 bits não executa programas de 32 bits (sem WOW64 completo) e
/// vice-versa; o erro que aparece no WinPE ("side-by-side configuration is incorrect") não explica isso.
/// </summary>
public static class ProgramCompatibility
{
    /// <summary>Arquitetura do executável (Unknown para scripts, arquivos ilegíveis ou inexistentes).</summary>
    public static ExecutableKind KindOf(ProgramEntry program) => PeInfo.GetKind(program.SourceExe);

    /// <summary>
    /// Mensagem de incompatibilidade (para mostrar ao usuário) ou null se roda / não dá para saber. Nunca é erro: o usuário pode
    /// ter um motivo (ex.: vai adicionar o WOW64 depois), então é só aviso.
    /// </summary>
    public static string? Check(ProgramEntry program, WinPeArchitecture target)
    {
        ArgumentNullException.ThrowIfNull(program);
        var kind = KindOf(program);
        return (kind, target) switch
        {
            (ExecutableKind.X86, WinPeArchitecture.X64) =>
                "é um programa de 32 bits (x86) e este WinPE é de 64 bits, que não executa programas de 32 bits. "
                + "Use a versão de 64 bits do programa (ou gere um WinPE de 32 bits).",
            (ExecutableKind.X64, WinPeArchitecture.X86) =>
                "é um programa de 64 bits (x64) e este WinPE é de 32 bits, que não executa programas de 64 bits.",
            (ExecutableKind.Arm64, _) =>
                "é um programa ARM64, que não executa neste WinPE (x86/x64).",
            _ => null,
        };
    }

    /// <summary>Um aviso por programa incompatível ("Nome: é um programa de 32 bits…").</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<ProgramEntry> programs, WinPeArchitecture target)
    {
        ArgumentNullException.ThrowIfNull(programs);
        return [.. programs.Select(p => (p, message: Check(p, target))).Where(x => x.message is not null).Select(x => $"{x.p.Name}: {x.message}")];
    }

    /// <summary>Rótulo curto para listas ("64 bits", "32 bits ⚠"); vazio para scripts/desconhecido.</summary>
    public static string Label(ProgramEntry program, WinPeArchitecture target)
    {
        var kind = KindOf(program);
        var text = kind switch
        {
            ExecutableKind.X86 => "32 bits",
            ExecutableKind.X64 => "64 bits",
            ExecutableKind.Arm64 => "ARM64",
            _ => string.Empty,
        };
        return text.Length > 0 && Check(program, target) is not null ? text + " ⚠" : text;
    }
}
