using System.Text.Encodings.Web;
using System.Text.Json;
using WinPEBuilder.Core.Build;

namespace WinPEBuilder.Core.Programs;

/// <summary>Copia os programas para a pasta "Arquivos" do WinPE e escreve o programs.json que o launcher lê.</summary>
public static class ProgramStaging
{
    /// <summary>Pasta (dentro de Arquivos) onde cada programa ganha a sua subpasta.</summary>
    public const string ProgramsFolderName = "Programas";

    /// <summary>Manifesto lido pelo launcher, na raiz da pasta Arquivos.</summary>
    public const string ManifestFileName = "programs.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Caminho do executável DENTRO da pasta Arquivos (relativo, com "\").</summary>
    public static string RelativeExe(ProgramEntry program) =>
        Path.Combine(ProgramsFolderName, program.Id, Path.GetFileName(program.SourceExe));

    /// <summary>Texto do programs.json (versão 1).</summary>
    public static string BuildManifest(IReadOnlyList<ProgramEntry> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);
        var manifest = new
        {
            version = 1,
            programs = programs.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                category = p.Category,
                exe = RelativeExe(p),
                arguments = p.Arguments ?? string.Empty,
                desktop = p.ShowOnDesktop,
                startMenu = p.ShowInStartMenu,
            }),
        };
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    /// <summary>Soma dos bytes que serão copiados (para avisos de tamanho).</summary>
    public static long TotalBytes(IReadOnlyList<ProgramEntry> programs) =>
        programs.Sum(p => p.CopyFolder
            ? Directory.EnumerateFiles(p.SourceFolder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : new FileInfo(p.SourceExe).Length);

    /// <summary>Copia cada programa para Arquivos\Programas\&lt;id&gt; e grava o manifesto em Arquivos\programs.json.</summary>
    public static void Stage(IReadOnlyList<ProgramEntry> programs, string filesRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(programs);
        if (programs.Count == 0)
        {
            return;
        }

        foreach (var program in programs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(filesRoot, ProgramsFolderName, program.Id);
            if (program.CopyFolder)
            {
                FileSystemHelper.CopyDirectory(program.SourceFolder, destination, cancellationToken);
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
        File.WriteAllText(Path.Combine(filesRoot, ManifestFileName), BuildManifest(programs), new System.Text.UTF8Encoding(false));
    }
}
