#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace WinPEBuilder.Launcher.Model
{
    /// <summary>
    /// Matematica do papel de parede (sem desenhar nada): onde a imagem cai na tela em cada modo. O launcher (GDI+) e o
    /// editor visual (WPF) usam as MESMAS contas, para o preview ser igual ao que aparece no WinPE.
    /// </summary>
    public static class WallpaperGeometry
    {
        public const string Fill = "fill";
        public const string Fit = "fit";
        public const string Stretch = "stretch";
        public const string Center = "center";
        public const string Tile = "tile";
        public const string DefaultColor = "#124678";
        private const int MaxTiles = 4000;

        public static string NormalizeMode(string mode)
        {
            var m = (mode ?? string.Empty).Trim().ToLowerInvariant();
            return m == Fit || m == Stretch || m == Center || m == Tile ? m : Fill;
        }

        /// <summary>Retangulos de destino (coordenadas da area) em que a imagem deve ser desenhada; podem passar da area (o desenho recorta).</summary>
        public static List<Rectangle> Compute(string mode, int imageWidth, int imageHeight, int areaWidth, int areaHeight)
        {
            var result = new List<Rectangle>();
            if (imageWidth <= 0 || imageHeight <= 0 || areaWidth <= 0 || areaHeight <= 0)
            {
                return result;
            }

            switch (NormalizeMode(mode))
            {
                case Stretch:
                    result.Add(new Rectangle(0, 0, areaWidth, areaHeight));
                    break;

                case Center:
                    result.Add(new Rectangle((areaWidth - imageWidth) / 2, (areaHeight - imageHeight) / 2, imageWidth, imageHeight));
                    break;

                case Tile:
                    for (int y = 0, count = 0; y < areaHeight && count < MaxTiles; y += imageHeight)
                    {
                        for (var x = 0; x < areaWidth && count < MaxTiles; x += imageWidth, count++)
                        {
                            result.Add(new Rectangle(x, y, imageWidth, imageHeight));
                        }
                    }

                    break;

                case Fit:
                case Fill:
                default:
                    var scaleX = (double)areaWidth / imageWidth;
                    var scaleY = (double)areaHeight / imageHeight;
                    var scale = NormalizeMode(mode) == Fit ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
                    var width = (int)Math.Round(imageWidth * scale);
                    var height = (int)Math.Round(imageHeight * scale);
                    result.Add(new Rectangle((areaWidth - width) / 2, (areaHeight - height) / 2, width, height));
                    break;
            }

            return result;
        }

        /// <summary>Cor "#RRGGBB" para <see cref="Color"/>; usa a cor padrao se o texto for invalido.</summary>
        public static Color ParseColor(string hex)
        {
            Color color;
            return TryParseColor(hex, out color) ? color : ParseDefault();
        }

        public static bool TryParseColor(string hex, out Color color)
        {
            color = Color.Empty;
            var text = (hex ?? string.Empty).Trim();
            if (text.Length != 7 || text[0] != '#')
            {
                return false;
            }

            int rgb;
            if (!int.TryParse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
            {
                return false;
            }

            color = Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        private static Color ParseDefault()
        {
            Color color;
            TryParseColor(DefaultColor, out color);
            return color;
        }
    }
}
