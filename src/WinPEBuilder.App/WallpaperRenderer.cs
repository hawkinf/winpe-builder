using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinPEBuilder.Core.Programs;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.App;

/// <summary>
/// Desenha o papel de parede para o editor visual com a MESMA geometria do launcher
/// (<see cref="WallpaperGeometry"/>), então o preview sai igual ao que aparece no WinPE.
/// </summary>
internal static class WallpaperRenderer
{
    private static readonly Dictionary<string, (DateTime Stamp, BitmapSource Image)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Imagem decodificada (cacheada por caminho + data do arquivo); null se não existir ou não for imagem válida.</summary>
    public static BitmapSource? LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (Cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            {
                return cached.Image;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // não deixa o arquivo aberto
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(Path.GetFullPath(path));
            image.EndInit();
            image.Freeze();
            Cache[path] = (stamp, image);
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or FileFormatException)
        {
            return null;
        }
    }

    public static System.Windows.Media.Color ToWpfColor(string hex)
    {
        var c = WallpaperGeometry.ParseColor(hex);
        return System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
    }

    /// <summary>Desenha o papel de parede numa imagem de <paramref name="width"/> x <paramref name="height"/> pixels.</summary>
    public static BitmapSource Render(WallpaperSettings wallpaper, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(ToWpfColor(wallpaper.Color)), null, new Rect(0, 0, width, height));
            if (wallpaper.HasImage && LoadImage(wallpaper.ImagePath) is { } image)
            {
                var rects = WallpaperGeometry.Compute(wallpaper.Mode.ToString().ToLowerInvariant(), image.PixelWidth, image.PixelHeight, width, height);
                foreach (var r in rects)
                {
                    dc.DrawImage(image, new Rect(r.X, r.Y, r.Width, r.Height));
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
