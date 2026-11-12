using System.Drawing;
using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>Papel de parede: geometria dos modos, validação, manifesto, build e posicionamento dos ícones na grade.</summary>
public sealed class WallpaperTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-wall-" + Guid.NewGuid().ToString("N"));

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

    private string Image(string name = "foto.png", int bytes = 10)
    {
        var path = Path.Combine(_temp, name);
        Directory.CreateDirectory(_temp);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private ProgramEntry P(string id)
    {
        var exe = Path.Combine(_temp, "origem", id, id + ".exe"); // pasta própria: não pode conter a pasta de trabalho
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "x");
        return new ProgramEntry { Id = id, Name = id, SourceExe = exe };
    }

    // ---- geometria ----

    [Fact]
    public void Fill_CoversTheScreenKeepingAspect_AndCropsTheExcess()
    {
        var r = Assert.Single(WallpaperGeometry.Compute("fill", 600, 300, 1000, 1000)); // imagem larga em tela quadrada

        Assert.Equal(new Rectangle(-500, 0, 2000, 1000), r);   // altura preenchida, largura passa e é cortada
        Assert.True(r.Width * 300 == r.Height * 600);          // proporção mantida
    }

    [Fact]
    public void Fit_ShowsTheWholeImage_WithBars()
    {
        var r = Assert.Single(WallpaperGeometry.Compute("fit", 600, 300, 1000, 1000));

        Assert.Equal(new Rectangle(0, 250, 1000, 500), r);
    }

    [Fact]
    public void Stretch_UsesTheWholeArea() =>
        Assert.Equal(new Rectangle(0, 0, 1024, 726), Assert.Single(WallpaperGeometry.Compute("stretch", 10, 10, 1024, 726)));

    [Fact]
    public void Center_KeepsOriginalSize_AndMayOverflow()
    {
        Assert.Equal(new Rectangle(212, 213, 600, 300), Assert.Single(WallpaperGeometry.Compute("center", 600, 300, 1024, 726)));
        Assert.Equal(new Rectangle(-488, -137, 2000, 1000), Assert.Single(WallpaperGeometry.Compute("center", 2000, 1000, 1024, 726)));
    }

    [Fact]
    public void Tile_CoversTheAreaFromTopLeft()
    {
        var tiles = WallpaperGeometry.Compute("tile", 300, 200, 1000, 500);

        Assert.Equal(4 * 3, tiles.Count);              // 4 colunas (0..900) x 3 linhas (0..400)
        Assert.Equal(new Rectangle(0, 0, 300, 200), tiles[0]);
        Assert.Equal(new Rectangle(900, 400, 300, 200), tiles[^1]);
    }

    [Fact]
    public void Tile_IsCappedForTinyImages() =>
        Assert.True(WallpaperGeometry.Compute("tile", 1, 1, 4000, 4000).Count <= 4000);

    [Theory]
    [InlineData("FILL", "fill")]
    [InlineData(" Fit ", "fit")]
    [InlineData("stretch", "stretch")]
    [InlineData("CENTER", "center")]
    [InlineData("tile", "tile")]
    [InlineData("qualquer", "fill")]
    [InlineData(null, "fill")]
    public void NormalizeMode_DefaultsToFill(string? input, string expected) => Assert.Equal(expected, WallpaperGeometry.NormalizeMode(input));

    [Theory]
    [InlineData(0, 10, 100, 100)]
    [InlineData(10, 0, 100, 100)]
    [InlineData(10, 10, 0, 100)]
    public void Compute_DegenerateSizes_ReturnsNothing(int iw, int ih, int aw, int ah) =>
        Assert.Empty(WallpaperGeometry.Compute("fill", iw, ih, aw, ah));

    [Fact]
    public void ModeNames_MatchTheCoreEnum()
    {
        foreach (var mode in Enum.GetValues<WallpaperMode>())
        {
            Assert.Equal(mode.ToString().ToLowerInvariant(), WallpaperGeometry.NormalizeMode(mode.ToString()));
        }
    }

    // ---- cor ----

    [Fact]
    public void ParseColor_ValidAndInvalid()
    {
        Assert.Equal(Color.FromArgb(255, 0x12, 0x46, 0x78), WallpaperGeometry.ParseColor("#124678"));
        Assert.Equal(Color.FromArgb(255, 255, 255, 255), WallpaperGeometry.ParseColor(" #ffffff "));
        Assert.Equal(WallpaperGeometry.ParseColor(WallpaperGeometry.DefaultColor), WallpaperGeometry.ParseColor("azul"));
        Assert.False(WallpaperGeometry.TryParseColor("#12345", out _));
        Assert.False(WallpaperGeometry.TryParseColor("#GGGGGG", out _));
        Assert.False(WallpaperGeometry.TryParseColor(null, out _));
        Assert.Equal(WallpaperSettings.DefaultColor, WallpaperGeometry.DefaultColor);
    }

    // ---- validação ----

    [Fact]
    public void Default_IsDefault_AndPassesValidation()
    {
        var w = new WallpaperSettings();

        Assert.True(w.IsDefault);
        Assert.Empty(WallpaperSettings.Validate(w));
        Assert.Empty(WallpaperSettings.Validate(null));
    }

    [Fact]
    public void Validate_Color_Image_Extension_Size()
    {
        Assert.NotEmpty(WallpaperSettings.Validate(new WallpaperSettings { Color = "azul" }));
        Assert.NotEmpty(WallpaperSettings.Validate(new WallpaperSettings { ImagePath = Path.Combine(_temp, "nao-existe.png") }));
        Assert.NotEmpty(WallpaperSettings.Validate(new WallpaperSettings { ImagePath = Image("x.gif") }));
        Assert.Empty(WallpaperSettings.Validate(new WallpaperSettings { ImagePath = Image("ok.JPG") }));

        var huge = Path.Combine(_temp, "enorme.png");
        using (var fs = new FileStream(huge, FileMode.Create))
        {
            fs.SetLength(WallpaperSettings.MaxBytes + 1);
        }

        Assert.Contains(WallpaperSettings.Validate(new WallpaperSettings { ImagePath = huge }), e => e.Contains("MB", StringComparison.Ordinal));
    }

    [Fact]
    public void IsDefault_FalseWithImageOrCustomColor()
    {
        Assert.False(new WallpaperSettings { Color = "#000000" }.IsDefault);
        Assert.False(new WallpaperSettings { ImagePath = Image() }.IsDefault);
        Assert.True(new WallpaperSettings { Color = "#124678" }.IsDefault);
        Assert.True(new WallpaperSettings { Color = "#124678".ToLowerInvariant() }.IsDefault);
    }

    [Fact]
    public void BuildOptions_Validate_ReportsWallpaperProblems()
    {
        var options = new BuildOptions
        {
            WorkDirectory = Path.Combine(_temp, "w"),
            OutputIsoPath = Path.Combine(_temp, "o.iso"),
            Wallpaper = new WallpaperSettings { Color = "xx" },
        };

        Assert.Contains(options.Validate(), e => e.Contains("Papel de parede", StringComparison.Ordinal));
    }

    // ---- manifesto ----

    [Fact]
    public void Manifest_DefaultWallpaper_IsOmitted()
    {
        var manifest = ManifestReader.Parse(ProgramStaging.BuildManifest([P("a")], null, null, new WallpaperSettings()));

        Assert.Null(manifest.Wallpaper);
    }

    [Fact]
    public void Manifest_ColorOnly_AndImage_ReachTheLauncher()
    {
        var onlyColor = ManifestReader.Parse(ProgramStaging.BuildManifest([P("a")], null, null, new WallpaperSettings { Color = "#0a3d62" })).Wallpaper!;
        Assert.Equal(string.Empty, onlyColor.File);
        Assert.Equal("#0A3D62", onlyColor.Color);

        var withImage = ManifestReader.Parse(ProgramStaging.BuildManifest(
            [P("a")], null, null, new WallpaperSettings { ImagePath = Image("Minha Foto.JPG"), Mode = WallpaperMode.Tile })).Wallpaper!;
        Assert.Equal("wallpaper.jpg", withImage.File);
        Assert.Equal("tile", withImage.Mode);
    }

    [Fact]
    public void Parse_BadWallpaperFields_FallBackToSafeDefaults()
    {
        const string json = """{"version":1,"programs":[],"wallpaper":{"file":"a.png","mode":"explodir","color":"vermelho"}}""";

        var wallpaper = ManifestReader.Parse(json).Wallpaper!;

        Assert.Equal("fill", wallpaper.Mode);
        Assert.Equal(WallpaperGeometry.DefaultColor, wallpaper.Color);
    }

    [Fact]
    public void ResolveRelative_RefusesEscapes()
    {
        var root = Path.Combine(_temp, "Arquivos");

        Assert.Equal(Path.Combine(root, "wallpaper.png"), ManifestReader.ResolveRelative(root, "wallpaper.png"));
        Assert.Null(ManifestReader.ResolveRelative(root, "..\\segredo.png"));
        Assert.Null(ManifestReader.ResolveRelative(root, "C:\\Windows\\win.ini"));
        Assert.Null(ManifestReader.ResolveRelative(root, ""));
    }

    // ---- cópia para a mídia ----

    [Fact]
    public void Stage_CopiesTheImageNextToTheManifest()
    {
        var wallpaper = new WallpaperSettings { ImagePath = Image("foto.jpeg", 123) };
        var files = Path.Combine(_temp, "saida", "Arquivos");

        ProgramStaging.Stage([P("a")], files, default, null, null, wallpaper);

        Assert.Equal(123, new FileInfo(Path.Combine(files, "wallpaper.jpeg")).Length);
        Assert.Equal("wallpaper.jpeg", ManifestReader.Load(Path.Combine(files, "programs.json")).Wallpaper!.File);
    }

    [Fact]
    public async Task Build_WithWallpaper_PutsImageAndBlockOnTheIso()
    {
        var work = Path.Combine(_temp, "app", "work", "amd64");
        var kit = new AdkInstall(Path.Combine(_temp, "kit"));
        foreach (var f in new[] { kit.DismExe, kit.OscdimgExe, kit.CopypeCmd, kit.WinPeWim(WinPeArchitecture.X64) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, "x");
        }

        var oscdimgDir = Path.Combine(kit.DeploymentToolsRoot, "amd64", "Oscdimg");
        Directory.CreateDirectory(oscdimgDir);
        File.WriteAllText(Path.Combine(oscdimgDir, "etfsboot.com"), "x");
        File.WriteAllText(Path.Combine(oscdimgDir, "efisys_noprompt.bin"), "x");
        Directory.CreateDirectory(kit.OptionalComponentsDirectory(WinPeArchitecture.X64));
        var runner = new ScriptedRunner(c =>
        {
            if (c.Arguments.Contains("copype.cmd"))
            {
                Directory.CreateDirectory(Path.Combine(work, "media", "sources"));
                File.WriteAllText(Path.Combine(work, "media", "sources", "boot.wim"), "x");
                Directory.CreateDirectory(Path.Combine(work, "mount"));
            }

            if (c.FileName.EndsWith("oscdimg.exe", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(Path.Combine(_temp, "o.iso"), "x");
            }

            return (0, []);
        });
        var options = new BuildOptions
        {
            WorkDirectory = work,
            OutputIsoPath = Path.Combine(_temp, "o.iso"),
            Programs = [P("a")],
            Wallpaper = new WallpaperSettings { ImagePath = Image("fundo.png"), Mode = WallpaperMode.Fit, Color = "#000000" },
        };

        await new WinPeBuildService(runner, new AdkLocator([kit.KitsRoot]), Path.Combine(_temp, "app"))
            .BuildAsync(options, new Progress<BuildProgress>(_ => { }), _ => { }, CancellationToken.None);

        var files = Path.Combine(work, "media", "Arquivos");
        Assert.True(File.Exists(Path.Combine(files, "wallpaper.png")));
        var wallpaper = ManifestReader.Load(Path.Combine(files, "programs.json")).Wallpaper!;
        Assert.Equal(("wallpaper.png", "fit", "#000000"), (wallpaper.File, wallpaper.Mode, wallpaper.Color));
    }

    // ---- ícones na grade (launcher) ----

    private static List<LayoutEntry> Layout(params (string Key, int Col, int Row)[] entries) =>
        [.. entries.Select(e => new LayoutEntry { Key = e.Key, Col = e.Col, Row = e.Row })];

    [Fact]
    public void PlaceIcons_HonorsLayout_AndPlacesTheRestColumnByColumn()
    {
        var cells = ManifestReader.PlaceIcons(["p:a", "p:b", "p:c"], Layout(("p:b", 3, 2)), 12, 9);

        Assert.Equal(new Point(3, 2), cells["p:b"]);
        Assert.Equal(new Point(0, 0), cells["p:a"]);
        Assert.Equal(new Point(0, 1), cells["p:c"]);
    }

    [Fact]
    public void PlaceIcons_OutsideVisibleGrid_IsRelocated_NeverHidden()
    {
        var cells = ManifestReader.PlaceIcons(["p:a", "p:b"], Layout(("p:a", 11, 8), ("p:b", 0, 0)), 5, 4); // tela pequena

        Assert.True(cells["p:a"].X < 5 && cells["p:a"].Y < 4);
        Assert.Equal(new Point(0, 0), cells["p:b"]);
        Assert.NotEqual(cells["p:a"], cells["p:b"]);
    }

    [Fact]
    public void PlaceIcons_DuplicateCells_DoNotOverlap()
    {
        var cells = ManifestReader.PlaceIcons(["p:a", "p:b"], Layout(("p:a", 2, 2), ("p:b", 2, 2)), 12, 9);

        Assert.Equal(2, cells.Values.Distinct().Count());
    }

    [Fact]
    public void PlaceIcons_FullGrid_StillReturnsAllItems()
    {
        var keys = Enumerable.Range(0, 7).Select(i => $"p:{i}").ToList();

        var cells = ManifestReader.PlaceIcons(keys, null, 2, 3); // 6 células para 7 ícones

        Assert.Equal(7, cells.Count);
    }

    [Fact]
    public void PlaceIcons_MatchesTheCoreGridForReferenceScreens()
    {
        var programs = Enumerable.Range(0, 15).Select(i => P($"prog{i:00}") with { ShowOnDesktop = true }).ToList();
        var coreLayout = DesktopLayoutHelper.Normalize(DesktopLayoutHelper.Items(programs), null);

        var launcher = ManifestReader.PlaceIcons([.. coreLayout.Keys], null, DesktopLayoutHelper.Columns, DesktopLayoutHelper.Rows);

        foreach (var (key, slot) in coreLayout)
        {
            Assert.Equal(new Point(slot.Col, slot.Row), launcher[key]);
        }
    }

    // ---- editor e persistência ----

    [Fact]
    public void DesignerModel_Wallpaper_DirtyOnlyWhenChanged_AndDiscardKeepsOriginal()
    {
        var original = new WallpaperSettings { Color = "#111111" };
        var model = new DesignerModel([], null, null, original);

        Assert.False(model.SetWallpaper(original with { }));   // igual: não suja
        Assert.False(model.Dirty);
        Assert.True(model.SetWallpaper(original with { Mode = WallpaperMode.Tile }));
        Assert.True(model.Dirty);
        Assert.Equal(WallpaperMode.Fill, original.Mode);        // o original (descartar) não mudou
    }

    [Fact]
    public void Settings_PersistWallpaper_AndOldFilesGetDefault()
    {
        var path = Path.Combine(_temp, "s.json");
        Directory.CreateDirectory(_temp);
        var wallpaper = new WallpaperSettings { ImagePath = @"C:\fotos\a.png", Mode = WallpaperMode.Center, Color = "#ABCDEF" };

        Assert.True(new UserSettings { Wallpaper = wallpaper }.TrySave(path));
        Assert.Equal(wallpaper, UserSettings.Load(path).Wallpaper);
        Assert.Contains("Center", File.ReadAllText(path), StringComparison.Ordinal);   // enum legível no JSON

        File.WriteAllText(path, "{ \"VolumeLabel\": \"X\" }");
        Assert.True(UserSettings.Load(path).Wallpaper.IsDefault);
    }
}
