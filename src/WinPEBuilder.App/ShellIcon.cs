using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPEBuilder.App;

/// <summary>Ícones do shell do Windows (o mesmo que o Explorer mostra) como ImageSource do WPF, com cache.</summary>
internal static class ShellIcon
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;

    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _folder;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr HIcon;
        public int IIcon;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Ícone 32x32 do arquivo. Se o arquivo não existir, devolve o ícone genérico da extensão.</summary>
    public static ImageSource Get(string path)
    {
        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var info = new ShFileInfo();
        var exists = File.Exists(path);
        var flags = ShgfiIcon | ShgfiLargeIcon | (exists ? 0u : ShgfiUseFileAttributes);
        SHGetFileInfo(path, exists ? 0u : FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);

        ImageSource image;
        if (info.HIcon == IntPtr.Zero)
        {
            image = Folder; // não deveria acontecer; melhor um ícone qualquer que nenhum
        }
        else
        {
            try
            {
                image = Imaging.CreateBitmapSourceFromHIcon(info.HIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
            }
            finally
            {
                DestroyIcon(info.HIcon);
            }
        }

        Cache[path] = image;
        return image;
    }

    /// <summary>Ícone de pasta (as mesmas cores/formas que o launcher desenha dentro do WinPE).</summary>
    public static ImageSource Folder => _folder ??= BuildFolder();

    private static ImageSource BuildFolder()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(222, 168, 40)), null, new RectangleGeometry(new Rect(2, 6, 12, 6))));
        var front = new LinearGradientBrush(Color.FromRgb(255, 214, 102), Color.FromRgb(240, 180, 50), 90);
        group.Children.Add(new GeometryDrawing(front, new Pen(new SolidColorBrush(Color.FromRgb(150, 100, 10)), 1), new RectangleGeometry(new Rect(2, 10, 28, 18))));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
