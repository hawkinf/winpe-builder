using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>
/// Programas de 32 bits não rodam no WinPE de 64 bits (sem WOW64): o app detecta pelo cabeçalho PE e avisa, em vez de deixar
/// o usuário descobrir pelo erro "side-by-side configuration is incorrect" dentro do WinPE.
/// </summary>
public sealed class ArchitectureTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-arch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    /// <summary>Escreve um "executável" mínimo: cabeçalho DOS ('MZ', offset do PE em 0x3C) + 'PE\0\0' + machine.</summary>
    private string FakePe(string name, ushort machine, string folder = "")
    {
        var bytes = new byte[256];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
        var dir = string.IsNullOrEmpty(folder) ? _temp : Path.Combine(_temp, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ProgramEntry Entry(string path, string? name = null) => new()
    {
        Id = Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),
        Name = name ?? Path.GetFileNameWithoutExtension(path),
        SourceExe = path,
    };

    // ---- cabeçalho PE ----

    [Theory]
    [InlineData((ushort)0x014C, ExecutableKind.X86)]
    [InlineData((ushort)0x8664, ExecutableKind.X64)]
    [InlineData((ushort)0xAA64, ExecutableKind.Arm64)]
    [InlineData((ushort)0x0200, ExecutableKind.Other)]    // ia64
    public void GetKind_ReadsMachineFromThePeHeader(ushort machine, ExecutableKind expected) =>
        Assert.Equal(expected, PeInfo.GetKind(FakePe("a.exe", machine)));

    [Fact]
    public void GetKind_NotPeOrBroken_IsUnknown()
    {
        Directory.CreateDirectory(_temp);
        var script = Path.Combine(_temp, "run.cmd");
        File.WriteAllText(script, "@echo oi");
        var truncated = Path.Combine(_temp, "curto.exe");
        File.WriteAllBytes(truncated, [(byte)'M', (byte)'Z', 1, 2, 3]);
        var badOffset = FakePe("offset.exe", 0x8664);
        var bytes = File.ReadAllBytes(badOffset);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 0x3C);
        File.WriteAllBytes(badOffset, bytes);
        var noSignature = FakePe("sem-pe.exe", 0x8664);
        var raw = File.ReadAllBytes(noSignature);
        raw[0x80] = (byte)'X';
        File.WriteAllBytes(noSignature, raw);

        Assert.Equal(ExecutableKind.Unknown, PeInfo.GetKind(script));
        Assert.Equal(ExecutableKind.Unknown, PeInfo.GetKind(truncated));
        Assert.Equal(ExecutableKind.Unknown, PeInfo.GetKind(badOffset));
        Assert.Equal(ExecutableKind.Unknown, PeInfo.GetKind(noSignature));
        Assert.Equal(ExecutableKind.Unknown, PeInfo.GetKind(Path.Combine(_temp, "nao-existe.exe")));
        Assert.Equal(ExecutableKind.Unknown, PeInfo.GetKind(string.Empty));
    }

    [Fact]
    public void GetKind_RealWindowsFiles()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (Environment.Is64BitOperatingSystem && File.Exists(notepad))
        {
            Assert.Equal(ExecutableKind.X64, PeInfo.GetKind(notepad));
        }

        var wow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "notepad.exe");
        if (File.Exists(wow))
        {
            Assert.Equal(ExecutableKind.X86, PeInfo.GetKind(wow)); // o mesmo programa, versão de 32 bits
        }
    }

    [Fact]
    public void GetKind_FileInUse_StillReads()
    {
        var path = FakePe("emuso.exe", 0x8664);
        using var open = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Assert.Equal(ExecutableKind.X64, PeInfo.GetKind(path));
    }

    [Fact]
    public void Describe_HasFriendlyNames()
    {
        Assert.Equal("32 bits (x86)", PeInfo.Describe(ExecutableKind.X86));
        Assert.Equal("64 bits (x64)", PeInfo.Describe(ExecutableKind.X64));
        Assert.Equal("ARM64", PeInfo.Describe(ExecutableKind.Arm64));
        Assert.Equal("desconhecida", PeInfo.Describe(ExecutableKind.Unknown));
    }

    // ---- compatibilidade ----

    [Theory]
    [InlineData((ushort)0x014C, WinPeArchitecture.X64, true)]    // 32 bits no WinPE de 64: NÃO roda
    [InlineData((ushort)0x8664, WinPeArchitecture.X64, false)]
    [InlineData((ushort)0x014C, WinPeArchitecture.X86, false)]
    [InlineData((ushort)0x8664, WinPeArchitecture.X86, true)]    // 64 bits no WinPE de 32: NÃO roda
    [InlineData((ushort)0xAA64, WinPeArchitecture.X64, true)]
    [InlineData((ushort)0xAA64, WinPeArchitecture.X86, true)]
    public void Check_Matrix(ushort machine, WinPeArchitecture target, bool expectsWarning)
    {
        var message = ProgramCompatibility.Check(Entry(FakePe("p.exe", machine)), target);

        Assert.Equal(expectsWarning, message is not null);
    }

    [Fact]
    public void Check_Message_ExplainsTheCauseAndTheWayOut()
    {
        var x86On64 = ProgramCompatibility.Check(Entry(FakePe("a.exe", 0x014C)), WinPeArchitecture.X64)!;
        var x64On86 = ProgramCompatibility.Check(Entry(FakePe("b.exe", 0x8664)), WinPeArchitecture.X86)!;

        Assert.Contains("32 bits", x86On64, StringComparison.Ordinal);
        Assert.Contains("versão de 64 bits", x86On64, StringComparison.Ordinal);
        Assert.Contains("64 bits", x64On86, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_ScriptsMissingFilesAndUnknown_AreNeverFlagged()
    {
        Directory.CreateDirectory(_temp);
        var script = Path.Combine(_temp, "ferramenta.cmd");
        File.WriteAllText(script, "@echo oi");

        Assert.Null(ProgramCompatibility.Check(Entry(script), WinPeArchitecture.X64));
        Assert.Null(ProgramCompatibility.Check(Entry(Path.Combine(_temp, "sumiu.exe")), WinPeArchitecture.X64));
    }

    [Fact]
    public void Warnings_OnePerIncompatibleProgram_WithItsName()
    {
        var programs = new[]
        {
            Entry(FakePe("ok64.exe", 0x8664), "Programa 64"),
            Entry(FakePe("velho32.exe", 0x014C), "Programa Velho"),
            Entry(FakePe("outro32.exe", 0x014C), "Outro 32"),
        };

        var warnings = ProgramCompatibility.Warnings(programs, WinPeArchitecture.X64);

        Assert.Equal(2, warnings.Count);
        Assert.StartsWith("Programa Velho:", warnings[0], StringComparison.Ordinal);
        Assert.StartsWith("Outro 32:", warnings[1], StringComparison.Ordinal);
        Assert.Empty(ProgramCompatibility.Warnings(programs.Take(1).ToList(), WinPeArchitecture.X64));
    }

    [Fact]
    public void Label_MarksIncompatibleOnes()
    {
        var x86 = Entry(FakePe("a.exe", 0x014C));
        var x64 = Entry(FakePe("b.exe", 0x8664));

        Assert.Equal("32 bits ⚠", ProgramCompatibility.Label(x86, WinPeArchitecture.X64));
        Assert.Equal("32 bits", ProgramCompatibility.Label(x86, WinPeArchitecture.X86));
        Assert.Equal("64 bits", ProgramCompatibility.Label(x64, WinPeArchitecture.X64));
        Assert.Equal("64 bits ⚠", ProgramCompatibility.Label(x64, WinPeArchitecture.X86));
    }

    // ---- opções e build ----

    [Fact]
    public void BuildOptions_CompatibilityWarnings_FollowTheArchitecture()
    {
        var program = Entry(FakePe("velho32.exe", 0x014C, "velho"), "Velho");
        BuildOptions Options(WinPeArchitecture arch) => new()
        {
            Architecture = arch,
            WorkDirectory = Path.Combine(_temp, "w"),
            OutputIsoPath = Path.Combine(_temp, "o.iso"),
            Programs = [program],
        };

        Assert.Single(Options(WinPeArchitecture.X64).CompatibilityWarnings);
        Assert.Empty(Options(WinPeArchitecture.X86).CompatibilityWarnings);
        Assert.Empty(Options(WinPeArchitecture.X64).Validate()); // aviso não é erro: não impede gerar
    }

    [Fact]
    public async Task Build_LogsAWarningForIncompatiblePrograms_ButStillBuilds()
    {
        var work = Path.Combine(_temp, "app", "work", "amd64");
        var kit = new AdkInstall(Path.Combine(_temp, "kit"));
        foreach (var f in new[] { kit.DismExe, kit.OscdimgExe, kit.CopypeCmd, kit.WinPeWim(WinPeArchitecture.X64) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, "x");
        }

        var oscdimgDir = Path.Combine(kit.DeploymentToolsRoot, "amd64", "Oscdimg");
        Directory.CreateDirectory(oscdimgDir);
        File.WriteAllText(Path.Combine(oscdimgDir, "etfsboot.com"), "x");
        File.WriteAllText(Path.Combine(oscdimgDir, "efisys_noprompt.bin"), "x");
        Directory.CreateDirectory(kit.OptionalComponentsDirectory(WinPeArchitecture.X64));
        var runner = new ScriptedRunner(c =>
        {
            if (c.Arguments.Contains("copype.cmd"))
            {
                Directory.CreateDirectory(Path.Combine(work, "media", "sources"));
                File.WriteAllText(Path.Combine(work, "media", "sources", "boot.wim"), "x");
                Directory.CreateDirectory(Path.Combine(work, "mount"));
            }

            if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(Path.Combine(_temp, "o.iso"), "x");
            }

            return (0, []);
        });
        var options = new BuildOptions
        {
            WorkDirectory = work,
            OutputIsoPath = Path.Combine(_temp, "o.iso"),
            Programs = [Entry(FakePe("velho32.exe", 0x014C, "velho"), "Programa Velho")],
        };
        var log = new List<string>();

        await new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), Path.Combine(_temp, "app"))
            .BuildAsync(options, new Progress<BuildProgress>(_ => { }), log.Add, CancellationToken.None);

        Assert.Contains(log, l => l.StartsWith("Aviso: Programa Velho:", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(_temp, "o.iso")));
    }
}
