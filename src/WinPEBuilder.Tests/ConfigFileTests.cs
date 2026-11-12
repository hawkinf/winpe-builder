using WinPEBuilder.Core;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Programs;

namespace WinPEBuilder.Tests;

/// <summary>Gravar e carregar configurações escolhidas pelo usuário (arquivos .winpe.json).</summary>
public sealed class ConfigFileTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-cfg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private string Path_(string name)
    {
        Directory.CreateDirectory(_temp);
        return Path.Combine(_temp, name);
    }

    private string Touch(string name)
    {
        var path = Path_(name);
        File.WriteAllText(path, "x");
        return path;
    }

    private UserSettings Full()
    {
        var exe = Touch("prog.exe");
        return new UserSettings
        {
            Architecture = WinPeArchitecture.X86,
            Source = WinPeSourceKind.BootWimFromIso,
            SourceIsoPath = @"D:\iso\win10x86.iso",
            Components = ["WinPE-WMI", "WinPE-PowerShell"],
            InputLocale = "en-US",
            VolumeLabel = "MEUPE",
            FilesFolder = @"D:\meus-arquivos",
            FilesMode = FilesMode.InsideImage,
            OutputIsoPath = @"D:\saida\x.iso",
            Programs =
            [
                new ProgramEntry
                {
                    Id = "prog", Name = "Meu Programa", SourceExe = exe, Category = "Ferramentas", Arguments = "/x",
                    CopyFolder = false, ShowOnDesktop = false, ShowInStartMenu = true, DesktopFolder = "Ferr/Sub", SourceRoot = string.Empty,
                },
            ],
            DesktopLayout = new Dictionary<string, DesktopSlot> { ["f:FERR"] = new(3, 4) },
            StartMenuOrder = ["c:FERRAMENTAS", "p:prog"],
            Wallpaper = new WallpaperSettings { ImagePath = @"D:\fotos\a.png", Mode = WallpaperMode.Tile, Color = "#ABCDEF" },
        };
    }

    // ---- ida e volta ----

    [Fact]
    public void SaveThenRead_RestoresEverything()
    {
        var original = Full();
        var path = Path_("minha.winpe.json");

        Assert.True(original.TrySave(path, out var saveError));
        Assert.Null(saveError);
        Assert.True(UserSettings.TryRead(path, out var back, out var error), error);

        Assert.Equal(original.Architecture, back.Architecture);
        Assert.Equal(original.Source, back.Source);
        Assert.Equal(original.SourceIsoPath, back.SourceIsoPath);
        Assert.Equal(original.Components, back.Components);
        Assert.Equal(original.InputLocale, back.InputLocale);
        Assert.Equal(original.VolumeLabel, back.VolumeLabel);
        Assert.Equal(original.FilesFolder, back.FilesFolder);
        Assert.Equal(original.FilesMode, back.FilesMode);
        Assert.Equal(original.OutputIsoPath, back.OutputIsoPath);
        Assert.Equal(original.Programs, back.Programs);
        Assert.Equal(original.DesktopLayout, back.DesktopLayout);
        Assert.Equal(original.StartMenuOrder, back.StartMenuOrder);
        Assert.Equal(original.Wallpaper, back.Wallpaper);
    }

    [Fact]
    public void Save_WritesTheFormatVersion_AndReadableEnums()
    {
        var path = Path_("v.winpe.json");

        Assert.True((Full() with { FormatVersion = 0 }).TrySave(path));
        var text = File.ReadAllText(path);

        Assert.Contains($"\"FormatVersion\": {UserSettings.CurrentFormatVersion}", text, StringComparison.Ordinal);
        Assert.Contains("\"X86\"", text, StringComparison.Ordinal);
        Assert.Contains("\"InsideImage\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoConfigurations_AreIndependent()
    {
        var a = Path_("a.winpe.json");
        var b = Path_("b.winpe.json");
        new UserSettings { VolumeLabel = "UM" }.TrySave(a);
        new UserSettings { VolumeLabel = "DOIS" }.TrySave(b);

        Assert.True(UserSettings.TryRead(a, out var sa, out _));
        Assert.True(UserSettings.TryRead(b, out var sb, out _));
        Assert.Equal(("UM", "DOIS"), (sa.VolumeLabel, sb.VolumeLabel));
    }

    [Fact]
    public void OldFileWithoutFormatVersion_AndUnknownProperties_StillLoad()
    {
        var path = Path_("velho.winpe.json");
        File.WriteAllText(path, "{ \"VolumeLabel\": \"VELHO\", \"CampoDoFuturo\": [1,2,3] }");

        Assert.True(UserSettings.TryRead(path, out var s, out var error), error);

        Assert.Equal("VELHO", s.VolumeLabel);
        Assert.Equal(UserSettings.CurrentFormatVersion, s.FormatVersion);
    }

    // ---- arquivos inválidos: erro claro, nunca "carregar nada" ----

    [Fact]
    public void Read_MissingFile_IsAnError()
    {
        Assert.False(UserSettings.TryRead(Path.Combine(_temp, "nao-existe.json"), out _, out var error));
        Assert.Contains("não existe", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("isto nao e json")]
    [InlineData("{ \"VolumeLabel\": ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"so uma string\"")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    public void Read_InvalidContent_IsAnErrorWithAMessage(string content)
    {
        var path = Path_("ruim.json");
        File.WriteAllText(path, content);

        Assert.False(UserSettings.TryRead(path, out var settings, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        // devolve os padrões, nunca lixo parcial
        Assert.Equal(("WINPE", WinPeArchitecture.X64), (settings.VolumeLabel, settings.Architecture));
        Assert.Empty(settings.Programs);
    }

    [Fact]
    public void Read_InvalidEnumOrMissingRequiredField_IsAnError()
    {
        var badEnum = Path_("enum.json");
        File.WriteAllText(badEnum, "{ \"Architecture\": \"Sparc\" }");
        var noName = Path_("semnome.json");
        File.WriteAllText(noName, "{ \"Programs\": [ { \"SourceExe\": \"C:\\\\a.exe\" } ] }");

        Assert.False(UserSettings.TryRead(badEnum, out _, out var e1));
        Assert.False(UserSettings.TryRead(noName, out _, out var e2));
        Assert.Contains("válida", e1, StringComparison.Ordinal);
        Assert.Contains("válida", e2, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_FromANewerFormat_IsRefusedWithAdvice()
    {
        var path = Path_("novo.json");
        File.WriteAllText(path, $"{{ \"FormatVersion\": {UserSettings.CurrentFormatVersion + 1} }}");

        Assert.False(UserSettings.TryRead(path, out _, out var error));
        Assert.Contains("mais nova", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_HugeFile_IsRefusedWithoutLoadingIt()
    {
        var path = Path_("enorme.json");
        using (var fs = new FileStream(path, FileMode.Create))
        {
            fs.SetLength(UserSettings.MaxConfigBytes + 1);
        }

        Assert.False(UserSettings.TryRead(path, out _, out var error));
        Assert.Contains("grande demais", error, StringComparison.Ordinal);
    }

    // ---- gravar com problema ----

    [Fact]
    public void Save_ToAnInvalidPath_ReturnsFalseWithAMessage_AndDoesNotThrow()
    {
        Directory.CreateDirectory(_temp);
        var onTopOfAFolder = _temp; // gravar "em cima" de uma pasta existente

        Assert.False(new UserSettings().TrySave(onTopOfAFolder, out var error));
        Assert.StartsWith("Não foi possível gravar", error, StringComparison.Ordinal);
        Assert.False(new UserSettings().TrySave("\0inválido", out var error2));
        Assert.False(string.IsNullOrEmpty(error2));
    }

    // ---- arquivos que não existem neste PC ----

    [Fact]
    public void FindMissingFiles_ListsWhatIsGoneOnThisPc()
    {
        var settings = Full() with
        {
            Programs = [new ProgramEntry { Id = "x", Name = "Sumiu", SourceExe = @"Z:\nao\existe.exe" }],
        };

        var missing = settings.FindMissingFiles();

        Assert.Contains(missing, m => m.StartsWith("Programa \"Sumiu\"", StringComparison.Ordinal));
        Assert.Contains(missing, m => m.StartsWith("Pasta de arquivos:", StringComparison.Ordinal));
        Assert.Contains(missing, m => m.StartsWith("ISO do Windows:", StringComparison.Ordinal));
        Assert.Contains(missing, m => m.StartsWith("Imagem do papel de parede:", StringComparison.Ordinal));
    }

    [Fact]
    public void FindMissingFiles_IgnoresTheIsoWhenTheSourceIsTheAdk_AndEmptyPaths()
    {
        var onlyDefaults = new UserSettings { Source = WinPeSourceKind.Adk, SourceIsoPath = @"D:\iso\sumiu.iso" };

        Assert.Empty(onlyDefaults.FindMissingFiles());
        Assert.Empty(new UserSettings().FindMissingFiles());
    }

    [Fact]
    public void FindMissingFiles_Empty_WhenEverythingExists()
    {
        var exe = Touch("ok.exe");
        var folder = Path.Combine(_temp, "arquivos");
        Directory.CreateDirectory(folder);
        var image = Touch("fundo.png");
        var settings = new UserSettings
        {
            Programs = [new ProgramEntry { Id = "ok", Name = "Ok", SourceExe = exe }],
            FilesFolder = folder,
            Wallpaper = new WallpaperSettings { ImagePath = image },
        };

        Assert.Empty(settings.FindMissingFiles());
    }

    // ---- compatibilidade com o settings.json automático ----

    [Fact]
    public void TolerantLoad_StillNeverThrows_AndReturnsDefaultsOnGarbage()
    {
        var path = Path_("settings.json");
        File.WriteAllText(path, "{{{ lixo");

        Assert.Equal(new UserSettings().VolumeLabel, UserSettings.Load(path).VolumeLabel);
    }
}
