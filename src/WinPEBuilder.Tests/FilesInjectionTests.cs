using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;

namespace WinPEBuilder.Tests;

public sealed class FilesInjectionTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-files-" + Guid.NewGuid().ToString("N"));
    private string WorkRoot => Path.Combine(_temp, "app");
    private string Work => Path.Combine(WorkRoot, "work", "amd64");

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private static void Touch(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private AdkLocator FakeLocator()
    {
        var kit = new AdkInstall(Path.Combine(_temp, "kit"));
        Touch(kit.DismExe);
        Touch(kit.OscdimgExe);
        Touch(kit.CopypeCmd);
        Touch(kit.WinPeWim(WinPeArchitecture.X64));
        var oscdimgDir = Path.Combine(kit.DeploymentToolsRoot, "amd64", "Oscdimg");
        Touch(Path.Combine(oscdimgDir, "etfsboot.com"));
        Touch(Path.Combine(oscdimgDir, "efisys_noprompt.bin"));
        Directory.CreateDirectory(kit.OptionalComponentsDirectory(WinPeArchitecture.X64));
        return new AdkLocator([kit.KitsRoot]);
    }

    private ScriptedRunner Runner() => new(c =>
    {
        if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
        {
            Touch(Path.Combine(_temp, "o.iso"));
        }

        if (c.Arguments.Contains("copype.cmd"))
        {
            Touch(Path.Combine(Work, "media", "sources", "boot.wim"));
            Directory.CreateDirectory(Path.Combine(Work, "mount"));
        }

        return (0, []);
    });

    private BuildOptions Options(FilesMode mode, string? folder) => new()
    {
        WorkDirectory = Work,
        OutputIsoPath = Path.Combine(_temp, "o.iso"),
        FilesMode = mode,
        FilesFolder = folder,
    };

    private static readonly IProgress<BuildProgress> NoProgress = new Progress<BuildProgress>(_ => { });

    [Fact]
    public void Startnet_None_OnlyRunsWpeinit()
    {
        Assert.Equal("@echo off\r\nwpeinit\r\n", StartnetScript.Build(FilesMode.None));
    }

    [Theory]
    [InlineData(FilesMode.OnIso)]
    [InlineData(FilesMode.InsideImage)]
    public void Startnet_WithFiles_MapsDriveAndIsAsciiCrlf(FilesMode mode)
    {
        var script = StartnetScript.Build(mode);
        Assert.Contains("wpeinit", script);
        Assert.Contains($"subst {StartnetScript.DriveLetter}:", script);
        Assert.Contains("set ARQUIVOS=", script);
        Assert.Contains("startup.cmd", script);
        Assert.All(script, ch => Assert.True(ch < 128, $"caractere não ASCII: {ch}"));
        Assert.DoesNotContain("\n", script.Replace("\r\n", string.Empty));
        Assert.Equal(mode == FilesMode.OnIso, script.Contains("for %%d in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnIso_CopiesToMediaWithMarker_AndWritesStartnetBeforeUnmount()
    {
        var folder = Path.Combine(_temp, "meus");
        Touch(Path.Combine(folder, "tool.exe"));
        Touch(Path.Combine(folder, "sub", "dados.txt"));
        var runner = Runner();
        var service = new WinPeBuildService(runner, FakeLocator(), WorkRoot);

        await service.BuildAsync(Options(FilesMode.OnIso, folder), NoProgress, _ => { }, CancellationToken.None);

        var dest = Path.Combine(Work, "media", "Arquivos");
        Assert.True(File.Exists(Path.Combine(dest, "tool.exe")));
        Assert.True(File.Exists(Path.Combine(dest, "sub", "dados.txt")));
        Assert.True(File.Exists(Path.Combine(dest, StartnetScript.MarkerFileName)));
        Assert.False(Directory.Exists(Path.Combine(Work, "mount", "Arquivos")));

        var startnet = await File.ReadAllTextAsync(Path.Combine(Work, "mount", "Windows", "System32", "startnet.cmd"));
        Assert.Equal(StartnetScript.Build(FilesMode.OnIso), startnet);
    }

    [Fact]
    public async Task InsideImage_CopiesIntoMount()
    {
        var folder = Path.Combine(_temp, "pequeno");
        Touch(Path.Combine(folder, "a.txt"));
        var service = new WinPeBuildService(Runner(), FakeLocator(), WorkRoot);

        await service.BuildAsync(Options(FilesMode.InsideImage, folder), NoProgress, _ => { }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(Work, "mount", "Arquivos", "a.txt")));
        Assert.True(File.Exists(Path.Combine(Work, "mount", "Arquivos", StartnetScript.MarkerFileName)));
        Assert.False(Directory.Exists(Path.Combine(Work, "media", "Arquivos")));
    }

    [Fact]
    public async Task InsideImage_TooBig_IsRefused()
    {
        var folder = Path.Combine(_temp, "grande");
        Directory.CreateDirectory(folder);
        await using (var fs = new FileStream(Path.Combine(folder, "enorme.bin"), FileMode.Create))
        {
            fs.SetLength(WinPeBuildService.MaxInsideImageBytes + 1);
        }

        var runner = Runner();
        var service = new WinPeBuildService(runner, FakeLocator(), WorkRoot);

        var ex = await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options(FilesMode.InsideImage, folder), NoProgress, _ => { }, CancellationToken.None));

        Assert.Contains("na ISO", ex.Message);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task FilesFolderOverlappingWorkDirectory_IsRefused()
    {
        Touch(Path.Combine(Work, "media", "x.txt"));
        var service = new WinPeBuildService(Runner(), FakeLocator(), WorkRoot);

        await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options(FilesMode.OnIso, Work), NoProgress, _ => { }, CancellationToken.None));
        await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options(FilesMode.OnIso, WorkRoot), NoProgress, _ => { }, CancellationToken.None));
    }
}
