using WinPEBuilder.Core;
using WinPEBuilder.Core.Build;

namespace WinPEBuilder.Tests;

public sealed class SettingsAndLogTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wpb-set-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var path = Path.Combine(_temp, "sub", "settings.json");
        var original = new UserSettings
        {
            Architecture = WinPeArchitecture.X86,
            Source = WinPeSourceKind.BootWimFromIso,
            SourceIsoPath = @"O:\isos\w10.iso",
            Components = ["WinPE-WMI"],
            InputLocale = "en-US",
            VolumeLabel = "TESTE",
            FilesFolder = @"D:\meus",
            FilesMode = FilesMode.InsideImage,
            OutputIsoPath = @"D:\out\x.iso",
        };

        Assert.True(original.TrySave(path));
        var loaded = UserSettings.Load(path);

        Assert.Equal(original.Architecture, loaded.Architecture);
        Assert.Equal(original.Source, loaded.Source);
        Assert.Equal(original.SourceIsoPath, loaded.SourceIsoPath);
        Assert.Equal(original.Components, loaded.Components);
        Assert.Equal(original.InputLocale, loaded.InputLocale);
        Assert.Equal(original.VolumeLabel, loaded.VolumeLabel);
        Assert.Equal(original.FilesFolder, loaded.FilesFolder);
        Assert.Equal(original.FilesMode, loaded.FilesMode);
        Assert.Equal(original.OutputIsoPath, loaded.OutputIsoPath);
        Assert.Contains("\"X86\"", File.ReadAllText(path)); // enums legíveis no JSON
    }

    [Fact]
    public void Settings_MissingOrCorrupt_FallBackToDefaults()
    {
        Assert.Equal(WinPeArchitecture.X64, UserSettings.Load(Path.Combine(_temp, "nao-existe.json")).Architecture);

        Directory.CreateDirectory(_temp);
        var bad = Path.Combine(_temp, "ruim.json");
        File.WriteAllText(bad, "{ isto não é json");
        var loaded = UserSettings.Load(bad);
        Assert.Equal(WinPeArchitecture.X64, loaded.Architecture);
        Assert.Equal(OptionalComponents.DefaultSelection, loaded.Components);
    }

    [Fact]
    public void Settings_UnwritablePath_ReturnsFalseInsteadOfThrowing()
    {
        Directory.CreateDirectory(_temp);
        var file = Path.Combine(_temp, "arquivo");
        File.WriteAllText(file, "x");
        // "arquivo" é um arquivo, então não dá para criar uma pasta com esse nome.
        Assert.False(new UserSettings().TrySave(Path.Combine(file, "settings.json")));
    }

    [Fact]
    public void LogFile_WritesTimestampedLines_AndKeepsOnlyRecent()
    {
        var dir = Path.Combine(_temp, "logs");
        Directory.CreateDirectory(dir);
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(dir, $"build-2020010{i}-000000.log"), "velho");
        }

        string path;
        using (var log = LogFile.Create(dir, keep: 3))
        {
            log.WriteLine("olá");
            path = log.Path;
        }

        Assert.Contains("olá", File.ReadAllText(path));
        Assert.Equal(3, Directory.GetFiles(dir, "build-*.log").Length);
        Assert.True(File.Exists(path)); // o novo nunca é apagado
    }
}
