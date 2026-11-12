using System.Text.Json;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Programs;

namespace WinPEBuilder.Core;

/// <summary>
/// Escolhas do usuário: lembradas entre execuções (settings.json em %LOCALAPPDATA%\WinPEBuilder) e também gravadas/carregadas
/// pelo usuário como "configuração" (arquivo .winpe.json), para ter várias e levar para outro PC.
/// </summary>
public sealed record UserSettings
{
    /// <summary>Versão do formato do arquivo. Aumente se um dia o formato mudar de um jeito incompatível.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Extensão sugerida para arquivos de configuração do usuário.</summary>
    public const string ConfigFileExtension = ".winpe.json";

    /// <summary>Limite de tamanho ao carregar um arquivo escolhido pelo usuário (a configuração real tem poucos KB).</summary>
    public const long MaxConfigBytes = 5L * 1024 * 1024;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeSourceKind Source { get; init; } = WinPeSourceKind.Adk;
    public string SourceIsoPath { get; init; } = string.Empty;
    public string[] Components { get; init; } = [.. OptionalComponents.DefaultSelection];
    public string InputLocale { get; init; } = "pt-BR";
    public string VolumeLabel { get; init; } = "WINPE";
    public string FilesFolder { get; init; } = string.Empty;
    public FilesMode FilesMode { get; init; } = FilesMode.OnIso;
    public string OutputIsoPath { get; init; } = string.Empty;
    public ProgramEntry[] Programs { get; init; } = [];
    public Dictionary<string, DesktopSlot> DesktopLayout { get; init; } = [];
    public string[] StartMenuOrder { get; init; } = [];
    public WallpaperSettings Wallpaper { get; init; } = new();

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
    public bool TrySave(string path) => TrySave(path, out _);

    /// <summary>Grava o arquivo e, se falhar, explica o motivo em <paramref name="error"/> (para mostrar ao usuário).</summary>
    public bool TrySave(string path, out string? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this with { FormatVersion = CurrentFormatVersion }, JsonOptions));
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = $"Não foi possível gravar o arquivo: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Lê uma configuração escolhida pelo usuário. Diferente de <see cref="Load"/> (que perdoa tudo para o app nunca travar ao
    /// abrir), aqui um arquivo inválido é um ERRO com mensagem clara: carregar "nada" em silêncio apagaria a tela do usuário.
    /// </summary>
    public static bool TryRead(string path, out UserSettings settings, out string? error)
    {
        settings = new UserSettings();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = "O arquivo não existe.";
                return false;
            }

            if (info.Length > MaxConfigBytes)
            {
                error = "O arquivo é grande demais para ser uma configuração do WinPE Builder.";
                return false;
            }

            var text = File.ReadAllText(path);
            using (var document = JsonDocument.Parse(text))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = "O arquivo não é uma configuração do WinPE Builder (o conteúdo não é um objeto JSON).";
                    return false;
                }
            }

            var loaded = JsonSerializer.Deserialize<UserSettings>(text, JsonOptions);
            if (loaded is null)
            {
                error = "O arquivo está vazio.";
                return false;
            }

            if (loaded.FormatVersion > CurrentFormatVersion)
            {
                error = $"Esta configuração foi criada por uma versão mais nova do WinPE Builder (formato {loaded.FormatVersion}). Atualize o programa.";
                return false;
            }

            settings = loaded;
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"O arquivo não é uma configuração válida do WinPE Builder: {ex.Message}";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = $"Não foi possível ler o arquivo: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Arquivos citados pela configuração que não existem neste PC (programas, pasta de arquivos, ISO de origem, imagem do papel
    /// de parede) — comum ao abrir uma configuração gravada em outro computador.
    /// </summary>
    public IReadOnlyList<string> FindMissingFiles()
    {
        var missing = new List<string>();
        foreach (var program in Programs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(program.SourceExe) && !File.Exists(program.SourceExe))
            {
                missing.Add($"Programa \"{program.Name}\": {program.SourceExe}");
            }
        }

        if (!string.IsNullOrWhiteSpace(FilesFolder) && !Directory.Exists(FilesFolder))
        {
            missing.Add($"Pasta de arquivos: {FilesFolder}");
        }

        if (Source == WinPeSourceKind.BootWimFromIso && !string.IsNullOrWhiteSpace(SourceIsoPath) && !File.Exists(SourceIsoPath))
        {
            missing.Add($"ISO do Windows: {SourceIsoPath}");
        }

        if (Wallpaper is { HasImage: true } && !File.Exists(Wallpaper.ImagePath))
        {
            missing.Add($"Imagem do papel de parede: {Wallpaper.ImagePath}");
        }

        return missing;
    }
}
