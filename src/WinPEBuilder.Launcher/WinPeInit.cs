using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinPEBuilder.Launcher
{
    /// <summary>
    /// Inicialização do WinPE feita pelo próprio launcher (ele é o shell, iniciado pelo winpeshl.ini): mapeia a pasta Arquivos como
    /// Z:, define %ARQUIVOS%, roda o <c>wpeinit</c> em SEGUNDO PLANO (drivers, rede; pode demorar) e, quando ele termina, o
    /// startup.cmd do usuário. O desktop aparece na hora e a barra mostra "Iniciando…" enquanto o wpeinit trabalha — antes, o
    /// desktop só aparecia depois do wpeinit e o usuário via só um prompt vazio por muito tempo.
    /// </summary>
    internal sealed class WinPeInit
    {
        public const string StatusText = "Iniciando o WinPE (drivers e rede)…";

        private readonly string _filesRoot;
        private readonly ProgramRunner _runner;
        private Process _wpeinit;

        public WinPeInit(string filesRoot, ProgramRunner runner)
        {
            _filesRoot = filesRoot;
            _runner = runner;
        }

        /// <summary>true se estamos dentro de um WinPE (chave MiniNT do registro, a mesma que o próprio Windows usa).</summary>
        public static bool IsWinPE()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT"))
                {
                    return key != null;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                return false;
            }
        }

        public void Start()
        {
            MapFilesDrive();
            StartWpeInit();
        }

        /// <summary>
        /// Texto de andamento para a barra ("Iniciando…") enquanto o wpeinit roda; null quando terminou. Na primeira vez em que vê
        /// o wpeinit terminado, dispara o startup.cmd do usuário.
        /// </summary>
        public string Poll()
        {
            if (_wpeinit == null)
            {
                return null;
            }

            bool exited;
            try
            {
                exited = _wpeinit.HasExited;
            }
            catch (InvalidOperationException)
            {
                exited = true;
            }

            if (!exited)
            {
                return StatusText;
            }

            AppLog.Write("wpeinit terminou");
            _wpeinit.Dispose();
            _wpeinit = null;
            RunStartupScript();
            return null;
        }

        private void MapFilesDrive()
        {
            if (string.IsNullOrEmpty(_filesRoot))
            {
                return;
            }

            Environment.SetEnvironmentVariable("ARQUIVOS", _filesRoot);   // herdada pelos programas iniciados pelo launcher
            if (Directory.Exists("Z:\\"))
            {
                return;   // letra em uso: %ARQUIVOS% continua valendo
            }

            try
            {
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "subst.exe"), "Z: \"" + _filesRoot.TrimEnd('\\') + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(info))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                    }
                }

                AppLog.Write("Z: mapeada para " + _filesRoot);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                AppLog.Write("Não foi possível mapear Z: " + ex.Message);
            }
        }

        private void StartWpeInit()
        {
            var path = Path.Combine(Environment.SystemDirectory, "wpeinit.exe");
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                _wpeinit = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true });
                AppLog.Write("wpeinit iniciado em segundo plano");
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                AppLog.Write("Não foi possível iniciar o wpeinit: " + ex.Message);
            }
        }

        private void RunStartupScript()
        {
            var script = "Z:\\startup.cmd";
            if (!File.Exists(script) && !string.IsNullOrEmpty(_filesRoot))
            {
                script = Path.Combine(_filesRoot, "startup.cmd");
            }

            if (File.Exists(script))
            {
                _runner.StartFile(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c call \"" + script + "\"", Path.GetDirectoryName(script), "startup.cmd");
            }
        }
    }
}
