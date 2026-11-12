using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.App;

/// <summary>Escolha do papel de parede (imagem, modo e cor) com preview exato da tela do WinPE.</summary>
public partial class WallpaperWindow : Window
{
    private static readonly string[] PresetColors =
        ["#124678", "#0A3D62", "#1E272E", "#000000", "#2C3A47", "#3B3B98", "#1B9CE5", "#218C74", "#B33771", "#833471", "#CAD3C8", "#FFFFFF"];

    private string _imagePath = string.Empty;
    private bool _loading = true;

    public WallpaperSettings? Result { get; private set; }

    public WallpaperWindow(WallpaperSettings current)
    {
        InitializeComponent();
        _imagePath = current.ImagePath ?? string.Empty;
        ImageBox.Text = _imagePath;
        ModeBox.SelectedIndex = Math.Max(0, (int)current.Mode);
        ColorBox.Text = current.Color;
        foreach (var hex in PresetColors)
        {
            var button = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(WallpaperRenderer.ToWpfColor(hex)),
                BorderBrush = Brushes.Gray,
                ToolTip = hex,
                Tag = hex,
            };
            button.Click += (_, _) => ColorBox.Text = hex;
            Presets.Children.Add(button);
        }

        _loading = false;
        Refresh();
    }

    private WallpaperSettings Current => new()
    {
        ImagePath = _imagePath,
        Mode = ModeBox.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<WallpaperMode>(tag, out var mode) ? mode : WallpaperMode.Fill,
        Color = ColorBox.Text.Trim(),
    };

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }

        var wallpaper = Current;
        var colorOk = WallpaperGeometry.TryParseColor(wallpaper.Color, out _);
        ColorBox.BorderBrush = colorOk ? SystemColors.ControlDarkBrush : Brushes.Red;
        ColorSwatch.Background = new SolidColorBrush(WallpaperRenderer.ToWpfColor(colorOk ? wallpaper.Color : WallpaperSettings.DefaultColor));
        ModeBox.IsEnabled = wallpaper.HasImage;
        RemoveImageButton.IsEnabled = wallpaper.HasImage;
        ImageBox.Text = _imagePath;

        var preview = colorOk ? wallpaper : wallpaper with { Color = WallpaperSettings.DefaultColor };
        PreviewImage.Source = WallpaperRenderer.Render(preview, 1024, 726);

        if (wallpaper.HasImage)
        {
            var image = WallpaperRenderer.LoadImage(_imagePath);
            InfoText.Text = image is null
                ? "⚠ Não foi possível abrir essa imagem."
                : $"Imagem de {image.PixelWidth}×{image.PixelHeight} px, {new FileInfo(_imagePath).Length / (1024.0 * 1024):0.#} MB. "
                  + "O preview mostra a tela de referência 1024×768 (em telas maiores a imagem é redimensionada do mesmo jeito).";
        }
        else
        {
            InfoText.Text = "Sem imagem: a área de trabalho usa só a cor de fundo.";
        }
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => Refresh();

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha a imagem do papel de parede",
            Filter = "Imagens (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _imagePath = dialog.FileName;
            Refresh();
        }
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        _imagePath = string.Empty;
        Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _imagePath = string.Empty;
        ModeBox.SelectedIndex = 0;
        ColorBox.Text = WallpaperSettings.DefaultColor;
        Refresh();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var wallpaper = Current;
        var errors = WallpaperSettings.Validate(wallpaper);
        if (wallpaper.HasImage && WallpaperRenderer.LoadImage(_imagePath) is null)
        {
            errors = [.. errors, "Papel de parede: não foi possível abrir a imagem (arquivo corrompido ou formato não suportado)."];
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "WinPE Builder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = wallpaper with { Color = wallpaper.Color.ToUpperInvariant() };
        DialogResult = true;
    }
}
