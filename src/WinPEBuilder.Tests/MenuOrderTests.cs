using WinPEBuilder.Core;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>Ordem do menu Iniciar escolhida à mão: modelo, persistência e igualdade com o launcher.</summary>
public sealed class MenuOrderTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-menu-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private ProgramEntry P(string id, string category, bool menu = true)
    {
        var exe = Path.Combine(_temp, id + ".exe");
        Directory.CreateDirectory(_temp);
        File.WriteAllText(exe, "x");
        return new ProgramEntry { Id = id, Name = id, SourceExe = exe, Category = category, ShowInStartMenu = menu, ShowOnDesktop = false };
    }

    private static IEnumerable<string> Flat(DesignerModel m) => m.StartMenu.SelectMany(g => g.Programs.Select(p => p.Id));

    private DesignerModel Model(params ProgramEntry[] programs) => new(programs, null);

    // ---- ordem ----

    [Fact]
    public void Default_IsAlphabetical()
    {
        var groups = StartMenuModel.Build([P("zeta", "Rede"), P("alfa", "Rede"), P("beta", "Discos")]);

        Assert.Equal(["Discos", "Rede"], groups.Select(g => g.Category));
        Assert.Equal(["alfa", "zeta"], groups[1].Programs.Select(p => p.Id));
    }

    [Fact]
    public void ExplicitOrder_WinsOverAlphabetical_AndUnlistedComeLast()
    {
        var programs = new[] { P("a", "A"), P("b", "B"), P("c", "C"), P("c2", "C") };
        var order = new[] { "c:C", "p:c2", "p:c", "c:A" }; // B e "a" não estão na lista

        var groups = StartMenuModel.Build(programs, order);

        Assert.Equal(["C", "A", "B"], groups.Select(g => g.Category));
        Assert.Equal(["c2", "c"], groups[0].Programs.Select(p => p.Id));
    }

    [Fact]
    public void Flatten_RoundTripsThroughBuild()
    {
        var programs = new[] { P("a", "A"), P("b", "B"), P("b2", "B") };
        var order = new[] { "c:B", "p:b2", "p:b", "c:A", "p:a" };

        var first = StartMenuModel.Build(programs, order);
        var second = StartMenuModel.Build(programs, StartMenuModel.Flatten(first));

        Assert.Equal(first.SelectMany(g => g.Programs.Select(p => p.Id)), second.SelectMany(g => g.Programs.Select(p => p.Id)));
        Assert.Equal(first.Select(g => g.Category), second.Select(g => g.Category));
    }

    [Fact]
    public void CategoryMatching_IsCaseInsensitive()
    {
        var groups = StartMenuModel.Build([P("a", "Rede"), P("b", "rede")]);

        Assert.Single(groups);
        Assert.Equal("Rede", groups[0].Category);
    }

    // ---- subir / descer ----

    [Fact]
    public void MoveMenuEntry_Category_UpAndDown_AndStopsAtLimits()
    {
        var m = Model(P("a", "A"), P("b", "B"), P("c", "C"));

        Assert.True(m.MoveMenuEntry("c:C", -1));
        Assert.Equal(["A", "C", "B"], m.StartMenu.Select(g => g.Category));
        Assert.True(m.MoveMenuEntry("c:A", +1));
        Assert.Equal(["C", "A", "B"], m.StartMenu.Select(g => g.Category));
        Assert.False(m.MoveMenuEntry("c:C", -1));  // já é o primeiro
        Assert.False(m.MoveMenuEntry("c:B", +1));  // já é o último
        Assert.False(m.MoveMenuEntry("c:X", +1));  // não existe
        Assert.True(m.Dirty);
    }

    [Fact]
    public void MoveMenuEntry_Program_StaysInsideItsCategory()
    {
        var m = Model(P("a1", "A"), P("a2", "A"), P("b1", "B"));

        Assert.True(m.MoveMenuEntry("p:a2", -1));
        Assert.Equal(["a2", "a1"], m.StartMenu[0].Programs.Select(p => p.Id));
        Assert.False(m.MoveMenuEntry("p:a2", -1));  // topo da categoria; não vaza para outra
        Assert.False(m.MoveMenuEntry("p:b1", +1));  // único da categoria
        Assert.Equal(["b1"], m.StartMenu[1].Programs.Select(p => p.Id));
    }

    // ---- arrastar ----

    [Fact]
    public void Drop_CategoryOnCategory_GoesBeforeIt()
    {
        var m = Model(P("a", "A"), P("b", "B"), P("c", "C"));

        Assert.True(m.DropMenuEntry("c:C", "c:A"));

        Assert.Equal(["C", "A", "B"], m.StartMenu.Select(g => g.Category));
    }

    [Fact]
    public void Drop_ProgramOnCategory_ChangesCategoryAndGoesLast()
    {
        var m = Model(P("a1", "A"), P("b1", "B"), P("b2", "B"));

        Assert.True(m.DropMenuEntry("p:a1", "c:B"));

        Assert.Equal("B", m.Find("a1")!.Category);
        Assert.Equal(["b1", "b2", "a1"], m.StartMenu.Single().Programs.Select(p => p.Id));
    }

    [Fact]
    public void Drop_ProgramOnProgram_InsertsBeforeIt_AcrossCategories()
    {
        var m = Model(P("a1", "A"), P("b1", "B"), P("b2", "B"));

        Assert.True(m.DropMenuEntry("p:a1", "p:b2"));

        Assert.Equal(["b1", "a1", "b2"], m.StartMenu.Single().Programs.Select(p => p.Id));
        Assert.Equal("B", m.Find("a1")!.Category);
    }

    [Fact]
    public void Drop_SameCategoryReorder_KeepsCategory()
    {
        var m = Model(P("x", "A"), P("y", "A"), P("z", "A"));

        Assert.True(m.DropMenuEntry("p:z", "p:x"));

        Assert.Equal(["z", "x", "y"], Flat(m));
        Assert.All(m.Programs, p => Assert.Equal("A", p.Category));
    }

    [Fact]
    public void Drop_InvalidCases_ReturnFalse()
    {
        var m = Model(P("a", "A"), P("hidden", "A", menu: false));

        Assert.False(m.DropMenuEntry("p:a", "p:a"));
        Assert.False(m.DropMenuEntry("p:hidden", "c:A"));   // fora do menu
        Assert.False(m.DropMenuEntry("p:a", "c:INEXISTENTE"));
        Assert.False(m.DropMenuEntry("c:INEXISTENTE", "c:A"));
        Assert.False(m.Dirty);
    }

    [Fact]
    public void MenuOrder_IsCompleteAfterAnyEdit()
    {
        var m = Model(P("a", "A"), P("b", "B"));
        m.MoveMenuEntry("c:B", -1);

        Assert.Equal(["c:B", "p:b", "c:A", "p:a"], m.MenuOrder);
    }

    [Fact]
    public void Discard_DoesNotTouchTheOriginalOrder()
    {
        var original = new List<string> { "c:A", "c:B" };
        var m = new DesignerModel([P("a", "A"), P("b", "B")], null, original);

        m.MoveMenuEntry("c:B", -1);

        Assert.Equal(["c:A", "c:B"], original);
    }

    // ---- manifesto / launcher ----

    [Fact]
    public void Manifest_OrderReachesLauncher_WithSameResultAsCore()
    {
        var programs = new[] { P("a", "Alfa"), P("b", "Beta"), P("b2", "Beta"), P("c", "Gama") };
        var m = new DesignerModel(programs, null);
        m.MoveMenuEntry("c:GAMA", -2);
        m.MoveMenuEntry("p:b2", -1);

        var manifest = ManifestReader.Parse(ProgramStaging.BuildManifest(m.Programs, null, m.MenuOrder));
        var launcher = ManifestReader.GroupForStartMenu(manifest.Programs, manifest.MenuOrder);

        Assert.Equal(m.StartMenu.Select(g => g.Category), launcher.Select(g => g.Key));
        Assert.Equal(m.StartMenu.SelectMany(g => g.Programs.Select(p => p.Id)), launcher.SelectMany(g => g.Value.Select(p => p.Id)));
        Assert.Equal(["Gama", "Alfa", "Beta"], launcher.Select(g => g.Key));
    }

    [Fact]
    public void Manifest_WithoutOrder_OmitsFieldAndLauncherIsAlphabetical()
    {
        var manifest = ManifestReader.Parse(ProgramStaging.BuildManifest([P("z", "Z"), P("a", "A")]));

        Assert.Empty(manifest.MenuOrder);
        Assert.Equal(["A", "Z"], ManifestReader.GroupForStartMenu(manifest.Programs, manifest.MenuOrder).Select(g => g.Key));
    }

    [Fact]
    public void Settings_PersistMenuOrder_AndOldFilesLoadEmpty()
    {
        var path = Path.Combine(_temp, "s.json");
        Directory.CreateDirectory(_temp);

        Assert.True(new UserSettings { StartMenuOrder = ["c:B", "p:b", "c:A"] }.TrySave(path));
        Assert.Equal(["c:B", "p:b", "c:A"], UserSettings.Load(path).StartMenuOrder);

        File.WriteAllText(path, "{ \"VolumeLabel\": \"X\" }");
        Assert.Empty(UserSettings.Load(path).StartMenuOrder);
    }
}
