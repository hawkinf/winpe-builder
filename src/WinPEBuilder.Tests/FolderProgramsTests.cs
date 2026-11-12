using WinPEBuilder.Core;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>"Adicionar pasta": vários .exe da mesma pasta, cópia única, pasta na área de trabalho.</summary>
public sealed class FolderProgramsTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-folder-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>Pasta "Ferramentas" com 2 exes na raiz, 1 em subpasta e lixo (desinstalador, dll).</summary>
    private string MakeToolsFolder()
    {
        var root = Path.Combine(_temp, "Ferramentas");
        Touch(Path.Combine(root, "alfa.exe"));
        Touch(Path.Combine(root, "beta.exe"));
        Touch(Path.Combine(root, "x64", "gama.exe"));
        Touch(Path.Combine(root, "unins000.exe"));
        Touch(Path.Combine(root, "VC_redist.x64.exe"));
        Touch(Path.Combine(root, "lib.dll"));
        Touch(Path.Combine(root, "dados", "config.ini"));
        return root;
    }

    private static ProgramEntry Entry(string root, string relative, string id, string folder = "", bool desktop = false, bool menu = true) => new()
    {
        Id = id,
        Name = id,
        SourceExe = Path.Combine(root, relative),
        SourceRoot = root,
        CopyFolder = true,
        DesktopFolder = folder,
        ShowOnDesktop = desktop,
        ShowInStartMenu = menu,
    };

    // ---- scanner ----

    [Fact]
    public void Scanner_FindsExesInSubfoldersAndSkipsNoise()
    {
        var root = MakeToolsFolder();

        var result = ProgramScanner.FindExecutables(root);

        Assert.False(result.Truncated);
        var expected = new[] { "alfa.exe", Path.Combine("x64", "gama.exe"), "beta.exe" }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected, result.Files.Select(f => Path.GetRelativePath(root, f)));
    }

    [Fact]
    public void Scanner_RespectsDepthLimit()
    {
        var root = Path.Combine(_temp, "fundo");
        Touch(Path.Combine(root, "a.exe"));
        Touch(Path.Combine(root, "n1", "n2", "n3", "n4", "fundo.exe"));

        Assert.Single(ProgramScanner.FindExecutables(root, maxDepth: 3).Files);
        Assert.Equal(2, ProgramScanner.FindExecutables(root, maxDepth: 4).Files.Count);
        Assert.Single(ProgramScanner.FindExecutables(root, maxDepth: 0).Files);
    }

    [Fact]
    public void Scanner_TruncatesAtLimit()
    {
        var root = Path.Combine(_temp, "muitos");
        for (var i = 0; i < 12; i++)
        {
            Touch(Path.Combine(root, $"p{i:00}.exe"));
        }

        var result = ProgramScanner.FindExecutables(root, maxResults: 5);

        Assert.Equal(5, result.Files.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Scanner_MissingFolder_Throws() =>
        Assert.Throws<DirectoryNotFoundException>(() => ProgramScanner.FindExecutables(Path.Combine(_temp, "nao-existe")));

    // ---- cópia única + manifesto ----

    [Fact]
    public void SharedFolder_IsCopiedOnceAndEveryManifestPathPointsToARealFile()
    {
        var root = MakeToolsFolder();
        var programs = new[]
        {
            Entry(root, "alfa.exe", "alfa", folder: "Ferramentas"),
            Entry(root, "beta.exe", "beta", folder: "Ferramentas", desktop: true),
            Entry(root, Path.Combine("x64", "gama.exe"), "gama"),
        };
        var files = Path.Combine(_temp, "saida", "Arquivos");

        ProgramStaging.Stage(programs, files);

        Assert.Equal(["alfa"], Directory.GetDirectories(Path.Combine(files, "Programas")).Select(Path.GetFileName)); // 1 cópia só
        Assert.True(File.Exists(Path.Combine(files, "Programas", "alfa", "dados", "config.ini")));
        var manifest = ManifestReader.Parse(File.ReadAllText(Path.Combine(files, "programs.json")));
        Assert.Equal(3, manifest.Programs.Count);
        foreach (var item in manifest.Programs)
        {
            var resolved = ManifestReader.ResolveExe(files, item);
            Assert.NotNull(resolved);
            Assert.True(File.Exists(resolved), $"{item.Name}: {resolved}");
        }

        Assert.Equal(Path.Combine("Programas", "alfa", "x64", "gama.exe"), manifest.Programs.Single(p => p.Id == "gama").Exe);
    }

    [Fact]
    public void TotalBytes_CountsSharedFolderOnce()
    {
        var root = MakeToolsFolder();
        var one = Entry(root, "alfa.exe", "alfa");
        var two = Entry(root, "beta.exe", "beta");

        Assert.Equal(ProgramStaging.TotalBytes([one]), ProgramStaging.TotalBytes([one, two]));
    }

    [Fact]
    public void StageFolderName_SeparateFoldersKeepTheirOwnIds_AndNoCopyFolderUsesOwnId()
    {
        var a = Entry(MakeToolsFolder(), "alfa.exe", "alfa");
        var otherRoot = Path.Combine(_temp, "Outra");
        Touch(Path.Combine(otherRoot, "z.exe"));
        var b = Entry(otherRoot, "z.exe", "zeta");
        var single = a with { Id = "so-exe", CopyFolder = false };
        var all = new[] { a, b, single };

        Assert.Equal("alfa", ProgramStaging.StageFolderName(all, a));
        Assert.Equal("zeta", ProgramStaging.StageFolderName(all, b));
        Assert.Equal("so-exe", ProgramStaging.StageFolderName(all, single));
        Assert.Equal(Path.Combine("Programas", "so-exe", "alfa.exe"), ProgramStaging.RelativeExe(all, single));
    }

    // ---- pasta na área de trabalho (manifesto -> launcher) ----

    [Fact]
    public void DesktopFolder_FlowsFromEntryToLauncherGrouping()
    {
        var root = MakeToolsFolder();
        var programs = new[]
        {
            Entry(root, "beta.exe", "beta", folder: "Ferramentas", menu: false),
            Entry(root, "alfa.exe", "alfa", folder: "ferramentas"), // mesma pasta, caixa diferente
            Entry(root, Path.Combine("x64", "gama.exe"), "gama", desktop: true),
        };

        var items = ManifestReader.Parse(ProgramStaging.BuildManifest(programs)).Programs;
        var folders = ManifestReader.DesktopFolders(items);

        var folder = Assert.Single(folders);
        Assert.Equal(["alfa", "beta"], folder.Value.Select(p => p.Name));
        Assert.Equal(["gama"], ManifestReader.ForDesktop(items).Select(p => p.Name));
        Assert.Equal(["alfa", "gama"], ManifestReader.GroupForStartMenu(items).SelectMany(g => g.Value).Select(p => p.Name));
    }

    [Fact]
    public void Parse_ManifestWithoutFolderField_StillWorks()
    {
        const string json = """{"version":1,"programs":[{"id":"a","name":"A","exe":"Programas\\a\\a.exe","desktop":true}]}""";

        var item = Assert.Single(ManifestReader.Parse(json).Programs);

        Assert.Equal(string.Empty, item.Folder);
        Assert.Empty(ManifestReader.DesktopFolders([item]));
    }

    // ---- validação ----

    [Fact]
    public void Validate_ExeOutsideRoot_IsRefused()
    {
        var root = MakeToolsFolder();
        var outside = Path.Combine(_temp, "fora.exe");
        Touch(outside);
        var entry = Entry(root, "alfa.exe", "alfa") with { SourceExe = outside };

        Assert.Contains(ProgramEntry.Validate([entry]), e => e.Contains("dentro da pasta", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RootThatContainsProtectedFolder_IsRefused()
    {
        var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!;
        var entry = new ProgramEntry
        {
            Id = "x",
            Name = "X",
            SourceExe = Path.Combine(_temp, "x.exe"),
            SourceRoot = users,
            CopyFolder = true,
        };
        Touch(entry.SourceExe);

        // exe fora da raiz já é erro; o importante é nunca aceitar uma raiz protegida
        Assert.NotEmpty(ProgramEntry.Validate([entry]));
        Assert.True(ProgramEntry.IsProtectedFolder(users));
    }

    [Fact]
    public void Validate_LongDesktopFolderName_IsRefused()
    {
        var root = MakeToolsFolder();
        var entry = Entry(root, "alfa.exe", "alfa", folder: new string('x', 41));

        Assert.Contains(ProgramEntry.Validate([entry]), e => e.Contains("pasta da área de trabalho", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_GoodFolderImport_HasNoErrors()
    {
        var root = MakeToolsFolder();
        var programs = new[] { Entry(root, "alfa.exe", "alfa", folder: "F"), Entry(root, Path.Combine("x64", "gama.exe"), "gama") };

        Assert.Empty(ProgramEntry.Validate(programs));
    }

    // ---- settings ----

    [Fact]
    public void Settings_PersistRootAndDesktopFolder()
    {
        var path = Path.Combine(_temp, "settings.json");
        var entry = Entry(MakeToolsFolder(), Path.Combine("x64", "gama.exe"), "gama", folder: "Ferramentas", desktop: true);

        Assert.True(new UserSettings { Programs = [entry] }.TrySave(path));
        var back = Assert.Single(UserSettings.Load(path).Programs);

        Assert.Equal(entry, back);
        Assert.Equal("Ferramentas", back.DesktopFolder);
        Assert.Equal(entry.SourceRoot, back.SourceRoot);
    }

    [Fact]
    public void OldProgramEntries_WithoutNewFields_BehaveAsBefore()
    {
        var dir = Path.Combine(_temp, "velho");
        Touch(Path.Combine(dir, "velho.exe"));
        var entry = new ProgramEntry { Id = "velho", Name = "Velho", SourceExe = Path.Combine(dir, "velho.exe") };

        Assert.Equal(dir, entry.SourceFolder);
        Assert.Equal(string.Empty, entry.DesktopFolder);
        Assert.Equal(Path.Combine("Programas", "velho", "velho.exe"), ProgramStaging.RelativeExe(entry));
        Assert.Empty(ProgramEntry.Validate([entry]));
    }
}
