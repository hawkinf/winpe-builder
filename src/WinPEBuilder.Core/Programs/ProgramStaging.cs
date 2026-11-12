using System.Text.Encodings.Web;
using System.Text.Json;
using WinPEBuilder.Core.Build;

namespace WinPEBuilder.Core.Programs;

/// <summary>Copia os programas para a pasta "Arquivos" do WinPE e escreve o programs.json que o launcher lê.</summary>
public static class ProgramStaging
{
    /// <summary>Pasta (dentro de Arquivos) onde cada programa (ou grupo de programas da mesma pasta) ganha a sua subpasta.</summary>
    public const string ProgramsFolderName = "Programas";

    /// <summary>Manifesto lido pelo launcher, na raiz da pasta Arquivos.</summary>
    public const string ManifestFileName = "programs.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static bool SameFolder(ProgramEntry a, ProgramEntry b) =>
        string.Equals(a.SourceFolder, b.SourceFolder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Nome da subpasta de destino. Programas com "copiar a pasta inteira" que vêm da MESMA pasta de origem
    /// compartilham uma única cópia (a subpasta tem o id do primeiro deles); os demais usam o próprio id.
    /// </summary>
    public static string StageFolderName(IReadOnlyList<ProgramEntry> all, ProgramEntry program)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(program);
        return program.CopyFolder
            ? all.FirstOrDefault(o => o.CopyFolder && SameFolder(o, program))?.Id ?? program.Id
            : program.Id;
    }

    /// <summary>Caminho do executável DENTRO da pasta Arquivos (relativo, com "\"), considerando cópias compartilhadas.</summary>
    public static string RelativeExe(IReadOnlyList<ProgramEntry> all, ProgramEntry program)
    {
        var inner = program.CopyFolder
            ? Path.GetRelativePath(program.SourceFolder, Path.GetFullPath(program.SourceExe))
            : Path.GetFileName(program.SourceExe);
        return Path.Combine(ProgramsFolderName, StageFolderName(all, program), inner);
    }

    /// <summary>Atalho para um programa isolado.</summary>
    public static string RelativeExe(ProgramEntry program) => RelativeExe([program], program);

    /// <summary>
    /// Texto do programs.json (versão 1). Com <paramref name="layout"/> não vazio, grava também a posição de cada ícone da
    /// área de trabalho (campo opcional "layout"); sem ele o launcher arruma os ícones sozinho.
    /// </summary>
    public static string BuildManifest(IReadOnlyList<ProgramEntry> programs, IReadOnlyDictionary<string, DesktopSlot>? layout = null)
    {
        ArgumentNullException.ThrowIfNull(programs);
        var slots = layout is { Count: > 0 }
            ? DesktopLayoutHelper.Normalize(DesktopLayoutHelper.Items(programs), layout)
            : null;
        var manifest = new
        {
            version = 1,
            programs = programs.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                category = p.Category,
                exe = RelativeExe(programs, p),
                arguments = p.Arguments ?? string.Empty,
                desktop = p.ShowOnDesktop,
                startMenu = p.ShowInStartMenu,
                folder = p.DesktopFolder ?? string.Empty,
            }),
            layout = slots?.Select(kv => new { key = kv.Key, col = kv.Value.Col, row = kv.Value.Row }).ToArray(),
        };
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    /// <summary>Soma dos bytes que serão copiados (para avisos de tamanho); pastas compartilhadas contam uma vez.</summary>
    public static long TotalBytes(IReadOnlyList<ProgramEntry> programs)
    {
        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in programs)
        {
            if (!p.CopyFolder)
            {
                total += new FileInfo(p.SourceExe).Length;
            }
            else if (seen.Add(p.SourceFolder))
            {
                total += Directory.EnumerateFiles(p.SourceFolder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            }
        }

        return total;
    }

    /// <summary>Copia cada programa para Arquivos\Programas\&lt;pasta&gt; e grava o manifesto em Arquivos\programs.json.</summary>
    public static void Stage(IReadOnlyList<ProgramEntry> programs, string filesRoot, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, DesktopSlot>? layout = null)
    {
        ArgumentNullException.ThrowIfNull(programs);
        if (programs.Count == 0)
        {
            return;
        }

        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in programs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(filesRoot, ProgramsFolderName, StageFolderName(programs, program));
            if (program.CopyFolder)
            {
                if (copied.Add(destination))
                {
                    FileSystemHelper.CopyDirectory(program.SourceFolder, destination, cancellationToken);
                }
            }
            else
            {
                Directory.CreateDirectory(destination);
                var target = Path.Combine(destination, Path.GetFileName(program.SourceExe));
                File.Copy(program.SourceExe, target, overwrite: true);
                File.SetAttributes(target, FileAttributes.Normal);
            }
        }

        // UTF-8 sem BOM: o launcher (DataContractJsonSerializer) lê direto.
        File.WriteAllText(Path.Combine(filesRoot, ManifestFileName), BuildManifest(programs, layout), new System.Text.UTF8Encoding(false));
    }
}
