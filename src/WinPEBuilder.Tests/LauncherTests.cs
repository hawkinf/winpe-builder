using System.Text;
using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>
/// Launcher (código compartilhado, compilado aqui também) + injeção dele na imagem.
/// O contrato é testado de ponta a ponta: o manifesto que o Core ESCREVE é o que o launcher LÊ.
/// </summary>
public sealed class LauncherTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-launcher-" + Guid.NewGuid().ToString("N"));
    private string WorkRoot => Path.Combine(_temp, "app");
    private string Work => Path.Combine(WorkRoot, "work", "amd64");

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

    private ProgramEntry FakeProgram(string name, string category = "Diagnóstico", bool desktop = true, bool menu = true)
    {
        var id = ProgramEntry.MakeId(name, []);
        var dir = Path.Combine(_temp, "origem", id);
        Touch(Path.Combine(dir, id + ".exe"));
        return new ProgramEntry
        {
            Id = id,
            Name = name,
            SourceExe = Path.Combine(dir, id + ".exe"),
            Category = category,
            ShowOnDesktop = desktop,
            ShowInStartMenu = menu,
        };
    }

    // ---- contrato Core -> launcher ----

    [Fact]
    public void Manifest_WrittenByCore_IsReadByLauncher()
    {
        var a = FakeProgram("Gerenciador de Discos", "Discos") with { Arguments = "/x \"a b\"" };
        var b = FakeProgram("CPU-Z", "Diagnóstico", desktop: false);

        var manifest = ManifestReader.Parse(ProgramStaging.BuildManifest([a, b]));

        Assert.Equal(1, manifest.Version);
        Assert.Equal(2, manifest.Programs.Count);
        var first = manifest.Programs[0];
        Assert.Equal("gerenciador-de-discos", first.Id);
        Assert.Equal("Gerenciador de Discos", first.Name);
        Assert.Equal("Discos", first.Category);
        Assert.Equal("/x \"a b\"", first.Arguments);
        Assert.Equal(Path.Combine("Programas", "gerenciador-de-discos", "gerenciador-de-discos.exe"), first.Exe);
        Assert.True(first.Desktop);
        Assert.True(first.StartMenu);
        Assert.False(manifest.Programs[1].Desktop);
    }

    [Fact]
    public void Parse_AcceptsUtf8Bom()
    {
        var json = "\uFEFF" + ProgramStaging.BuildManifest([FakeProgram("Teste")]);
        Assert.Single(ManifestReader.Parse(json).Programs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{\"version\":99,\"programs\":[]}")]
    [InlineData("{\"version\":0,\"programs\":[]}")]
    public void Parse_BadInput_ThrowsInvalidData(string json) =>
        Assert.Throws<InvalidDataException>(() => ManifestReader.Parse(json));

    [Fact]
    public void Parse_DropsIncompleteEntriesAndFillsDefaults()
    {
        const string json = """
            {"version":1,"programs":[
              {"id":"ok","name":"Ok","exe":"Programas\\ok\\ok.exe","desktop":true},
              {"id":"semnome","name":"","exe":"x.exe"},
              {"id":"semexe","name":"Sem exe","exe":""}
            ]}
            """;

        var programs = ManifestReader.Parse(json).Programs;

        var ok = Assert.Single(programs);
        Assert.Equal(ManifestReader.DefaultCategory, ok.Category);
        Assert.Equal(string.Empty, ok.Arguments);
    }

    // ---- caminhos ----

    [Fact]
    public void ResolveExe_StaysInsideFilesRoot()
    {
        var root = Path.Combine(_temp, "Arquivos");
        var good = new ProgramItem { Name = "a", Exe = Path.Combine("Programas", "a", "a.exe") };
        Assert.Equal(Path.Combine(root, "Programas", "a", "a.exe"), ManifestReader.ResolveExe(root, good));
    }

    [Theory]
    [InlineData("..\\fora.exe")]
    [InlineData("Programas\\..\\..\\fora.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("")]
    public void ResolveExe_RefusesPathsOutsideFilesRoot(string exe)
    {
        var root = Path.Combine(_temp, "Arquivos");
        Assert.Null(ManifestReader.ResolveExe(root, new ProgramItem { Name = "x", Exe = exe }));
    }

    [Fact]
    public void ResolveExe_RefusesSiblingFolderWithSamePrefix()
    {
        var root = Path.Combine(_temp, "Arquivos");
        Assert.Null(ManifestReader.ResolveExe(root, new ProgramItem { Name = "x", Exe = "..\\Arquivos2\\a.exe" }));
    }

    // ---- agrupamento ----

    [Fact]
    public void Grouping_SortsCategoriesAndProgramsAndRespectsFlags()
    {
        var items = new List<ProgramItem>
        {
            new() { Name = "Zeta", Category = "Rede", Exe = "z.exe", StartMenu = true },
            new() { Name = "Beta", Category = "Discos", Exe = "b.exe", StartMenu = true, Desktop = true },
            new() { Name = "alfa", Category = "discos", Exe = "a.exe", StartMenu = true },
            new() { Name = "Oculto", Category = "Rede", Exe = "o.exe", StartMenu = false, Desktop = true },
        };

        var groups = ManifestReader.GroupForStartMenu(items);

        Assert.Equal(["Discos", "Rede"], groups.Select(g => g.Key));
        Assert.Equal(["alfa", "Beta"], groups[0].Value.Select(p => p.Name));
        Assert.Equal(["Beta", "Oculto"], ManifestReader.ForDesktop(items).Select(p => p.Name));
    }

    // ---- achar a pasta Arquivos ----

    [Fact]
    public void FindFilesRoot_PrefersEnvironmentVariable()
    {
        var found = ManifestReader.FindFilesRoot("Z:\\Arquivos", p => p.StartsWith("Z:", StringComparison.Ordinal));
        Assert.Equal("Z:\\Arquivos", found);
    }

    [Fact]
    public void FindFilesRoot_FallsBackToScanningDrives()
    {
        var found = ManifestReader.FindFilesRoot(null, p => p == "E:\\Arquivos\\programs.json");
        Assert.Equal("E:\\Arquivos", found);
        Assert.Null(ManifestReader.FindFilesRoot("Q:\\Arquivos", _ => false));
    }

    // ---- startnet.cmd ----

    [Fact]
    public void Startnet_WithoutLauncher_IsUnchanged()
    {
        foreach (var mode in Enum.GetValues<FilesMode>())
        {
            Assert.DoesNotContain("WinPELauncher", StartnetScript.Build(mode));
        }
    }

    [Theory]
    [InlineData(FilesMode.OnIso)]
    [InlineData(FilesMode.InsideImage)]
    public void Startnet_WithLauncher_DoesNotStartIt_AndRunsUserStartupWithoutBlocking(FilesMode mode)
    {
        var script = StartnetScript.Build(mode, launcher: true);

        // o launcher agora é iniciado pelo winpeshl.ini (como shell), não pelo startnet.cmd
        Assert.DoesNotContain("WinPELauncher", script);
        // startup.cmd do usuário numa janela própria: não bloqueia o início do launcher
        Assert.Contains("start \"startup.cmd\" cmd.exe /c call Z:\\startup.cmd", script);
        Assert.DoesNotContain("\r\nif exist Z:\\startup.cmd call ", script);
        Assert.True(script.IndexOf("ARQUIVOS=", StringComparison.Ordinal) < script.IndexOf("startup.cmd", StringComparison.Ordinal));
        Assert.Equal(script, Encoding.ASCII.GetString(StartnetScript.ToBytes(script))); // só ASCII
        Assert.EndsWith("\r\n", script);
    }

    [Theory]
    [InlineData(FilesMode.OnIso)]
    [InlineData(FilesMode.InsideImage)]
    public void Startnet_WithoutLauncher_KeepsBlockingStartupCall(FilesMode mode) =>
        Assert.Contains("if exist Z:\\startup.cmd call Z:\\startup.cmd", StartnetScript.Build(mode, launcher: false));

    [Fact]
    public void Startnet_LauncherWithNoFiles_IsJustWpeinit() =>
        Assert.Equal("@echo off\r\nwpeinit\r\n", StartnetScript.Build(FilesMode.None, launcher: true));

    [Fact]
    public void Winpeshl_LauncherIsTheShell_FallbackPromptOnlyIfItEnds_AsciiCrlf()
    {
        var ini = StartnetScript.BuildWinpeshlIni();
        var lines = ini.TrimEnd('\r', '\n').Split("\r\n");

        Assert.Equal("[LaunchApps]", lines[0]);
        Assert.Equal(@"%SYSTEMROOT%\System32\WinPELauncher.exe", lines[1]);   // 1o: o shell (inicializa o WinPE sozinho)
        Assert.Equal(@"%SYSTEMROOT%\System32\cmd.exe", lines[2]);              // 2o: so aparece se o launcher terminar
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain("startnet", ini);                                  // sem prompt/startnet no caminho normal
        Assert.Equal(ini, Encoding.ASCII.GetString(StartnetScript.ToBytes(ini)));
        Assert.EndsWith("\r\n", ini);
    }

    // ---- opções ----

    private BuildOptions Options(IReadOnlyList<ProgramEntry> programs, string? launcher, WinPeSourceKind source = WinPeSourceKind.Adk,
        IReadOnlyList<string>? components = null) => new()
        {
            WorkDirectory = Work,
            OutputIsoPath = Path.Combine(_temp, "o.iso"),
            Programs = programs,
            LauncherExePath = launcher,
            Source = source,
            SourceIsoPath = source == WinPeSourceKind.BootWimFromIso ? Path.Combine(_temp, "w.iso") : null,
            OptionalComponents = components ?? [],
        };

    [Fact]
    public void InstallLauncher_OnlyWithProgramsAdkSourceAndPath()
    {
        var p = FakeProgram("A");
        Assert.True(Options([p], "x.exe").InstallLauncher);
        Assert.False(Options([], "x.exe").InstallLauncher);
        Assert.False(Options([p], null).InstallLauncher);
        Assert.False(Options([p], "x.exe", WinPeSourceKind.BootWimFromIso).InstallLauncher);
    }

    [Fact]
    public void EffectiveComponents_AddsNetFxOnlyWhenNeededWithoutDuplicates()
    {
        var p = FakeProgram("A");
        Assert.Equal(["WinPE-NetFx"], Options([p], "x.exe").EffectiveComponents);
        Assert.Equal(["WinPE-WMI", "WinPE-NetFx"], Options([p], "x.exe", components: ["WinPE-WMI"]).EffectiveComponents);
        Assert.Equal(["winpe-netfx"], Options([p], "x.exe", components: ["winpe-netfx"]).EffectiveComponents);
        Assert.Empty(Options([p], null).EffectiveComponents);
    }

    [Fact]
    public void Validate_MissingLauncherFile_IsReported()
    {
        var p = FakeProgram("A");
        var errors = Options([p], Path.Combine(_temp, "nao-existe.exe")).Validate();
        Assert.Contains(errors, e => e.Contains("launcher", StringComparison.OrdinalIgnoreCase));
    }

    // ---- build com o runner simulado ----

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
        foreach (var c in new[] { "WinPE-WMI", "WinPE-NetFx" })
        {
            Touch(Path.Combine(kit.OptionalComponentsDirectory(WinPeArchitecture.X64), c + ".cab"));
        }

        return new AdkLocator([kit.KitsRoot]);
    }

    private ScriptedRunner Runner() => new(c =>
    {
        if (c.Arguments.Contains("copype.cmd"))
        {
            Touch(Path.Combine(Work, "media", "sources", "boot.wim"));
            Directory.CreateDirectory(Path.Combine(Work, "mount"));
        }

        if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
        {
            Touch(Path.Combine(_temp, "o.iso"));
        }

        return (0, []);
    });

    private static readonly IProgress<BuildProgress> NoProgress = new Progress<BuildProgress>(_ => { });

    [Fact]
    public async Task Build_WithLauncher_InstallsItAddsNetFxAndMakesItTheShell()
    {
        var launcher = Path.Combine(_temp, "launcher", "WinPELauncher.exe");
        Touch(launcher);
        var runner = Runner();
        var steps = new List<string>();
        var service = new WinPeBuildService(runner, FakeLocator(), WorkRoot);

        await service.BuildAsync(Options([FakeProgram("HWiNFO")], launcher), new Progress<BuildProgress>(p => steps.Add(p.Message)),
            _ => { }, CancellationToken.None);

        var system32 = Path.Combine(Work, "mount", "Windows", "System32");
        Assert.True(File.Exists(Path.Combine(system32, "WinPELauncher.exe")));
        Assert.DoesNotContain("WinPELauncher", await File.ReadAllTextAsync(Path.Combine(system32, "startnet.cmd")));
        Assert.Equal(StartnetScript.BuildWinpeshlIni(), await File.ReadAllTextAsync(Path.Combine(system32, "winpeshl.ini"))); // o launcher é o shell

        var added = runner.Commands.Where(c => c.Arguments.Contains("/Add-Package")).Select(c => c.Arguments).ToList();
        Assert.Contains(added, a => a.Contains("WinPE-WMI.cab"));
        Assert.Contains(added, a => a.Contains("WinPE-NetFx.cab"));
        Assert.True(added.FindIndex(a => a.Contains("WinPE-WMI.cab")) < added.FindIndex(a => a.Contains("WinPE-NetFx.cab")));
        Assert.True(Directory.Exists(Path.Combine(Work, "media", "Arquivos", "Programas", "hwinfo")));
    }

    [Fact]
    public async Task Build_WithoutLauncherPath_DoesNotTouchSystem32OrPackages()
    {
        var runner = Runner();
        var service = new WinPeBuildService(runner, FakeLocator(), WorkRoot);

        await service.BuildAsync(Options([FakeProgram("HWiNFO")], null), NoProgress, _ => { }, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(Work, "mount", "Windows", "System32", "WinPELauncher.exe")));
        Assert.False(File.Exists(Path.Combine(Work, "mount", "Windows", "System32", "winpeshl.ini"))); // sem launcher: shell padrão do WinPE
        Assert.DoesNotContain(runner.Commands, c => c.Arguments.Contains("/Add-Package"));
    }

    // ---- settings ----

    [Fact]
    public void Settings_PersistProgramsAcrossSaveAndLoad()
    {
        var path = Path.Combine(_temp, "settings.json");
        var program = FakeProgram("CPU-Z", "Diagnóstico", desktop: false) with { Arguments = "-x", CopyFolder = false };

        Assert.True(new UserSettings { Programs = [program] }.TrySave(path));
        var loaded = UserSettings.Load(path).Programs;

        var back = Assert.Single(loaded);
        Assert.Equal(program, back);
        Assert.DoesNotContain("SourceFolder", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_OldFileWithoutProgramsStillLoads()
    {
        var path = Path.Combine(_temp, "settings.json");
        Directory.CreateDirectory(_temp);
        File.WriteAllText(path, "{ \"VolumeLabel\": \"MEUPE\" }");

        var loaded = UserSettings.Load(path);

        Assert.Equal("MEUPE", loaded.VolumeLabel);
        Assert.Empty(loaded.Programs);
    }
}
