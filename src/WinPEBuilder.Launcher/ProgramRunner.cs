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

        /// <summary>Ícone do executável (o mesmo do Explorer); cai no ícone padrão se não der.</summary>
        public static Bitmap LoadIcon(string exePath, int size)
        {
            try
            {
                if (exePath != null && File.Exists(exePath))
                {
                    using (var icon = Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null)
                        {
                            return new Bitmap(icon.ToBitmap(), new Size(size, size));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is Win32Exception || ex is UnauthorizedAccessException)
            {
                AppLog.Write("Sem ícone para " + exePath + ": " + ex.Message);
            }

            return new Bitmap(SystemIcons.Application.ToBitmap(), new Size(size, size));
        }
    }
}
