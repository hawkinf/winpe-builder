using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.Tests;

/// <summary>Runner que registra os comandos e deixa o teste simular o efeito deles no disco.</summary>
internal sealed class ScriptedRunner(Func<CommandSpec, (int Exit, string[] Lines)> handler) : IProcessRunner
{
    public List<CommandSpec> Commands { get; } = [];

    public Task<int> RunAsync(CommandSpec command, Action<string> onOutput, CancellationToken cancellationToken)
    {
        Commands.Add(command);
        var (exit, lines) = handler(command);
        foreach (var line in lines)
        {
            onOutput(line);
        }

        return Task.FromResult(exit);
    }
}

public sealed class BuildServiceTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-build-" + Guid.NewGuid().ToString("N"));
    private string WorkRoot => Path.Combine(_temp, "app");

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            foreach (var f in Directory.EnumerateFiles(_temp, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }

            Directory.Delete(_temp, recursive: true);
        }
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private AdkInstall FakeKit(WinPeArchitecture arch, params string[] components)
    {
        var kit = new AdkInstall(Path.Combine(_temp, "kit"));
        Touch(kit.DismExe);
        Touch(kit.OscdimgExe);
        Touch(kit.CopypeCmd);
        Touch(kit.WinPeWim(arch));
        var oscdimgDir = Path.Combine(kit.DeploymentToolsRoot, arch.ToAdkName(), "Oscdimg");
        Touch(Path.Combine(oscdimgDir, "etfsboot.com"));
        Touch(Path.Combine(oscdimgDir, "efisys.bin"));
        Touch(Path.Combine(oscdimgDir, "efisys_noprompt.bin"));
        Directory.CreateDirectory(kit.OptionalComponentsDirectory(arch));
        foreach (var c in components)
        {
            Touch(Path.Combine(kit.OptionalComponentsDirectory(arch), c + ".cab"));
            Touch(Path.Combine(kit.OptionalComponentsDirectory(arch), "en-us", c + "_en-us.cab"));
        }

        return kit;
    }

    private BuildOptions Options(WinPeArchitecture arch = WinPeArchitecture.X64, IReadOnlyList<string>? components = null) => new()
    {
        Architecture = arch,
        WorkDirectory = Path.Combine(WorkRoot, "work", arch.ToAdkName()),
        OutputIsoPath = Path.Combine(_temp, "out", "x.iso"),
        OptionalComponents = components ?? [],
    };

    /// <summary>Simula o copype criando o mesmo layout que ele cria.</summary>
    private (int, string[]) FakeCopype(CommandSpec c, string workDir)
    {
        if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
        {
            Touch(Path.Combine(_temp, "out", "x.iso"));
        }

        if (c.FileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) && c.Arguments.Contains("copype.cmd"))
        {
            Touch(Path.Combine(workDir, "media", "sources", "boot.wim"));
            Directory.CreateDirectory(Path.Combine(workDir, "mount"));
            Directory.CreateDirectory(Path.Combine(workDir, "fwfiles"));
        }

        return (0, []);
    }

    private static readonly IProgress<BuildProgress> NoProgress = new Progress<BuildProgress>(_ => { });

    [Fact]
    public void Resolve_AddsDependenciesInInstallOrder()
    {
        var names = OptionalComponents.Resolve(["WinPE-DismCmdlets"]).Select(c => c.Name).ToList();
        Assert.Equal(["WinPE-WMI", "WinPE-NetFx", "WinPE-Scripting", "WinPE-PowerShell", "WinPE-DismCmdlets"], names);
        Assert.Throws<ArgumentException>(() => OptionalComponents.Resolve(["WinPE-Inventado"]));
    }

    [Fact]
    public void Resolve_Duplicates_AreCollapsed()
    {
        var names = OptionalComponents.Resolve(["winpe-wmi", "WinPE-WMI", "WinPE-NetFx"]).Select(c => c.Name).ToList();
        Assert.Equal(["WinPE-WMI", "WinPE-NetFx"], names);
    }

    [Fact]
    public void Validate_ReportsFriendlyErrors()
    {
        var bad = new BuildOptions
        {
            WorkDirectory = "w",
            OutputIsoPath = "saida.txt",
            Source = WinPeSourceKind.BootWimFromIso,
            OptionalComponents = ["WinPE-WMI"],
            ScratchSpaceMb = 100,
            VolumeLabel = "tem espaco",
            FilesMode = FilesMode.OnIso,
            FilesFolder = Path.Combine(_temp, "naoexiste"),
        };
        Assert.Equal(6, bad.Validate().Count);
        Assert.Empty(Options().Validate());
    }

    [Fact]
    public async Task Adk_RunsCopypeMountPackagesConfigureUnmountInOrder()
    {
        var kit = FakeKit(WinPeArchitecture.X64, "WinPE-WMI", "WinPE-NetFx", "WinPE-Scripting", "WinPE-PowerShell");
        var options = Options(components: ["WinPE-PowerShell"]);
        var runner = new ScriptedRunner(c => FakeCopype(c, options.WorkDirectory));
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot);

        await service.BuildAsync(options, NoProgress, _ => { }, CancellationToken.None);

        var args = runner.Commands.Select(c => c.Arguments).ToList();
        Assert.Contains("copype.cmd", args[0]);
        Assert.Contains("amd64", args[0]);
        Assert.StartsWith("/Mount-Image", args[1]);
        Assert.Contains("/Index:1", args[1]);

        var packages = args.Where(a => a.Contains("/Add-Package")).ToList();
        Assert.Equal(8, packages.Count); // 4 pacotes (PowerShell + 3 dependências) x (neutro + en-us)
        Assert.Contains("WinPE-WMI.cab", packages[0]);
        Assert.Contains("WinPE-WMI_en-us.cab", packages[1]);
        Assert.Contains("WinPE-PowerShell.cab", packages[6]);

        Assert.Contains(args, a => a.Contains("/Set-InputLocale:pt-BR"));
        Assert.Contains(args, a => a.Contains("/Set-ScratchSpace:512"));
        Assert.EndsWith("/Commit", args[^2]);
        Assert.Contains("-bootdata:2#p0,e,b", args[^1]); // última etapa: oscdimg
        Assert.EndsWith("oscdimg.exe", runner.Commands[^1].FileName);
        Assert.DoesNotContain(args, a => a.Contains("/Discard"));
        Assert.All(runner.Commands.Where(c => c.FileName == kit.DismExe), c => Assert.Equal(kit.WinPeRoot, c.Environment!["WinPERoot"]));
    }

    [Fact]
    public async Task Adk_PackageFailure_DiscardsMountAndThrows()
    {
        var kit = FakeKit(WinPeArchitecture.X64, "WinPE-WMI");
        var options = Options(components: ["WinPE-WMI"]);
        var runner = new ScriptedRunner(c =>
        {
            if (c.Arguments.Contains("/Add-Package"))
            {
                return (1, ["Error: 0x80070002"]);
            }

            return FakeCopype(c, options.WorkDirectory);
        });
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot);

        var ex = await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(options, NoProgress, _ => { }, CancellationToken.None));

        Assert.Contains("0x80070002", ex.Message);
        Assert.EndsWith("/Discard", runner.Commands[^1].Arguments);
        Assert.Contains("/Unmount-Image", runner.Commands[^1].Arguments);
    }

    [Fact]
    public async Task Adk_ExistingWorkDirectory_IsCleanedIncludingReadOnlyFiles()
    {
        var kit = FakeKit(WinPeArchitecture.X64);
        var options = Options();
        var stale = Path.Combine(options.WorkDirectory, "media", "velho.txt");
        Touch(stale);
        File.SetAttributes(stale, FileAttributes.ReadOnly);
        var runner = new ScriptedRunner(c => FakeCopype(c, options.WorkDirectory));
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot);

        await service.BuildAsync(options, NoProgress, _ => { }, CancellationToken.None);

        Assert.False(File.Exists(stale));
        Assert.Contains("/Cleanup-Mountpoints", runner.Commands[0].Arguments);
    }

    [Fact]
    public async Task WorkDirectoryOutsideAllowedRoot_IsRefusedAndNothingDeleted()
    {
        var kit = FakeKit(WinPeArchitecture.X64);
        var precious = Path.Combine(_temp, "documentos");
        Touch(Path.Combine(precious, "importante.txt"));
        var options = Options() with { WorkDirectory = precious };
        var runner = new ScriptedRunner(_ => (0, []));
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot);

        await Assert.ThrowsAsync<BuildException>(() => service.BuildAsync(options, NoProgress, _ => { }, CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(precious, "importante.txt")));
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task MissingWinPeAddon_GivesFriendlyMessage()
    {
        var service = new WinPeBuildService(new ScriptedRunner(_ => (0, [])), new AdkLocator([Path.Combine(_temp, "vazio")]), WorkRoot);
        var ex = await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options(WinPeArchitecture.X86), NoProgress, _ => { }, CancellationToken.None));
        Assert.Contains("32 bits", ex.Message);
    }

    [Fact]
    public async Task Cancellation_AfterMount_DiscardsImage()
    {
        var kit = FakeKit(WinPeArchitecture.X64);
        var options = Options();
        using var cts = new CancellationTokenSource();
        var runner = new ScriptedRunner(c =>
        {
            if (c.Arguments.StartsWith("/Mount-Image"))
            {
                cts.Cancel();
            }

            return FakeCopype(c, options.WorkDirectory);
        });
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.BuildAsync(options, NoProgress, _ => { }, cts.Token));

        Assert.EndsWith("/Discard", runner.Commands[^1].Arguments);
    }

    [Fact]
    public async Task BootWimFromIso_CopiesBootFilesExportsIndex1AndMounts()
    {
        var kit = FakeKit(WinPeArchitecture.X86); // só Deployment Tools é necessário nesta fonte
        var isoRoot = Path.Combine(_temp, "isoroot");
        Touch(Path.Combine(isoRoot, "sources", "boot.wim"));
        Touch(Path.Combine(isoRoot, "boot", "etfsboot.com"));
        Touch(Path.Combine(isoRoot, "boot", "boot.sdi"));
        Touch(Path.Combine(isoRoot, "efi", "boot", "bootia32.efi"));
        Touch(Path.Combine(isoRoot, "bootmgr"));
        Touch(Path.Combine(isoRoot, "sources", "install.wim")); // não pode ser copiado
        var iso = Path.Combine(_temp, "win.iso");
        Touch(iso);
        var options = Options(WinPeArchitecture.X86) with { Source = WinPeSourceKind.BootWimFromIso, SourceIsoPath = iso };
        var runner = new ScriptedRunner(c =>
        {
            if (c.FileName == "powershell.exe" && c.Arguments.Contains("Mount-DiskImage"))
            {
                return (0, ["", "Z"]);
            }

            if (c.Arguments.StartsWith("/Export-Image"))
            {
                Touch(Path.Combine(options.WorkDirectory, "media", "sources", "boot.wim"));
            }

            if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
            {
                Touch(options.OutputIsoPath);
            }

            return (0, []);
        });
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot, _ => isoRoot);

        await service.BuildAsync(options, NoProgress, _ => { }, CancellationToken.None);

        var media = Path.Combine(options.WorkDirectory, "media");
        Assert.True(File.Exists(Path.Combine(media, "boot", "boot.sdi")));
        Assert.True(File.Exists(Path.Combine(media, "efi", "boot", "bootia32.efi")));
        Assert.True(File.Exists(Path.Combine(media, "bootmgr")));
        Assert.False(File.Exists(Path.Combine(media, "sources", "install.wim")));

        var export = runner.Commands.Single(c => c.Arguments.StartsWith("/Export-Image"));
        Assert.Contains("/SourceIndex:1", export.Arguments);
        Assert.Contains("/Bootable", export.Arguments);
        Assert.Contains(runner.Commands, c => c.Arguments.Contains("Dismount-DiskImage"));
        Assert.Contains(runner.Commands, c => c.Arguments.StartsWith("/Mount-Image"));
        Assert.DoesNotContain(runner.Commands, c => c.Arguments.Contains("copype"));
    }

    [Fact]
    public async Task ProgressNoise_IsFilteredFromLog()
    {
        // O filtro é interno; verificamos indiretamente via log de uma execução com saída ruidosa.
        var logged = new List<string>();
        var kit = FakeKit(WinPeArchitecture.X64);
        var options = Options();
        var runner = new ScriptedRunner(c =>
        {
            var result = FakeCopype(c, options.WorkDirectory);
            return c.Arguments.StartsWith("/Mount-Image")
                ? (0, ["[==========================100,0%==========================]", "A operação foi concluída com êxito."])
                : result;
        });
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot);

        await service.BuildAsync(options, NoProgress, logged.Add, CancellationToken.None);

        Assert.DoesNotContain(logged, l => l.Contains("100,0%"));
        Assert.Contains("A operação foi concluída com êxito.", logged);
    }
}
