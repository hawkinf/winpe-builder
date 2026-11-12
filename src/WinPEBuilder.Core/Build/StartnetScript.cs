using System.Text;

namespace WinPEBuilder.Core.Build;

/// <summary>Gera o startnet.cmd que o WinPE executa ao iniciar, para dar acesso aos arquivos do usuário.</summary>
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

    /// <summary>
    /// Texto do startnet.cmd. Só ASCII (o WinPE lê na página OEM), com CRLF.
    /// Define %ARQUIVOS% e a unidade <see cref="DriveLetter"/>:, e executa startup.cmd dela se existir.
    /// Se a letra já estiver em uso o subst falha em silêncio, mas %ARQUIVOS% continua correto.
    /// </summary>
    public static string Build(FilesMode mode, bool launcher = false)
    {
        var lines = new List<string> { "@echo off", "wpeinit" };
        var launcherLine = $"start \"\" \"%SystemRoot%\\System32\\{LauncherFileName}\"";

        switch (mode)
        {
            case FilesMode.None:
                if (launcher)
                {
                    lines.Add(launcherLine);
                }

                break;

            case FilesMode.InsideImage:
                lines.Add($"rem WinPE Builder: arquivos dentro da imagem (RAM)");
                lines.Add($"if exist X:\\{FilesFolderName}\\{MarkerFileName} (");
                lines.Add($"  set ARQUIVOS=X:\\{FilesFolderName}");
                lines.Add($"  subst {DriveLetter}: X:\\{FilesFolderName} 2>nul");
                lines.Add(")");
                AddTail(lines, launcher ? launcherLine : null);
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
                AddTail(lines, launcher ? launcherLine : null);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo desconhecido.");
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Launcher primeiro (a área de trabalho aparece mesmo que o startup.cmd demore), depois o startup.cmd do usuário.</summary>
    private static void AddTail(List<string> lines, string? launcherLine)
    {
        if (launcherLine is not null)
        {
            lines.Add(launcherLine);
        }

        lines.Add($"if exist {DriveLetter}:\\startup.cmd call {DriveLetter}:\\startup.cmd");
    }

    public static byte[] ToBytes(string script) => Encoding.ASCII.GetBytes(script);
}
