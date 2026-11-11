using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.Tests;

public sealed class IsoAndUsbTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-iso-" + Guid.NewGuid().ToString("N"));
    private string WorkRoot => Path.Combine(_temp, "app");
    private string Work => Path.Combine(WorkRoot, "work", "amd64");
    private string Iso => Path.Combine(_temp, "out", "meu winpe.iso");

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private AdkInstall FakeKit(params string[] oscdimgFiles)
    {
        var kit = new AdkInstall(Path.Combine(_temp, "kit"));
        Touch(kit.DismExe);
        Touch(kit.OscdimgExe);
        Touch(kit.CopypeCmd);
        Touch(kit.MakeWinPeMediaCmd);
        Touch(kit.WinPeWim(WinPeArchitecture.X64));
        Directory.CreateDirectory(kit.OptionalComponentsDirectory(WinPeArchitecture.X64));
        foreach (var f in oscdimgFiles)
        {
            Touch(Path.Combine(kit.DeploymentToolsRoot, "amd64", "Oscdimg", f));
        }

        return kit;
    }

    private BuildOptions Options(bool noPrompt = true) => new()
    {
        WorkDirectory = Work,
        OutputIsoPath = Iso,
        VolumeLabel = "MEU_PE",
        BootWithoutPrompt = noPrompt,
    };

    private ScriptedRunner Runner(int oscdimgExit = 0, bool createIso = true) => new(c =>
    {
        if (c.Arguments.Contains("copype.cmd"))
        {
            Touch(Path.Combine(Work, "media", "sources", "boot.wim"));
            Directory.CreateDirectory(Path.Combine(Work, "mount"));
            Directory.CreateDirectory(Path.Combine(Work, "fwfiles"));
        }

        if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (createIso)
            {
                Touch(Iso);
            }

            return (oscdimgExit, oscdimgExit == 0 ? [] : ["OSCDIMG: erro de teste"]);
        }

        return (0, []);
    });

    private static readonly IProgress<BuildProgress> NoProgress = new Progress<BuildProgress>(_ => { });

    [Fact]
    public void OscdimgArguments_AreBiosPlusUefiUdf()
    {
        var boot = new BootFiles(@"C:\k x\etfsboot.com", @"C:\k x\efisys_noprompt.bin", []);
        var args = BootFiles.BuildOscdimgArguments(boot, @"D:\w\media", @"D:\out\a b.iso", "MEU_PE");
        Assert.Equal(
            "-m -o -u2 -udfver102 -lMEU_PE -bootdata:2#p0,e,b\"C:\\k x\\etfsboot.com\"#pEF,e,b\"C:\\k x\\efisys_noprompt.bin\" \"D:\\w\\media\" \"D:\\out\\a b.iso\"",
            args);
    }

    [Fact]
    public void Resolve_PrefersNoPromptAndFwFiles_ThenFallsBack()
    {
        var kit = FakeKit("etfsboot.com", "efisys.bin"); // sem noprompt no Oscdimg
        var layout = new BuildLayout(Work);

        var withoutPrompt = BootFiles.Resolve(kit, Options(noPrompt: true), layout);
        Assert.EndsWith("efisys.bin", withoutPrompt.EfiSys); // cai para o efisys.bin

        Touch(Path.Combine(layout.FwFiles, "efisys_noprompt.bin"));
        Assert.EndsWith(Path.Combine("fwfiles", "efisys_noprompt.bin"), BootFiles.Resolve(kit, Options(noPrompt: true), layout).EfiSys);
        Assert.EndsWith("efisys.bin", BootFiles.Resolve(kit, Options(noPrompt: false), layout).EfiSys);
    }

    [Fact]
    public void Resolve_UsesIsoMediaFilesWhenAdkHasNone()
    {
        var kit = FakeKit(); // ADK sem arquivos de boot
        var layout = new BuildLayout(Work);
        Touch(Path.Combine(layout.Media, "boot", "etfsboot.com"));
        Touch(Path.Combine(layout.Media, "efi", "microsoft", "boot", "efisys.bin"));

        var boot = BootFiles.Resolve(kit, Options(), layout);

        Assert.StartsWith(layout.Media, boot.EtfsBoot);
        Assert.StartsWith(layout.Media, boot.EfiSys);
    }

    [Fact]
    public void Resolve_Missing_ThrowsFriendly()
    {
        var kit = FakeKit();
        var ex = Assert.Throws<BuildException>(() => BootFiles.Resolve(kit, Options(), new BuildLayout(Work)));
        Assert.Contains("etfsboot.com", ex.Message);
    }

    [Fact]
    public async Task Build_ProducesIso_ReplacesOldOne_AndPopulatesFwFiles()
    {
        var kit = FakeKit("etfsboot.com", "efisys.bin", "efisys_noprompt.bin");
        Touch(Iso);
        File.WriteAllText(Iso, "antiga");
        var runner = Runner();
        var service = new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), WorkRoot);

        await service.BuildAsync(Options(), NoProgress, _ => { }, CancellationToken.None);

        Assert.Equal("x", await File.ReadAllTextAsync(Iso)); // a antiga foi apagada e a nova criada
        Assert.True(File.Exists(Path.Combine(Work, "fwfiles", "etfsboot.com")));
        Assert.True(File.Exists(Path.Combine(Work, "fwfiles", "efisys_noprompt.bin")));
        var osc = runner.Commands[^1];
        Assert.Equal(kit.OscdimgExe, osc.FileName);
        Assert.Contains("-lMEU_PE", osc.Arguments);
        Assert.Contains("efisys_noprompt.bin", osc.Arguments);
        Assert.Contains(Path.Combine(Work, "media"), osc.Arguments);
    }

    [Fact]
    public async Task Build_OscdimgFailure_ReportsToolOutput()
    {
        var kit = FakeKit("etfsboot.com", "efisys.bin");
        var service = new WinPeBuildService(Runner(oscdimgExit: 1, createIso: false), new AdkLocator([kit.KitsRoot]), WorkRoot);

        var ex = await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options(), NoProgress, _ => { }, CancellationToken.None));

        Assert.Contains("oscdimg falhou", ex.Message);
        Assert.Contains("erro de teste", ex.Message);
    }

    [Fact]
    public async Task Build_OscdimgSilentlyProducesNothing_IsAnError()
    {
        var kit = FakeKit("etfsboot.com", "efisys.bin");
        var service = new WinPeBuildService(Runner(createIso: false), new AdkLocator([kit.KitsRoot]), WorkRoot);

        var ex = await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options(), NoProgress, _ => { }, CancellationToken.None));

        Assert.Contains("sem gerar a ISO", ex.Message);
    }

    // ---- pendrive ----

    private static IReadOnlyList<RemovableDrive> Usb(params char[] letters) =>
        letters.Select(l => new RemovableDrive(l, "PEN", 16L * 1024 * 1024 * 1024)).ToList();

    private void PrepareWork()
    {
        Touch(Path.Combine(Work, "media", "sources", "boot.wim"));
        Directory.CreateDirectory(Path.Combine(Work, "fwfiles"));
    }

    [Fact]
    public void Usb_Arguments()
    {
        Assert.Equal("/UFD /F \"D:\\w x\" E:", UsbMediaWriter.BuildArguments(@"D:\w x", 'e'));
    }

    [Fact]
    public async Task Usb_Write_RunsMakeWinPeMediaOnlyForListedRemovableDrive()
    {
        var kit = FakeKit();
        PrepareWork();
        var runner = new ScriptedRunner(_ => (0, ["Success"]));
        var writer = new UsbMediaWriter(runner, () => Usb('E'));

        await writer.WriteAsync(kit, Work, 'e', _ => { }, CancellationToken.None);

        var cmd = Assert.Single(runner.Commands);
        Assert.Equal("cmd.exe", cmd.FileName);
        Assert.Contains("MakeWinPEMedia.cmd", cmd.Arguments);
        Assert.Contains("/UFD /F", cmd.Arguments);
        Assert.EndsWith("E:\"", cmd.Arguments);
    }

    [Fact]
    public async Task Usb_Write_RefusesDriveNotInRemovableList_AndRunsNothing()
    {
        var kit = FakeKit();
        PrepareWork();
        var runner = new ScriptedRunner(_ => (0, []));
        var writer = new UsbMediaWriter(runner, () => Usb('E'));

        await Assert.ThrowsAsync<BuildException>(() => writer.WriteAsync(kit, Work, 'C', _ => { }, CancellationToken.None));

        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Usb_Write_WithoutBuiltWinPe_Refuses()
    {
        var kit = FakeKit();
        var runner = new ScriptedRunner(_ => (0, []));
        var writer = new UsbMediaWriter(runner, () => Usb('E'));

        var ex = await Assert.ThrowsAsync<BuildException>(() => writer.WriteAsync(kit, Work, 'E', _ => { }, CancellationToken.None));

        Assert.Contains("Gere o WinPE", ex.Message);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Usb_Write_ToolFailure_Throws()
    {
        var kit = FakeKit();
        PrepareWork();
        var writer = new UsbMediaWriter(new ScriptedRunner(_ => (5, ["Access denied"])), () => Usb('E'));

        var ex = await Assert.ThrowsAsync<BuildException>(() => writer.WriteAsync(kit, Work, 'E', _ => { }, CancellationToken.None));

        Assert.Contains("Access denied", ex.Message);
    }

    [Fact]
    public void Usb_ListRemovable_NeverIncludesSystemDrive()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory)![0];
        Assert.DoesNotContain(UsbMediaWriter.ListRemovableDrives(), d => d.Letter == char.ToUpperInvariant(system));
    }
}
