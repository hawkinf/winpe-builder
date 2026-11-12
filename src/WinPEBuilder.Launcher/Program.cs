using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Launcher
{
    internal static class Program
    {
        /// <summary>
        /// Uso: WinPELauncher.exe [--preview] [--manifest "C:\...\programs.json"]
        /// Sem argumentos: acha a pasta Arquivos por %ARQUIVOS% (ou varrendo as unidades) e vira a área de trabalho.
        /// --preview: janela normal para testar no Windows comum (não mexe na área de trabalho nem desliga nada).
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            var preview = false;
            string manifestPath = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--preview", StringComparison.OrdinalIgnoreCase))
                {
                    preview = true;
                }
                else if (string.Equals(args[i], "--manifest", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    manifestPath = args[++i];
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => AppLog.Write("Erro não tratado: " + e.Exception);

            string filesRoot;
            if (manifestPath != null)
            {
                filesRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            }
            else
            {
                filesRoot = ManifestReader.FindFilesRoot(Environment.GetEnvironmentVariable("ARQUIVOS"), File.Exists);
                manifestPath = filesRoot == null ? null : Path.Combine(filesRoot, ManifestReader.ManifestFileName);
            }

            var programs = new List<ProgramItem>();
            var layout = new List<LayoutEntry>();
            var menuOrder = new List<string>();
            string notice = null;
            if (manifestPath == null || !File.Exists(manifestPath))
            {
                notice = "programs.json não encontrado: nenhum programa foi configurado nesta mídia.";
                AppLog.Write(notice);
            }
            else
            {
                try
                {
                    var manifest = ManifestReader.Load(manifestPath);
                    programs = manifest.Programs;
                    layout = manifest.Layout;
                    menuOrder = manifest.MenuOrder;
                    AppLog.Write("Manifesto carregado: " + manifestPath + " (" + programs.Count + " programa(s))");
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    notice = "Não foi possível ler o programs.json: " + ex.Message;
                    AppLog.Write(notice);
                }
            }

            var runner = new ProgramRunner(filesRoot ?? string.Empty);
            using (var desktop = new DesktopForm(runner, programs, layout, menuOrder, preview, notice))
            {
                Application.Run(desktop);
            }

            return 0;
        }
    }
}
