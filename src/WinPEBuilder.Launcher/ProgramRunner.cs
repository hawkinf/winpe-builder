using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Launcher
{
    /// <summary>Log simples em %TEMP% (no WinPE: X:\Windows\Temp). Nunca lança exceção.</summary>
    internal static class AppLog
    {
        public static void Write(string message)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "WinPELauncher.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // sem log: não é motivo para travar o launcher
            }
        }
    }

    /// <summary>Inicia os programas do manifesto e extrai os ícones dos executáveis.</summary>
    internal sealed class ProgramRunner
    {
        private readonly string _filesRoot;

        public ProgramRunner(string filesRoot)
        {
            _filesRoot = filesRoot;
        }

        public string FilesRoot
        {
            get { return _filesRoot; }
        }

        public string ExePath(ProgramItem program)
        {
            return ManifestReader.ResolveExe(_filesRoot, program);
        }

        /// <summary>Inicia o programa; devolve o processo, ou null (já mostrou a mensagem de erro).</summary>
        public Process Start(ProgramItem program)
        {
            var exe = ExePath(program);
            if (exe == null)
            {
                Error("O caminho do programa \"" + program.Name + "\" é inválido.");
                return null;
            }

            if (!File.Exists(exe))
            {
                Error("O arquivo de \"" + program.Name + "\" não foi encontrado:\n" + exe);
                return null;
            }

            return StartFile(exe, program.Arguments, Path.GetDirectoryName(exe), program.Name);
        }

        public Process StartFile(string file, string arguments, string workingDirectory, string displayName)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    UseShellExecute = false,
                };
                AppLog.Write("Iniciando: " + file + " " + arguments);
                return Process.Start(info);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                AppLog.Write("Falha ao iniciar " + file + ": " + ex);
                Error("Não foi possível iniciar \"" + displayName + "\".\n\n" + Explain(file, ex));
                return null;
            }
        }

        /// <summary>
        /// Motivo mais provável da falha em palavras claras. O caso típico: programa de 32 bits no WinPE de 64 bits (que não tem
        /// WOW64), onde o Windows só diz "side-by-side configuration is incorrect".
        /// </summary>
        private static string Explain(string file, Exception ex)
        {
            var kind = PeInfo.GetKind(file);
            if (kind == ExecutableKind.X86 && Environment.Is64BitOperatingSystem && !File.Exists(Path.Combine(Environment.SystemDirectory, "wow64.dll")))
            {
                return "Este programa é de 32 bits (x86) e este WinPE é de 64 bits, que não executa programas de 32 bits.\n\n"
                    + "Use a versão de 64 bits do programa ou gere um WinPE de 32 bits.\n\nDetalhe técnico: " + ex.Message;
            }

            if (kind == ExecutableKind.X64 && !Environment.Is64BitOperatingSystem)
            {
                return "Este programa é de 64 bits e este WinPE é de 32 bits, que não executa programas de 64 bits.\n\nDetalhe técnico: " + ex.Message;
            }

            return ex.Message;
        }

        public static void Error(string message)
        {
            MessageBox.Show(message, "WinPE Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Ícone do executável. 1) lê os recursos do próprio .exe (funciona no WinPE, que não tem as APIs de ícone do shell);
        /// 2) se não houver (ex.: .cmd/.bat), pergunta ao Windows; 3) cai no ícone padrão. Quem chama é dono do Bitmap devolvido.
        /// </summary>
        public static Bitmap LoadIcon(string exePath, int size)
        {
            if (exePath != null && File.Exists(exePath))
            {
                var fromResources = IconFromResources(exePath, size);
                if (fromResources != null)
                {
                    return fromResources;
                }

                try
                {
                    using (var icon = Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null)
                        {
                            return Resized(icon.ToBitmap(), size);
                        }
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is Win32Exception || ex is UnauthorizedAccessException)
                {
                    AppLog.Write("Sem ícone do shell para " + exePath + ": " + ex.Message);
                }
            }

            return Resized(SystemIcons.Application.ToBitmap(), size);
        }

        private static Bitmap IconFromResources(string exePath, int size)
        {
            try
            {
                var ico = PeIconReader.ReadIcoFile(exePath);
                var image = PeIconReader.Pick(PeIconReader.ParseIco(ico), size);
                if (image == null)
                {
                    return null;
                }

                if (image.IsPng)
                {
                    using (var stream = new MemoryStream(image.Data))
                    using (var png = new Bitmap(stream))
                    {
                        return Resized(png, size);
                    }
                }

                // imagem em formato DIB: o Windows converte um .ico de uma imagem só
                using (var single = new MemoryStream(SingleImageIco(image)))
                using (var icon = new Icon(single))
                {
                    return Resized(icon.ToBitmap(), size);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is OutOfMemoryException || ex is InvalidOperationException)
            {
                // GDI+ lança OutOfMemoryException para dados que não são imagem
                AppLog.Write("Ícone dos recursos ilegível em " + exePath + ": " + ex.Message);
                return null;
            }
        }

        private static byte[] SingleImageIco(PeIconReader.IconImage image)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)1);
                w.Write((byte)(image.Width >= 256 ? 0 : image.Width));
                w.Write((byte)(image.Height >= 256 ? 0 : image.Height));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)image.BitCount);
                w.Write((uint)image.Data.Length);
                w.Write((uint)22);
                w.Write(image.Data);
                return ms.ToArray();
            }
        }

        /// <summary>Redimensiona com boa qualidade para <paramref name="size"/> x <paramref name="size"/> mantendo a transparência.</summary>
        private static Bitmap Resized(Image source, int size)
        {
            var result = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, size, size));
            }

            return result;
        }
    }
}
