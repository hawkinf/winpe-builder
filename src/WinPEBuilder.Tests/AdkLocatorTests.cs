using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;

namespace WinPEBuilder.Tests;

public sealed class AdkLocatorTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-test-" + Guid.NewGuid().ToString("N"));

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
        File.WriteAllText(path, string.Empty);
    }

    private AdkInstall FakeKit(string name, bool deployment, params WinPeArchitecture[] winPe)
    {
        var install = new AdkInstall(Path.Combine(_temp, name));
        if (deployment)
        {
            Touch(install.DismExe);
            Touch(install.OscdimgExe);
        }

        foreach (var arch in winPe)
        {
            Touch(install.CopypeCmd);
            Touch(install.WinPeWim(arch));
            Directory.CreateDirectory(install.OptionalComponentsDirectory(arch));
        }

        return install;
    }

    [Fact]
    public void EmptyRoots_FindNothing()
    {
        var status = new AdkLocator([Path.Combine(_temp, "nada")]).GetStatus();
        Assert.Null(status.DeploymentTools);
        Assert.Null(status.WinPeX64);
        Assert.Null(status.WinPeX86);
    }

    [Fact]
    public void DetectsDeploymentToolsAndWinPeX64()
    {
        var kit = FakeKit("k1", deployment: true, WinPeArchitecture.X64);
        var status = new AdkLocator([kit.KitsRoot]).GetStatus();
        Assert.Equal(kit, status.DeploymentTools);
        Assert.Equal(kit, status.WinPeX64);
        Assert.Null(status.WinPeX86);
    }

    [Fact]
    public void X86CanComeFromDifferentRootThanX64()
    {
        var modern = FakeKit("modern", deployment: true, WinPeArchitecture.X64);
        var legacy = FakeKit("legacy", deployment: false, WinPeArchitecture.X86);
        var status = new AdkLocator([modern.KitsRoot, legacy.KitsRoot]).GetStatus();
        Assert.Equal(modern, status.WinPeFor(WinPeArchitecture.X64));
        Assert.Equal(legacy, status.WinPeFor(WinPeArchitecture.X86));
    }

    [Fact]
    public void WinPeWithoutWimIsNotDetected()
    {
        var kit = new AdkInstall(Path.Combine(_temp, "partial"));
        Touch(kit.CopypeCmd);
        Directory.CreateDirectory(kit.OptionalComponentsDirectory(WinPeArchitecture.X64));
        Assert.False(kit.HasWinPe(WinPeArchitecture.X64));
    }

    [Fact]
    public void BuildEnvironment_ExposesAdkVariablesAndPrependsPath()
    {
        var kit = new AdkInstall(@"C:\Kits\10");
        var env = kit.BuildEnvironment();
        Assert.Equal(kit.WinPeRoot, env["WinPERoot"]);
        Assert.Equal(kit.OscdimgDirectory, env["OSCDImgRoot"]);
        Assert.StartsWith(kit.DismDirectory + ";", env["PATH"], StringComparison.OrdinalIgnoreCase);
    }
}
