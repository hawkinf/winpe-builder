using System.Text.RegularExpressions;

namespace WinPEBuilder.Core.Programs;

/// <summary>Resultado da busca de executáveis numa pasta.</summary>
public sealed record ScanResult(IReadOnlyList<string> Files, bool Truncated);

/// <summary>Procura os executáveis de uma pasta ("Adicionar pasta"), ignorando o que não é programa de uso.</summary>
public static class ProgramScanner
{
    public const int DefaultMaxDepth = 3;
    public const int DefaultMaxResults = 300;

    // Desinstaladores e instaladores de runtime não fazem sentido num menu do WinPE.
    private static readonly Regex Noise = new(
        @"^(unins\w*|uninst\w*|uninstall\w*|vc_?redist[\w.\-]*|dotnet[\w.\-]*(setup|install)[\w.\-]*|dxsetup|crashpad_handler|crashreporter)\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Lista os .exe da pasta e subpastas (até <paramref name="maxDepth"/> níveis; 0 = só a pasta), em ordem alfabética
    /// do caminho. Não segue links/junctions e ignora pastas sem permissão.
    /// </summary>
    public static ScanResult FindExecutables(string root, int maxDepth = DefaultMaxDepth, int maxResults = DefaultMaxResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"A pasta não existe: {full}");
        }

        var found = new List<string>();
        var truncated = false;
        Walk(full, 0);
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return new ScanResult(found, truncated);

        void Walk(string directory, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (truncated)
            {
                return;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.exe"))
                {
                    if (Noise.IsMatch(Path.GetFileName(file)))
                    {
                        continue;
                    }

                    if (found.Count >= maxResults)
                    {
                        truncated = true;
                        return;
                    }

                    found.Add(file);
                }

                if (depth >= maxDepth)
                {
                    return;
                }

                foreach (var sub in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    Walk(sub, depth + 1);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // pasta sem permissão ou que sumiu: segue com as outras
            }
        }
    }

    /// <summary>Nome amigável a partir do arquivo ("Process Explorer.exe" → "Process Explorer"; "procexp64" → "procexp64").</summary>
    public static string SuggestName(string exePath) => Path.GetFileNameWithoutExtension(exePath);
}
