using System.Text.Json;
using WinPEBuilder.Core.Build;

namespace WinPEBuilder.Core;

/// <summary>Escolhas do usuário lembradas entre execuções (settings.json em %LOCALAPPDATA%\WinPEBuilder).</summary>
public sealed record UserSettings
{
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeSourceKind Source { get; init; } = WinPeSourceKind.Adk;
    public string SourceIsoPath { get; init; } = string.Empty;
    public string[] Components { get; init; } = [.. OptionalComponents.DefaultSelection];
    public string InputLocale { get; init; } = "pt-BR";
    public string VolumeLabel { get; init; } = "WINPE";
    public string FilesFolder { get; init; } = string.Empty;
    public FilesMode FilesMode { get; init; } = FilesMode.OnIso;
    public string OutputIsoPath { get; init; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Lê o arquivo; se não existir ou estiver corrompido, devolve os padrões (nunca lança).</summary>
    public static UserSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), JsonOptions) ?? new UserSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Arquivo ilegível: recomeça com os padrões.
        }

        return new UserSettings();
    }

    /// <summary>Grava o arquivo; devolve false (sem lançar) se não foi possível — não é motivo para travar o app.</summary>
    public bool TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
