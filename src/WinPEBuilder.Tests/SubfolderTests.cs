using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>Subpastas dentro das pastas da área de trabalho ("A/B"): caminhos, conteúdo, edição e launcher.</summary>
public sealed class SubfolderTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-sub-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private ProgramEntry P(string id, string folder = "", bool desktop = false)
    {
        var exe = Path.Combine(_temp, id + ".exe");
        Directory.CreateDirectory(_temp);
        File.WriteAllText(exe, "x");
        return new ProgramEntry { Id = id, Name = id, SourceExe = exe, DesktopFolder = folder, ShowOnDesktop = desktop };
    }

    private DesignerModel Tree() => new(
    [
        P("raiz", "Ferr"),
        P("disco1", "Ferr/Discos"),
        P("disco2", "Ferr/Discos"),
        P("part", "Ferr/Discos/Particoes"),
        P("rede", "Ferr/Rede"),
        P("solto", "", desktop: true),
    ], null);

    // ---- FolderPath ----

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("A", "A")]
    [InlineData(" A / B/ ", "A/B")]
    [InlineData("A//B", "A/B")]
    [InlineData("a/b/c/d", "a/b/c/d")]
    public void Normalize_Valid(string input, string expected) => Assert.Equal(expected, FolderPath.Normalize(input));

    [Theory]
    [InlineData("a/b/c/d/e")]                // 5 níveis
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // 41 caracteres
    [InlineData("a/b\u0001")]
    public void Normalize_Invalid_ReturnsNull(string input) => Assert.Null(FolderPath.Normalize(input));

    [Fact]
    public void Helpers_TopLeafParentDepthAndPrefix()
    {
        Assert.Equal("A", FolderPath.Top("A/B/C"));
        Assert.Equal("C", FolderPath.Leaf("A/B/C"));
        Assert.Equal("A/B", FolderPath.Parent("A/B/C"));
        Assert.Equal(string.Empty, FolderPath.Parent("A"));
        Assert.Equal(3, FolderPath.Depth("A/B/C"));
        Assert.True(FolderPath.IsSameOrInside("a/b/c", "A/B"));
        Assert.True(FolderPath.IsSameOrInside("A/B", "a/b"));
        Assert.False(FolderPath.IsSameOrInside("A/BX", "A/B"));   // prefixo de texto != pasta
        Assert.False(FolderPath.IsSameOrInside("A", "A/B"));
        Assert.Equal("N/B/C", FolderPath.Replace("A/B/C", "A", "N"));
        Assert.Equal("C", FolderPath.Replace("A/B/C", "A/B", string.Empty));
        Assert.Equal("f:A/B", FolderPath.Key(" a / b "));
    }

    // ---- itens e conteúdo ----

    [Fact]
    public void Items_OnlyTopLevelFoldersAppearOnDesktop_WithAllDescendantsAsMembers()
    {
        var items = DesktopLayoutHelper.Items(Tree().Programs);

        Assert.Equal(["f:FERR", "p:solto"], items.Select(i => i.Key));
        Assert.Equal(5, items[0].Members.Count);
    }

    [Fact]
    public void FolderContents_ShowsDirectSubfoldersAndDirectPrograms()
    {
        var model = Tree();

        var root = model.FolderContents("Ferr");
        var discos = model.FolderContents("Ferr/Discos");

        Assert.Equal(["Ferr/Discos", "Ferr/Rede"], root.Subfolders);
        Assert.Equal(["raiz"], root.Programs.Select(p => p.Id));
        Assert.Equal(["Ferr/Discos/Particoes"], discos.Subfolders);
        Assert.Equal(["disco1", "disco2"], discos.Programs.Select(p => p.Id));
    }

    [Fact]
    public void AllFolderPaths_IncludesIntermediateFolders()
    {
        var paths = new DesignerModel([P("x", "A/B/C")], null).FolderPaths;

        Assert.Equal(["A", "A/B", "A/B/C"], paths);
    }

    [Fact]
    public void FolderOnlyExistsWhileItHasPrograms()
    {
        var model = Tree();

        model.Remove("part");

        Assert.DoesNotContain("Ferr/Discos/Particoes", model.FolderPaths);
    }

    // ---- operações ----

    [Fact]
    public void Rename_Subfolder_MovesEverythingInsideIt()
    {
        var model = Tree();

        Assert.True(model.RenameFolder("f:FERR/DISCOS", "HDs"));

        Assert.Equal("Ferr/HDs", model.Find("disco1")!.DesktopFolder);
        Assert.Equal("Ferr/HDs/Particoes", model.Find("part")!.DesktopFolder);
        Assert.Equal("Ferr/Rede", model.Find("rede")!.DesktopFolder);
    }

    [Fact]
    public void Rename_TopFolder_KeepsDesktopPositionAndSubpaths()
    {
        var model = Tree();
        model.MoveTo("f:FERR", new DesktopSlot(5, 5));

        Assert.True(model.RenameFolder("f:FERR", "Ferramentas"));

        Assert.Equal(new DesktopSlot(5, 5), model.Layout["f:FERRAMENTAS"]);
        Assert.Equal("Ferramentas/Discos/Particoes", model.Find("part")!.DesktopFolder);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("  ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Rename_RefusesBadLeafNames(string name) => Assert.False(Tree().RenameFolder("f:FERR/REDE", name));

    [Fact]
    public void Dissolve_Subfolder_ItsProgramsGoUpOneLevel()
    {
        var model = Tree();

        model.DissolveFolder("f:FERR/DISCOS");

        Assert.Equal("Ferr", model.Find("disco1")!.DesktopFolder);
        Assert.Equal("Ferr/Particoes", model.Find("part")!.DesktopFolder);
    }

    [Fact]
    public void Dissolve_TopFolder_DirectProgramsBecomeLoose_SubfoldersBecomeTopLevel()
    {
        var model = Tree();

        model.DissolveFolder("f:FERR");

        var raiz = model.Find("raiz")!;
        Assert.True(raiz.ShowOnDesktop);
        Assert.Equal(string.Empty, raiz.DesktopFolder);
        Assert.Equal("Discos", model.Find("disco1")!.DesktopFolder);
        Assert.Contains("f:DISCOS", model.Layout.Keys);
        Assert.Contains("f:REDE", model.Layout.Keys);
        Assert.DoesNotContain("f:FERR", model.Layout.Keys);
    }

    [Fact]
    public void MoveUpOneLevel_FromSubfolderAndFromTopFolder()
    {
        var model = Tree();

        Assert.True(model.MoveUpOneLevel("part"));
        Assert.Equal("Ferr/Discos", model.Find("part")!.DesktopFolder);

        Assert.True(model.MoveUpOneLevel("raiz")); // de pasta de primeiro nivel: vira ícone solto
        Assert.True(model.Find("raiz")!.ShowOnDesktop);
        Assert.Equal(string.Empty, model.Find("raiz")!.DesktopFolder);

        Assert.False(model.MoveUpOneLevel("solto")); // já está solto
    }

    [Fact]
    public void MoveToFolderPath_Subfolder_AndRefusesInvalidOrSame()
    {
        var model = Tree();

        Assert.True(model.MoveToFolderPath("solto", "Ferr/Rede"));
        Assert.Equal("Ferr/Rede", model.Find("solto")!.DesktopFolder);
        Assert.True(model.Find("solto")!.ShowOnDesktop);       // continua solto (não mexe nisso)
        Assert.False(model.MoveToFolderPath("solto", "Ferr/Rede")); // mesma pasta
        Assert.False(model.MoveToFolderPath("solto", "a/b/c/d/e")); // inválida
    }

    [Fact]
    public void MoveIntoFolder_WorksForSubfolderKeys()
    {
        var model = Tree();

        Assert.True(model.MoveIntoFolder("solto", "f:FERR/REDE"));

        var solto = model.Find("solto")!;
        Assert.Equal("Ferr/Rede", solto.DesktopFolder);
        Assert.False(solto.ShowOnDesktop);
    }

    [Fact]
    public void MoveFolder_Nests_AndCanComeBackToDesktop()
    {
        var model = new DesignerModel([P("a", "Alfa"), P("b", "Beta"), P("b2", "Beta/Sub")], null);

        Assert.True(model.MoveFolder("f:BETA", "f:ALFA"));
        Assert.Equal("Alfa/Beta", model.Find("b")!.DesktopFolder);
        Assert.Equal("Alfa/Beta/Sub", model.Find("b2")!.DesktopFolder);
        Assert.DoesNotContain("f:BETA", model.Layout.Keys);

        Assert.True(model.MoveFolder("f:ALFA/BETA", null)); // volta para a área de trabalho
        Assert.Equal("Beta", model.Find("b")!.DesktopFolder);
        Assert.Equal("Beta/Sub", model.Find("b2")!.DesktopFolder);
        Assert.Contains("f:BETA", model.Layout.Keys);
    }

    [Fact]
    public void MoveFolder_RefusesCyclesSameTargetAndDepthOverflow()
    {
        var model = new DesignerModel([P("a", "A/B"), P("deep", "X/Y/Z/W")], null);

        Assert.False(model.MoveFolder("f:A", "f:A/B"));      // para dentro de si mesma
        Assert.False(model.MoveFolder("f:A", "f:A"));
        Assert.False(model.MoveFolder("f:A/B", "f:A"));      // já está lá
        Assert.False(model.MoveFolder("f:A", "f:X/Y/Z/W"));  // passaria de 4 níveis
        Assert.False(model.MoveFolder("f:NAOEXISTE", null));
    }

    [Fact]
    public void RemoveFolder_RemovesProgramsInAllLevels()
    {
        var model = Tree();

        model.RemoveItem("f:FERR/DISCOS"); // subpasta (sem ícone próprio): remove Discos e Particoes

        Assert.Null(model.Find("disco1"));
        Assert.Null(model.Find("part"));
        Assert.NotNull(model.Find("rede"));
        Assert.NotNull(model.Find("raiz"));
    }

    [Fact]
    public void RemoveItem_TopFolder_RemovesEverythingInside()
    {
        var model = Tree();

        model.RemoveItem("f:FERR");

        Assert.Equal(["solto"], model.Programs.Select(p => p.Id));
    }

    [Fact]
    public void Discard_LeavesOriginalProgramsUntouched()
    {
        var original = new List<ProgramEntry> { P("a", "A/B") };

        var model = new DesignerModel(original, null);
        model.RenameFolder("f:A/B", "Z");
        model.DissolveFolder("f:A");

        Assert.Equal("A/B", original[0].DesktopFolder);
    }

    // ---- validação ----

    [Fact]
    public void Validate_FolderPath_DepthAndSegmentLimits()
    {
        Assert.Empty(ProgramEntry.Validate([P("ok", "A/B/C/D")]));
        Assert.NotEmpty(ProgramEntry.Validate([P("fundo", "A/B/C/D/E")]));
        Assert.NotEmpty(ProgramEntry.Validate([P("longo", new string('x', 41))]));
    }

    // ---- manifesto / launcher ----

    [Fact]
    public void Manifest_CarriesFolderPath_AndLauncherGroupsByTopLevel()
    {
        var model = Tree();

        var items = ManifestReader.Parse(ProgramStaging.BuildManifest(model.Programs)).Programs;
        var top = ManifestReader.DesktopFolders(items);

        var folder = Assert.Single(top);
        Assert.Equal("Ferr", folder.Key);
        Assert.Equal(5, folder.Value.Count);
        Assert.Equal("Ferr/Discos/Particoes", items.Single(i => i.Id == "part").Folder);
    }

    [Fact]
    public void Launcher_FolderContents_MatchesCore()
    {
        var model = Tree();
        var items = ManifestReader.Parse(ProgramStaging.BuildManifest(model.Programs)).Programs;

        foreach (var path in model.FolderPaths)
        {
            var core = model.FolderContents(path);
            var launcher = ManifestReader.FolderContents(items, path);

            Assert.Equal(core.Subfolders, launcher.Subfolders);
            Assert.Equal(core.Programs.Select(p => p.Id), launcher.Programs.Select(p => p.Id));
        }
    }

    [Fact]
    public void FolderKeys_AgreeBetweenCoreAndLauncher()
    {
        foreach (var path in new[] { "Ferr", " ferr / Discos ", "A/B/C", "Áreas/Teste" })
        {
            Assert.Equal(FolderPath.Key(path), ManifestReader.FolderKey(path));
        }

        Assert.True(ManifestReader.FolderContains("a/b", "A/B/c"));
        Assert.False(ManifestReader.FolderContains("a/b", "A/BX"));
        Assert.Equal("A/B", ManifestReader.FolderParent("A/B/C"));
    }

    [Fact]
    public void Launcher_ManifestWithOldFlatFolder_StillWorks()
    {
        const string json = """{"version":1,"programs":[{"id":"a","name":"A","exe":"x.exe","folder":"Ferramentas","desktop":false}]}""";

        var items = ManifestReader.Parse(json).Programs;

        Assert.Equal("Ferramentas", Assert.Single(ManifestReader.DesktopFolders(items)).Key);
        Assert.Equal(["a"], ManifestReader.FolderContents(items, "Ferramentas").Programs.Select(p => p.Id));
    }
}
