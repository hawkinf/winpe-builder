using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace WinPEBuilder.App;

/// <summary>
/// Entrada do aplicativo. O DISM (montar/desmontar WIM) e o copype exigem administrador. O manifesto já pede elevação, mas
/// ele só vale quando o programa é aberto pelo WinPEBuilder.exe; iniciado por "dotnet run", pelo depurador ou por
/// "dotnet WinPEBuilder.dll" o manifesto é ignorado e a geração falhava no meio com "Erro: 740". Por isso o próprio programa
/// confere e, se preciso, reabre a si mesmo como administrador.
/// </summary>
public partial class App : Application
{
    /// <summary>Variável que desliga a reabertura elevada (uso em automação/testes de interface).</summary>
    public const string NoElevateVariable = "WINPEBUILDER_NO_ELEVATE";

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsElevated() && Environment.GetEnvironmentVariable(NoElevateVariable) is null)
        {
            if (!RelaunchElevated(e.Args))
            {
                MessageBox.Show(
                    "O WinPE Builder precisa ser executado como administrador (o DISM e o copype exigem isso).\n\n" +
                    "Feche esta janela e abra o programa de novo aceitando o pedido de permissão do Windows " +
                    "(ou clique com o botão direito e escolha \"Executar como administrador\").",
                    "Permissão de administrador necessária", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reabre o programa pedindo elevação (UAC). false se o usuário recusou ou não foi possível.</summary>
    private static bool RelaunchElevated(string[] args)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }

            var arguments = new List<string>();
            // Aberto pelo "dotnet WinPEBuilder.dll": o processo é o dotnet.exe e o programa vai como primeiro argumento.
            if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                // Assembly.Location é vazio em single-file (erro IL3000); o primeiro argumento da linha de comando é a dll.
                var dll = Environment.GetCommandLineArgs().FirstOrDefault();
                if (string.IsNullOrEmpty(dll) || !dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(dll))
                {
                    return false;
                }

                arguments.Add(dll);
            }

            arguments.AddRange(args);
            var info = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            return Process.Start(info) is not null;
        }
        catch (Win32Exception)
        {
            return false; // pedido de permissão recusado (erro 1223) ou programa não pôde ser iniciado
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
