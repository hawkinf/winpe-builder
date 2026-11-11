using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.Tests;

public sealed class AdkInstallerTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-inst-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private sealed class FakeDownloader : IFileDownloader
    {
        public List<Uri> Requested { get; } = [];

        public Task DownloadAsync(Uri source, string destinationPath, IProgress<double?> progress, CancellationToken cancellationToken)
        {
            Requested.Add(source);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, "fake");
            progress.Report(1);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunner(int exitCode = 0) : IProcessRunner
    {
        public List<CommandSpec> Commands { get; } = [];

        public Task<int> RunAsync(CommandSpec command, Action<string> onOutput, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(exitCode);
        }
    }

    private static readonly IProgress<InstallProgress> NoProgress = new Progress<InstallProgress>(_ => { });

    [Fact]
    public void Catalog_X86UsesLegacyAdkInPrivateRoot()
    {
        var x86 = AdkCatalog.For(WinPeArchitecture.X86);
        Assert.True(x86.SupportsX86);
        Assert.True(x86.UsePrivateRoot);
        Assert.Contains("2120254", x86.AdkUrl.Query);
        Assert.Contains("2120253", x86.WinPeAddonUrl.Query);

        var x64 = AdkCatalog.For(WinPeArchitecture.X64);
        Assert.False(x64.SupportsX86);
        Assert.False(x64.UsePrivateRoot);
        Assert.All(new[] { x86.AdkUrl, x86.WinPeAddonUrl, x64.AdkUrl, x64.WinPeAddonUrl },
            u => Assert.Equal("go.microsoft.com", u.Host));
    }

    [Fact]
    public void BuildSetupArguments_QuotesInstallPath()
    {
        Assert.Equal("/quiet /norestart /ceip off /features OptionId.DeploymentTools",
            AdkInstaller.BuildSetupArguments(AdkInstaller.DeploymentToolsFeature, null));
        Assert.Equal("/quiet /norestart /ceip off /features OptionId.WindowsPreinstallationEnvironment /installpath \"C:\\a b\\adk\"",
            AdkInstaller.BuildSetupArguments(AdkInstaller.WinPeFeature, @"C:\a b\adk"));
    }

    [Fact]
    public async Task Install_DownloadsBothThenRunsAdkBeforeAddon()
    {
        var downloader = new FakeDownloader();
        var runner = new FakeRunner();
        var installer = new AdkInstaller(runner, downloader, _temp, _ => true);

        await installer.InstallAsync(AdkCatalog.Legacy2004, @"C:\priv", NoProgress, _ => { }, CancellationToken.None);

        Assert.Equal([AdkCatalog.Legacy2004.AdkUrl, AdkCatalog.Legacy2004.WinPeAddonUrl], downloader.Requested);
        Assert.Equal(2, runner.Commands.Count);
        Assert.EndsWith("adksetup.exe", runner.Commands[0].FileName);
        Assert.Contains(AdkInstaller.DeploymentToolsFeature, runner.Commands[0].Arguments);
        Assert.EndsWith("adkwinpesetup.exe", runner.Commands[1].FileName);
        Assert.Contains(AdkInstaller.WinPeFeature, runner.Commands[1].Arguments);
        Assert.All(runner.Commands, c => Assert.Contains("/installpath \"C:\\priv\"", c.Arguments));
    }

    [Fact]
    public async Task Install_RebootRequiredExitCodeIsSuccess()
    {
        var installer = new AdkInstaller(new FakeRunner(3010), new FakeDownloader(), _temp, _ => true);
        await installer.InstallAsync(AdkCatalog.Current, null, NoProgress, _ => { }, CancellationToken.None);
    }

    [Fact]
    public async Task Install_NonZeroExit_Throws()
    {
        var installer = new AdkInstaller(new FakeRunner(1), new FakeDownloader(), _temp, _ => true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.InstallAsync(AdkCatalog.Current, null, NoProgress, _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task Install_UnsignedInstaller_IsDiscardedAndNothingRuns()
    {
        var runner = new FakeRunner();
        var installer = new AdkInstaller(runner, new FakeDownloader(), _temp, _ => false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(AdkCatalog.Current, null, NoProgress, _ => { }, CancellationToken.None));

        Assert.Empty(runner.Commands);
        Assert.False(File.Exists(Path.Combine(_temp, AdkCatalog.Current.Id, "adksetup.exe")));
    }

    [Fact]
    public void Authenticode_UnsignedFile_IsRejected()
    {
        Directory.CreateDirectory(_temp);
        var file = Path.Combine(_temp, "x.exe");
        File.WriteAllText(file, "nao e um executavel assinado");
        Assert.False(AuthenticodeVerifier.IsSignedByMicrosoft(file));
    }

    [Fact]
    public void CommandSpec_QuoteAndCmd()
    {
        Assert.Equal("\"C:\\a b\\\\\"", CommandSpec.Quote(@"C:\a b\"));
        var cmd = CommandSpec.Cmd(@"C:\Program Files (x86)\x\copype.cmd", "amd64 \"D:\\w\"");
        Assert.Equal("cmd.exe", cmd.FileName);
        Assert.Equal("/s /c \"\"C:\\Program Files (x86)\\x\\copype.cmd\" amd64 \"D:\\w\"\"", cmd.Arguments);
    }

    [Fact]
    public async Task ProcessRunner_CapturesOutputAndExitCode()
    {
        var lines = new List<string>();
        var exit = await new ProcessRunner().RunAsync(
            new CommandSpec("cmd.exe", "/c echo ola& exit /b 7"), lines.Add, CancellationToken.None);
        Assert.Equal(7, exit);
        Assert.Contains("ola", lines);
    }
}
