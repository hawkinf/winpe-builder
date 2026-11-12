using System.Text;

namespace WinPEBuilder.Core.Build;

/// <summary>Gera o startnet.cmd (e, com launcher, o winpeshl.ini) que o WinPE executa ao iniciar.</summary>
public static class StartnetScript
{
    /// <summary>Nome da pasta de arquivos, na raiz da ISO ou em X:\.</summary>
    public const string FilesFolderName = "Arquivos";

    /// <summary>Arquivo-marca criado dentro da pasta, usado pelo script para achar a unidade certa.</summary>
    public const string MarkerFileName = ".winpe-files";

    /// <summary>Letra de unidade (rara de estar em uso no WinPE) atribuída à pasta de arquivos dentro do WinPE.</summary>
    public const char DriveLetter = 'Z';

    /// <summary>Nome do launcher dentro de X:\Windows\System32.</summary>
    public const string LauncherFileName = "WinPELauncher.exe";

    /// <summary>Arquivo que troca o shell padrão do WinPE (um cmd.exe) pelo launcher.</summary>
    public const string WinpeshlFileName = "winpeshl.ini";

    /// <summary>
    /// Texto do startnet.cmd. Só ASCII (o WinPE lê na página OEM), com CRLF.
    /// Define %ARQUIVOS% e a unidade <see cref="DriveLetter"/>:, e executa startup.cmd dela se existir.
    /// Se a letra já estiver em uso o subst falha em silêncio, mas %ARQUIVOS% continua correto.
    /// </summary>
    /// <param name="mode">Onde estão os arquivos do usuário.</param>
    /// <param name="launcher">
    /// true = o launcher é o shell (iniciado pelo winpeshl.ini DEPOIS deste script): então o startup.cmd roda numa janela
    /// própria, sem bloquear, para o desktop aparecer logo. false = o próprio cmd.exe do startnet é o shell (padrão do WinPE).
    /// </param>
    public static string Build(FilesMode mode, bool launcher = false)
    {
        var lines = new List<string> { "@echo off", "wpeinit" };

        switch (mode)
        {
            case FilesMode.None:
                break;

            case FilesMode.InsideImage:
                lines.Add($"rem WinPE Builder: arquivos dentro da imagem (RAM)");
                lines.Add($"if exist X:\\{FilesFolderName}\\{MarkerFileName} (");
                lines.Add($"  set ARQUIVOS=X:\\{FilesFolderName}");
                lines.Add($"  subst {DriveLetter}: X:\\{FilesFolderName} 2>nul");
                lines.Add(")");
                AddStartupHook(lines, launcher);
                break;

            case FilesMode.OnIso:
                lines.Add($"rem WinPE Builder: procura a midia (CD/USB) que contem a pasta {FilesFolderName}");
                lines.Add("for %%d in (C D E F G H I J K L M N O P Q R S T U V W Y Z) do (");
                lines.Add($"  if exist %%d:\\{FilesFolderName}\\{MarkerFileName} (");
                lines.Add($"    set ARQUIVOS=%%d:\\{FilesFolderName}");
                lines.Add($"    subst {DriveLetter}: %%d:\\{FilesFolderName} 2>nul");
                lines.Add("    goto :arquivos_ok");
                lines.Add("  )");
                lines.Add(")");
                lines.Add(":arquivos_ok");
                AddStartupHook(lines, launcher);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo desconhecido.");
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// Sem launcher o startup.cmd roda aqui mesmo (bloqueando, como sempre foi). Com launcher ele vai para uma janela própria
    /// (<c>start</c>): senão o desktop só apareceria depois que o script do usuário terminasse.
    /// </summary>
    private static void AddStartupHook(List<string> lines, bool launcher)
    {
        var startup = $"{DriveLetter}:\\startup.cmd";
        lines.Add(launcher
            ? $"if exist {startup} start \"startup.cmd\" cmd.exe /c call {startup}"
            : $"if exist {startup} call {startup}");
    }

    /// <summary>
    /// winpeshl.ini que faz do launcher o shell do WinPE. O winpeshl executa os itens de [LaunchApps] em sequência, um depois do
    /// outro: (1) o startnet.cmd (wpeinit, unidade Z:, startup.cmd) numa janela que fecha sozinha; (2) o launcher, que fica
    /// aberto (é o shell — se ele terminar, o WinPE reinicia o PC); (3) um prompt de comando, que só aparece se o launcher
    /// travar ou terminar, para nunca cair num loop de reinicialização. Só ASCII, com CRLF.
    /// </summary>
    public static string BuildWinpeshlIni() => string.Join(
        "\r\n",
        "[LaunchApps]",
        @"%SYSTEMROOT%\System32\cmd.exe, /c %SYSTEMROOT%\System32\startnet.cmd",
        $@"%SYSTEMROOT%\System32\{LauncherFileName}",
        @"%SYSTEMROOT%\System32\cmd.exe") + "\r\n";

    public static byte[] ToBytes(string script) => Encoding.ASCII.GetBytes(script);
}
