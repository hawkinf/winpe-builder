using System.Text.RegularExpressions;

namespace WinPEBuilder.Core.Programs;

/// <summary>Como a imagem do papel de parede ocupa a tela.</summary>
public enum WallpaperMode
{
    /// <summary>Cobre a tela inteira mantendo a proporção (corta o que sobrar).</summary>
    Fill,

    /// <summary>Cabe inteira na tela mantendo a proporção (sobram faixas da cor de fundo).</summary>
    Fit,

    /// <summary>Estica até preencher a tela (pode deformar).</summary>
    Stretch,

    /// <summary>Tamanho original, no centro.</summary>
    Center,

    /// <summary>Repete a imagem em mosaico.</summary>
    Tile,
}

/// <summary>Papel de parede da área de trabalho do WinPE: cor de fundo e, opcionalmente, uma imagem.</summary>
public sealed record WallpaperSettings
{
    public const string DefaultColor = "#124678";

    /// <summary>Tamanho máximo da imagem: ela vai para a mídia e é carregada na RAM do WinPE.</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    public static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    private static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

    /// <summary>Imagem no PC onde o app roda (vazio = só a cor).</summary>
    public string ImagePath { get; init; } = string.Empty;

    public WallpaperMode Mode { get; init; } = WallpaperMode.Fill;

    /// <summary>Cor de fundo "#RRGGBB" (aparece atrás da imagem e nas faixas do modo Ajustar).</summary>
    public string Color { get; init; } = DefaultColor;

    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    /// <summary>true se nada foi personalizado (então o manifesto não leva o bloco "wallpaper").</summary>
    public bool IsDefault => !HasImage && string.Equals(Color, DefaultColor, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nome do arquivo dentro da pasta Arquivos do WinPE ("wallpaper.jpg").</summary>
    public string StagedFileName => "wallpaper" + Path.GetExtension(ImagePath).ToLowerInvariant();

    public static IReadOnlyList<string> Validate(WallpaperSettings? wallpaper)
    {
        var errors = new List<string>();
        if (wallpaper is null)
        {
            return errors;
        }

        if (!ColorPattern.IsMatch(wallpaper.Color ?? string.Empty))
        {
            errors.Add("Papel de parede: a cor deve estar no formato #RRGGBB (ex.: #124678).");
        }

        if (!Enum.IsDefined(wallpaper.Mode))
        {
            errors.Add("Papel de parede: modo de exibição inválido.");
        }

        if (!wallpaper.HasImage)
        {
            return errors;
        }

        if (!File.Exists(wallpaper.ImagePath))
        {
            errors.Add($"Papel de parede: a imagem não existe mais: {wallpaper.ImagePath}");
            return errors;
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(wallpaper.ImagePath), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Papel de parede: só são aceitos {string.Join(", ", AllowedExtensions)}.");
        }

        if (new FileInfo(wallpaper.ImagePath).Length > MaxBytes)
        {
            errors.Add($"Papel de parede: a imagem passa de {MaxBytes / (1024 * 1024)} MB (ela é carregada na RAM do WinPE). Use uma menor.");
        }

        return errors;
    }
}
