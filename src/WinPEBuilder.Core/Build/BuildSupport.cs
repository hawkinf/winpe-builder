using System.Text.RegularExpressions;

namespace WinPEBuilder.Core.Build;

/// <summary>Erro esperado do processo de geração, com mensagem pronta para o usuário.</summary>
public sealed class BuildException(string message) : Exception(message);

/// <summary>Andamento da geração: etapa atual de N.</summary>
public sealed record BuildProgress(int Step, int Total, string Message);

/// <summary>Caminhos derivados da pasta de trabalho (mesmo layout do copype).</summary>
public sealed record BuildLayout(string WorkDirectory)
{
    public string Media => Path.Combine(WorkDirectory, "media");
    public string Mount => Path.Combine(WorkDirectory, "mount");
    public string FwFiles => Path.Combine(WorkDirectory, "fwfiles");
    public string SourcesDirectory => Path.Combine(Media, "sources");
    public string BootWim => Path.Combine(SourcesDirectory, "boot.wim");
}

internal static partial class BuildText
{
    /// <summary>Linhas de barra de progresso do DISM ("[====  50,0%  ]") — ruído no log.</summary>
    [GeneratedRegex(@"^\s*\[[= ]*\d+([.,]\d+)?%[= ]*\]\s*$")]
    private static partial Regex DismProgressLine();

    public static bool IsProgressNoise(string line) => DismProgressLine().IsMatch(line);
}

internal static class FileSystemHelper
{
    /// <summary>Apaga uma pasta mesmo com arquivos somente leitura (comum em cópias de ISO).</summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>Copia uma pasta inteira; arquivos copiados perdem o atributo somente leitura.</summary>
    public static void CopyDirectory(string source, string destination, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target, overwrite: true);
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    /// <summary>True se <paramref name="path"/> está estritamente dentro de <paramref name="root"/>.</summary>
    public static bool IsInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
