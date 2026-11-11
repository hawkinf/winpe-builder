using System.Text.Json;
using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Programs;

namespace WinPEBuilder.Tests;

public sealed class ProgramsTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-prog-" + Guid.NewGuid().ToString("N"));
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

    private static void Touch(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Cria um "programa portátil" falso: pasta própria com exe + arquivo vizinho.</summary>
    private ProgramEntry FakeProgram(string name, string folder = "hwinfo", string exe = "HWiNFO64.exe", string? id = null, bool copyFolder = true)
    {
        var dir = Path.Combine(_temp, "origem", folder);
        Touch(Path.Combine(dir, exe));
        Touch(Path.Combine(dir, "dados", "config.ini"));
        return new ProgramEntry
        {
            Id = id ?? ProgramEntry.MakeId(name, []),
            Name = name,
            SourceExe = Path.Combine(dir, exe),
            Category = "Diagnóstico",
            CopyFolder = copyFolder,
        };
    }

    // ---- ids ----

    [Theory]
    [InlineData("HWiNFO 64", "hwinfo-64")]
    [InlineData("Diagnóstico Rápido!", "diagnostico-rapido")]
    [InlineData("  ---  ", "programa")]
    [InlineData("CPU-Z", "cpu-z")]
    public void MakeId_Slugifies(string name, string expected) => Assert.Equal(expected, ProgramEntry.MakeId(name, []));

    [Fact]
    public void MakeId_AvoidsCollisions_CaseInsensitive()
    {
        Assert.Equal("cpu-z-2", ProgramEntry.MakeId("CPU-Z", ["cpu-z"]));
        Assert.Equal("cpu-z-3", ProgramEntry.MakeId("CPU-Z", ["CPU-Z", "cpu-z-2"]));
    }

    [Fact]
    public void MakeId_LongName_IsTruncatedAndStillValid()
    {
        var id = ProgramEntry.MakeId(new string('a', 200), []);
        Assert.True(id.Length <= 36);
        Assert.Empty(ProgramEntry.Validate([FakeProgram("x", id: id)]));
    }

    // ---- validação ----

    [Fact]
    public void Validate_ValidProgram_NoErrors() => Assert.Empty(ProgramEntry.Validate([FakeProgram("HWiNFO")]));

    [Fact]
    public void Validate_ReportsEachProblem()
    {
        var good = FakeProgram("Bom");
        var dup = good with { Name = "Duplicado" }; // mesmo Id
        var missing = good with { Id = "sumiu", Name = "Sumiu", SourceExe = Path.Combine(_temp, "nao-existe.exe") };
        var badExt = FakeProgram("Doc", folder: "doc", exe: "leia.txt");
        var noName = good with { Id = "semnome", Name = " " };
        var badCategory = good with { Id = "cat", Name = "Cat", Category = "a/b" };

        var errors = ProgramEntry.Validate([good, dup, missing, badExt, noName, badCategory]);

        Assert.Contains(errors, e => e.Contains("repetido"));
        Assert.Contains(errors, e => e.Contains("não existe mais"));
        Assert.Contains(errors, e => e.Contains(".exe, .cmd"));
        Assert.Contains(errors, e => e.Contains("nome"));
        Assert.Contains(errors, e => e.Contains("categoria"));
    }

    [Fact]
    public void IsProtectedFolder_CoversSystemProfileAndDriveRoot_NotOwnFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(ProgramEntry.IsProtectedFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.True(ProgramEntry.IsProtectedFolder(Environment.GetFolderPath(Environment.SpecialFolder.System)));
        Assert.True(ProgramEntry.IsProtectedFolder(profile));
        Assert.True(ProgramEntry.IsProtectedFolder(Path.Combine(profile, "Downloads")));
        Assert.True(ProgramEntry.IsProtectedFolder(Path.GetPathRoot(profile)!));
        Assert.True(ProgramEntry.IsProtectedFolder(Path.GetDirectoryName(profile)!)); // C:\Users contém o perfil
        Assert.False(ProgramEntry.IsProtectedFolder(Path.Combine(_temp, "origem", "hwinfo")));
    }

    [Fact]
    public void Validate_CopyFolderOfSystemExe_IsRefused_ButSingleExeIsAllowed()
    {
        var notepad = new ProgramEntry
        {
            Id = "notepad",
            Name = "Bloco de notas",
            SourceExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"),
        };
        Assert.True(File.Exists(notepad.SourceExe));

        Assert.Contains(ProgramEntry.Validate([notepad]), e => e.Contains("arquivos demais"));
        Assert.Empty(ProgramEntry.Validate([notepad with { CopyFolder = false }]));
    }

    // ---- manifesto ----

    [Fact]
    public void Manifest_HasRelativeExePathsAndFlags()
    {
        var a = FakeProgram("HWiNFO");
        var b = FakeProgram("CPU-Z", folder: "cpuz", exe: "cpuz_x64.exe") with { ShowOnDesktop = false, Arguments = "-silent" };

        using var doc = JsonDocument.Parse(ProgramStaging.BuildManifest([a, b]));
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var list = root.GetProperty("programs").EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("hwinfo", list[0].GetProperty("id").GetString());
        Assert.Equal(@"Programas\hwinfo\HWiNFO64.exe", list[0].GetProperty("exe").GetString());
        Assert.Equal("Diagnóstico", list[0].GetProperty("category").GetString());
        Assert.True(list[0].GetProperty("desktop").GetBoolean());
        Assert.False(list[1].GetProperty("desktop").GetBoolean());
        Assert.True(list[1].GetProperty("startMenu").GetBoolean());
        Assert.Equal("-silent", list[1].GetProperty("arguments").GetString());
    }

    [Fact]
    public void Manifest_KeepsAccentsReadable()
    {
        Assert.Contains("Diagnóstico", ProgramStaging.BuildManifest([FakeProgram("X")]));
    }

    // ---- cópia ----

    [Fact]
    public void Stage_CopiesWholeFolder_OrOnlyExe_AndWritesManifest()
    {
        var whole = FakeProgram("Inteiro");
        var single = FakeProgram("Solto", folder: "solto", exe: "solto.exe", copyFolder: false);
        var root = Path.Combine(_temp, "destino");

        ProgramStaging.Stage([whole, single], root);

        Assert.True(File.Exists(Path.Combine(root, "Programas", "inteiro", "HWiNFO64.exe")));
        Assert.True(File.Exists(Path.Combine(root, "Programas", "inteiro", "dados", "config.ini")));
        Assert.True(File.Exists(Path.Combine(root, "Programas", "solto", "solto.exe")));
        Assert.False(File.Exists(Path.Combine(root, "Programas", "solto", "dados", "config.ini")));
        Assert.True(File.Exists(Path.Combine(root, "programs.json")));
        Assert.False(File.ReadAllBytes(Path.Combine(root, "programs.json")).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })); // sem BOM
    }

    [Fact]
    public void Stage_Empty_WritesNothing()
    {
        var root = Path.Combine(_temp, "vazio");
        ProgramStaging.Stage([], root);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void TotalBytes_CountsFolderOrSingleFile()
    {
        var whole = FakeProgram("Inteiro");
        var single = whole with { Id = "so-exe", CopyFolder = false };
        Assert.Equal(2, ProgramStaging.TotalBytes([whole])); // exe (1 byte) + config.ini (1 byte)
        Assert.Equal(1, ProgramStaging.TotalBytes([single]));
    }

    // ---- integração com o build ----

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

    private BuildOptions Options(IReadOnlyList<ProgramEntry> programs, FilesMode mode = FilesMode.None, string? folder = null) => new()
    {
        WorkDirectory = Work,
        OutputIsoPath = Path.Combine(_temp, "o.iso"),
        Programs = programs,
        FilesMode = mode,
        FilesFolder = folder,
    };

    private static readonly IProgress<BuildProgress> NoProgress = new Progress<BuildProgress>(_ => { });

    [Fact]
    public void EffectiveFilesMode_ProgramsAloneImplyOnIso()
    {
        var p = FakeProgram("X");
        Assert.Equal(FilesMode.None, Options([]).EffectiveFilesMode);
        Assert.Equal(FilesMode.OnIso, Options([p]).EffectiveFilesMode);
        Assert.Equal(FilesMode.InsideImage, Options([p], FilesMode.InsideImage, _temp).EffectiveFilesMode);
    }

    [Fact]
    public async Task Build_ProgramsOnly_GoToIsoMediaWithManifestMarkerAndStartnet()
    {
        var service = new WinPeBuildService(Runner(), FakeLocator(), WorkRoot);

        await service.BuildAsync(Options([FakeProgram("HWiNFO")]), NoProgress, _ => { }, CancellationToken.None);

        var files = Path.Combine(Work, "media", "Arquivos");
        Assert.True(File.Exists(Path.Combine(files, "Programas", "hwinfo", "HWiNFO64.exe")));
        Assert.True(File.Exists(Path.Combine(files, "programs.json")));
        Assert.True(File.Exists(Path.Combine(files, StartnetScript.MarkerFileName)));
        var startnet = await File.ReadAllTextAsync(Path.Combine(Work, "mount", "Windows", "System32", "startnet.cmd"));
        Assert.Contains("for %%d in", startnet); // procura a mídia (OnIso)
    }

    [Fact]
    public async Task Build_ProgramsPlusUserFolder_BothEndUpTogether()
    {
        var folder = Path.Combine(_temp, "meus");
        Touch(Path.Combine(folder, "nota.txt"));
        var service = new WinPeBuildService(Runner(), FakeLocator(), WorkRoot);

        await service.BuildAsync(Options([FakeProgram("HWiNFO")], FilesMode.OnIso, folder), NoProgress, _ => { }, CancellationToken.None);

        var files = Path.Combine(Work, "media", "Arquivos");
        Assert.True(File.Exists(Path.Combine(files, "nota.txt")));
        Assert.True(File.Exists(Path.Combine(files, "Programas", "hwinfo", "HWiNFO64.exe")));
    }

    [Fact]
    public async Task Build_ProgramsInsideImage_GoToMount()
    {
        var folder = Path.Combine(_temp, "meus");
        Touch(Path.Combine(folder, "nota.txt"));
        var service = new WinPeBuildService(Runner(), FakeLocator(), WorkRoot);

        await service.BuildAsync(Options([FakeProgram("HWiNFO")], FilesMode.InsideImage, folder), NoProgress, _ => { }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(Work, "mount", "Arquivos", "Programas", "hwinfo", "HWiNFO64.exe")));
        Assert.True(File.Exists(Path.Combine(Work, "mount", "Arquivos", "programs.json")));
        Assert.False(Directory.Exists(Path.Combine(Work, "media", "Arquivos")));
    }

    [Fact]
    public async Task Build_InvalidProgram_IsRefusedBeforeAnythingRuns()
    {
        var runner = Runner();
        var service = new WinPeBuildService(runner, FakeLocator(), WorkRoot);
        var broken = FakeProgram("Quebrado") with { SourceExe = Path.Combine(_temp, "sumiu.exe") };

        var ex = await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options([broken]), NoProgress, _ => { }, CancellationToken.None));

        Assert.Contains("não existe mais", ex.Message);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Build_ProgramFolderInsideWorkDirectory_IsRefused()
    {
        var inside = Path.Combine(Work, "media", "x.exe");
        Touch(inside);
        var program = new ProgramEntry { Id = "x", Name = "X", SourceExe = inside };
        var service = new WinPeBuildService(Runner(), FakeLocator(), WorkRoot);

        await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(Options([program]), NoProgress, _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task Build_ProgramsInsideImage_TooBig_IsRefused()
    {
        var program = FakeProgram("Gigante");
        await using (var fs = new FileStream(Path.Combine(program.SourceFolder, "enorme.bin"), FileMode.Create))
        {
            fs.SetLength(WinPeBuildService.MaxInsideImageBytes + 1);
        }

        var service = new WinPeBuildService(Runner(), FakeLocator(), WorkRoot);
        var options = Options([program], FilesMode.InsideImage, _temp) with { FilesFolder = null };

        // FilesFolder nulo com FilesMode InsideImage reprova na validação básica; usa pasta pequena válida:
        var small = Path.Combine(_temp, "pequena");
        Touch(Path.Combine(small, "a.txt"));
        options = options with { FilesFolder = small };

        var ex = await Assert.ThrowsAsync<BuildException>(() =>
            service.BuildAsync(options, NoProgress, _ => { }, CancellationToken.None));
        Assert.Contains("na ISO", ex.Message);
    }
}
