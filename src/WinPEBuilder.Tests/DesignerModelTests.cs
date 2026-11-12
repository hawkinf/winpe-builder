using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>Editor visual: grade, mover/trocar, pastas, adicionar/remover, gravar/descartar e o layout até o launcher.</summary>
public sealed class DesignerModelTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-designer-" + Guid.NewGuid().ToString("N"));

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

    private ProgramEntry P(string id, bool desktop = true, bool menu = true, string folder = "", string category = "Utilitários")
    {
        var exe = Path.Combine(_temp, "origem", id, id + ".exe");
        Touch(exe);
        return new ProgramEntry
        {
            Id = id,
            Name = id,
            SourceExe = exe,
            Category = category,
            ShowOnDesktop = desktop,
            ShowInStartMenu = menu,
            DesktopFolder = folder,
        };
    }

    private static DesktopSlot Slot(int c, int r) => new(c, r);

    // ---- grade / normalização ----

    [Fact]
    public void Items_FoldersFirstThenPrograms_ProgramsInsideFolderAreNotLoose()
    {
        var items = DesktopLayoutHelper.Items([P("zeta"), P("alfa", desktop: false, folder: "Disco"), P("beta", folder: "Disco"), P("gama")]);

        Assert.Equal(["f:DISCO", "p:beta", "p:gama", "p:zeta"], items.Select(i => i.Key));
        Assert.Equal(["alfa", "beta"], items[0].Members.Select(m => m.Id)); // beta também é solto (marcou desktop)
    }

    [Fact]
    public void Normalize_FillsColumnByColumnTopToBottom()
    {
        var programs = Enumerable.Range(0, 11).Select(i => P($"p{i:00}")).ToList();

        var layout = DesktopLayoutHelper.Normalize(DesktopLayoutHelper.Items(programs), null);

        Assert.Equal(Slot(0, 0), layout["p:p00"]);
        Assert.Equal(Slot(0, DesktopLayoutHelper.Rows - 1), layout["p:p08"]);
        Assert.Equal(Slot(1, 0), layout["p:p09"]); // acabou a coluna 0
    }

    [Fact]
    public void Normalize_KeepsGoodSlots_DropsStaleOutOfBoundsAndDuplicates()
    {
        var programs = new[] { P("a"), P("b"), P("c"), P("d") };
        var saved = new Dictionary<string, DesktopSlot>
        {
            ["p:a"] = Slot(5, 5),
            ["p:b"] = Slot(5, 5),                    // mesma célula de a
            ["p:c"] = Slot(99, 0),                   // fora da grade
            ["p:velho"] = Slot(1, 1),                // programa que não existe mais
        };

        var layout = DesktopLayoutHelper.Normalize(DesktopLayoutHelper.Items(programs), saved);

        Assert.Equal(Slot(5, 5), layout["p:a"]);
        Assert.False(layout.ContainsKey("p:velho"));
        Assert.Equal(4, layout.Count);
        Assert.Equal(4, layout.Values.Distinct().Count()); // nenhuma célula repetida
        Assert.All(layout.Values, s => Assert.True(DesktopLayoutHelper.InBounds(s)));
    }

    [Fact]
    public void SlotAt_ClampsToGrid()
    {
        Assert.Equal(Slot(0, 0), DesktopLayoutHelper.SlotAt(-50, -3));
        Assert.Equal(Slot(1, 2), DesktopLayoutHelper.SlotAt(85, 170));
        Assert.Equal(Slot(DesktopLayoutHelper.Columns - 1, DesktopLayoutHelper.Rows - 1), DesktopLayoutHelper.SlotAt(99999, 99999));
    }

    // ---- mover / trocar ----

    [Fact]
    public void MoveTo_FreeCell_Moves_OccupiedCell_Swaps()
    {
        var m = new DesignerModel([P("a"), P("b")], null);
        var a0 = m.Layout["p:a"];
        var b0 = m.Layout["p:b"];

        Assert.True(m.MoveTo("p:a", Slot(7, 3)));
        Assert.Equal(Slot(7, 3), m.Layout["p:a"]);

        Assert.True(m.MoveTo("p:b", Slot(7, 3))); // ocupada por a: trocam
        Assert.Equal(Slot(7, 3), m.Layout["p:b"]);
        Assert.Equal(b0, m.Layout["p:a"]);
        Assert.NotEqual(a0, m.Layout["p:a"]);
        Assert.True(m.Dirty);
    }

    [Fact]
    public void MoveTo_SameCellOrUnknownKey_DoesNothing()
    {
        var m = new DesignerModel([P("a")], null);

        Assert.False(m.MoveTo("p:a", m.Layout["p:a"]));
        Assert.False(m.MoveTo("p:inexistente", Slot(1, 1)));
        Assert.False(m.Dirty);
    }

    [Fact]
    public void MoveTo_ClampsOutsideGrid()
    {
        var m = new DesignerModel([P("a")], null);

        m.MoveTo("p:a", Slot(500, -4));

        Assert.Equal(Slot(DesktopLayoutHelper.Columns - 1, 0), m.Layout["p:a"]);
    }

    [Fact]
    public void AutoArrange_ResetsManualPositions()
    {
        var m = new DesignerModel([P("a"), P("b")], null);
        m.MoveTo("p:a", Slot(9, 8));

        m.AutoArrange();

        Assert.Equal(Slot(0, 0), m.Layout["p:a"]);
        Assert.Equal(Slot(0, 1), m.Layout["p:b"]);
    }

    // ---- pastas ----

    [Fact]
    public void MoveIntoFolder_ProgramLeavesDesktopButStaysInMenu()
    {
        var m = new DesignerModel([P("disco1", folder: "Disco", desktop: false), P("solto")], null);

        Assert.True(m.MoveIntoFolder("solto", "f:DISCO"));

        var solto = m.Find("solto")!;
        Assert.Equal("Disco", solto.DesktopFolder);
        Assert.False(solto.ShowOnDesktop);
        Assert.True(solto.ShowInStartMenu);
        Assert.False(m.Layout.ContainsKey("p:solto"));
        Assert.Equal(["f:DISCO"], m.Layout.Keys);
    }

    [Fact]
    public void MoveIntoFolder_RefusesNonFolderTargets()
    {
        var m = new DesignerModel([P("a"), P("b")], null);

        Assert.False(m.MoveIntoFolder("a", "p:b"));
        Assert.False(m.MoveIntoFolder("a", "f:NAOEXISTE"));
        Assert.False(m.MoveIntoFolder("naoexiste", "f:X"));
    }

    [Fact]
    public void TakeOutOfFolder_BecomesLooseIconAtRequestedCell()
    {
        var m = new DesignerModel([P("a", folder: "F", desktop: false)], null);

        Assert.True(m.TakeOutOfFolder("a", Slot(4, 4)));

        var a = m.Find("a")!;
        Assert.Equal(string.Empty, a.DesktopFolder);
        Assert.True(a.ShowOnDesktop);
        Assert.Equal(Slot(4, 4), m.Layout["p:a"]);
        Assert.False(m.Layout.ContainsKey("f:F")); // pasta vazia some
    }

    [Fact]
    public void RenameFolder_KeepsPositionAndRefusesBadNames()
    {
        var m = new DesignerModel([P("a", folder: "Velha", desktop: false)], null);
        m.MoveTo("f:VELHA", Slot(6, 2));

        Assert.True(m.RenameFolder("f:VELHA", "Nova"));

        Assert.Equal("Nova", m.Find("a")!.DesktopFolder);
        Assert.Equal(Slot(6, 2), m.Layout["f:NOVA"]);
        Assert.False(m.Layout.ContainsKey("f:VELHA"));
        Assert.False(m.RenameFolder("f:NOVA", "  "));
        Assert.False(m.RenameFolder("f:NOVA", new string('x', 41)));
    }

    [Fact]
    public void DissolveFolder_MembersBecomeLooseIcons()
    {
        var m = new DesignerModel([P("a", folder: "F", desktop: false), P("b", folder: "F", desktop: false)], null);

        m.DissolveFolder("f:F");

        Assert.All(m.Programs, p => Assert.True(p.ShowOnDesktop && p.DesktopFolder.Length == 0));
        Assert.Equal(["p:a", "p:b"], m.Layout.Keys.OrderBy(k => k));
    }

    // ---- adicionar / editar / remover ----

    [Fact]
    public void Add_PlacesNewIconAtDropCell_AndDisplacedIconGetsAFreeCell()
    {
        var m = new DesignerModel([P("a")], null);
        var cell = m.Layout["p:a"]; // 0,0

        m.Add([P("novo")], cell);

        Assert.Equal(cell, m.Layout["p:novo"]);
        Assert.NotEqual(cell, m.Layout["p:a"]);
        Assert.Equal(2, m.Layout.Values.Distinct().Count());
    }

    [Fact]
    public void Add_DuplicateId_Throws()
    {
        var m = new DesignerModel([P("a")], null);

        Assert.Throws<InvalidOperationException>(() => m.Add([P("a")]));
    }

    [Fact]
    public void Add_MenuOnlyProgram_HasNoDesktopIcon()
    {
        var m = new DesignerModel([], null);

        m.Add([P("so-menu", desktop: false)], Slot(2, 2));

        Assert.Empty(m.Layout);
        Assert.Single(m.StartMenu);
    }

    [Fact]
    public void ShowOnDesktopAt_PutsMenuOnlyProgramOnDesktop()
    {
        var m = new DesignerModel([P("so-menu", desktop: false)], null);

        Assert.True(m.ShowOnDesktopAt("so-menu", Slot(3, 1)));

        Assert.True(m.Find("so-menu")!.ShowOnDesktop);
        Assert.Equal(Slot(3, 1), m.Layout["p:so-menu"]);
    }

    [Fact]
    public void Replace_KeepsPosition_AndDirtyOnlyWhenChanged()
    {
        var m = new DesignerModel([P("a")], null);
        m.MoveTo("p:a", Slot(5, 5));
        var original = m.Find("a")!;
        var clean = new DesignerModel([original], m.Layout);

        clean.Replace(original);
        Assert.False(clean.Dirty);

        m.Replace(original with { Name = "Renomeado" });
        Assert.Equal(Slot(5, 5), m.Layout["p:a"]);
        Assert.True(m.Dirty);
    }

    [Fact]
    public void Remove_ItemAndFolder()
    {
        var m = new DesignerModel([P("a"), P("b", folder: "F", desktop: false), P("c", folder: "F", desktop: false)], null);

        m.RemoveItem("p:a");
        Assert.Null(m.Find("a"));

        m.RemoveItem("f:F"); // remove a pasta e os programas dela
        Assert.Empty(m.Programs);
        Assert.Empty(m.Layout);
        Assert.False(m.Remove("a")); // já foi
    }

    // ---- gravar / descartar ----

    [Fact]
    public void Discard_LeavesOriginalsUntouched_BecauseModelWorksOnACopy()
    {
        var original = new List<ProgramEntry> { P("a"), P("b") };
        var originalLayout = new Dictionary<string, DesktopSlot> { ["p:a"] = Slot(1, 1), ["p:b"] = Slot(2, 2) };

        var m = new DesignerModel(original, originalLayout);
        m.RemoveItem("p:a");
        m.MoveTo("p:b", Slot(9, 9));
        m.Add([P("novo")]);

        Assert.Equal(["a", "b"], original.Select(p => p.Id));
        Assert.Equal(Slot(2, 2), originalLayout["p:b"]);
        Assert.Equal(2, originalLayout.Count);
    }

    // ---- manifesto / launcher ----

    [Fact]
    public void Manifest_WithLayout_RoundTripsToLauncherPixelPositions()
    {
        var programs = new[] { P("a", folder: "Ferramentas", desktop: false), P("b") };
        var m = new DesignerModel(programs, null);
        m.MoveTo("f:FERRAMENTAS", Slot(3, 2));
        m.MoveTo("p:b", Slot(0, 4));

        var manifest = ManifestReader.Parse(ProgramStaging.BuildManifest(m.Programs, m.Layout));

        Assert.Equal(2, manifest.Layout.Count);
        var keys = new[] { ManifestReader.FolderKey("Ferramentas"), ManifestReader.ProgramKey(manifest.Programs.Single(p => p.Id == "b")) };
        var pixels = ManifestReader.PixelPositions(manifest.Layout, keys)!;
        Assert.Equal(new System.Drawing.Point(3 * 80, 2 * 80), pixels[keys[0]]);
        Assert.Equal(new System.Drawing.Point(0, 4 * 80), pixels[keys[1]]);
        Assert.Equal(DesktopLayoutHelper.CellSize, ManifestReader.CellSize);
    }

    [Fact]
    public void Manifest_WithoutLayout_OmitsItAndLauncherFallsBackToAuto()
    {
        var manifest = ManifestReader.Parse(ProgramStaging.BuildManifest([P("a")]));

        Assert.Empty(manifest.Layout);
        Assert.Null(ManifestReader.PixelPositions(manifest.Layout, ["p:a"]));
    }

    [Fact]
    public void PixelPositions_MissingOneKey_ReturnsNull()
    {
        var layout = new List<LayoutEntry> { new() { Key = "p:a", Col = 1, Row = 1 } };

        Assert.Null(ManifestReader.PixelPositions(layout, ["p:a", "p:b"]));
    }

    [Fact]
    public void Parse_DropsInvalidLayoutEntries()
    {
        const string json = """{"version":1,"programs":[],"layout":[{"key":"p:a","col":1,"row":1},{"key":"","col":0,"row":0},{"key":"p:b","col":-1,"row":0}]}""";

        Assert.Single(ManifestReader.Parse(json).Layout);
    }

    [Fact]
    public void FolderKeys_AgreeBetweenCoreAndLauncher()
    {
        foreach (var name in new[] { "Ferramentas", "  disco  ", "Diagnóstico", "ÁREA" })
        {
            Assert.Equal(DesktopLayoutHelper.FolderKey(name), ManifestReader.FolderKey(name));
        }

        var program = P("xyz");
        Assert.Equal(DesktopLayoutHelper.ProgramKey(program), ManifestReader.ProgramKey(new ProgramItem { Id = "xyz" }));
    }

    // ---- start menu ----

    [Fact]
    public void StartMenuModel_MatchesLauncherGrouping()
    {
        var programs = new[]
        {
            P("zeta", category: "Rede"), P("beta", category: "Discos"), P("alfa", category: "discos"), P("oculto", menu: false),
        };

        var groups = StartMenuModel.Build(programs);
        var launcher = ManifestReader.GroupForStartMenu(ManifestReader.Parse(ProgramStaging.BuildManifest(programs)).Programs);

        Assert.Equal(launcher.Select(g => g.Key), groups.Select(g => g.Category));
        Assert.Equal(launcher.SelectMany(g => g.Value).Select(p => p.Id), groups.SelectMany(g => g.Programs).Select(p => p.Id));
    }

    // ---- persistência e build ----

    [Fact]
    public void Settings_PersistLayout()
    {
        var path = Path.Combine(_temp, "s.json");
        var layout = new Dictionary<string, DesktopSlot> { ["p:a"] = Slot(4, 6), ["f:X"] = Slot(0, 1) };

        Assert.True(new UserSettings { DesktopLayout = layout }.TrySave(path));
        var back = UserSettings.Load(path).DesktopLayout;

        Assert.Equal(layout.OrderBy(k => k.Key), back.OrderBy(k => k.Key));
    }

    [Fact]
    public void Settings_OldFileWithoutLayout_LoadsEmpty()
    {
        var path = Path.Combine(_temp, "s.json");
        Directory.CreateDirectory(_temp);
        File.WriteAllText(path, "{ \"VolumeLabel\": \"X\" }");

        Assert.Empty(UserSettings.Load(path).DesktopLayout);
    }

    [Fact]
    public async Task Build_WritesLayoutIntoManifestOnTheMedia()
    {
        var work = Path.Combine(_temp, "app", "work", "amd64");
        var kit = new AdkInstall(Path.Combine(_temp, "kit"));
        Touch(kit.DismExe);
        Touch(kit.OscdimgExe);
        Touch(kit.CopypeCmd);
        Touch(kit.WinPeWim(WinPeArchitecture.X64));
        var oscdimgDir = Path.Combine(kit.DeploymentToolsRoot, "amd64", "Oscdimg");
        Touch(Path.Combine(oscdimgDir, "etfsboot.com"));
        Touch(Path.Combine(oscdimgDir, "efisys_noprompt.bin"));
        Directory.CreateDirectory(kit.OptionalComponentsDirectory(WinPeArchitecture.X64));
        var runner = new ScriptedRunner(c =>
        {
            if (c.Arguments.Contains("copype.cmd"))
            {
                Touch(Path.Combine(work, "media", "sources", "boot.wim"));
                Directory.CreateDirectory(Path.Combine(work, "mount"));
            }

            if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
            {
                Touch(Path.Combine(_temp, "o.iso"));
            }

            return (0, []);
        });
        var program = P("a");
        var options = new BuildOptions
        {
            WorkDirectory = work,
            OutputIsoPath = Path.Combine(_temp, "o.iso"),
            Programs = [program],
            DesktopLayout = new Dictionary<string, DesktopSlot> { ["p:a"] = Slot(7, 5) },
        };

        await new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), Path.Combine(_temp, "app"))
            .BuildAsync(options, new Progress<BuildProgress>(_ => { }), _ => { }, CancellationToken.None);

        var manifest = ManifestReader.Load(Path.Combine(work, "media", "Arquivos", "programs.json"));
        var entry = Assert.Single(manifest.Layout);
        Assert.Equal(("p:a", 7, 5), (entry.Key, entry.Col, entry.Row));
    }
}
