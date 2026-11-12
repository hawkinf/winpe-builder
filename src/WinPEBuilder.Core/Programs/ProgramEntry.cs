using System.Text;
using System.Text.RegularExpressions;

namespace WinPEBuilder.Core.Programs;

/// <summary>Um programa que aparecerá no desktop e/ou no menu Iniciar do WinPE.</summary>
public sealed record ProgramEntry
{
    /// <summary>Identificador estável e seguro para nome de pasta (a-z, 0-9, "-").</summary>
    public string Id { get; init; } = string.Empty;

    public required string Name { get; init; }

    /// <summary>Grupo no menu Iniciar (ex.: "Diagnóstico").</summary>
    public string Category { get; init; } = DefaultCategory;

    /// <summary>Caminho do executável no PC onde o app roda (origem da cópia).</summary>
    public required string SourceExe { get; init; }

    public string Arguments { get; init; } = string.Empty;

    /// <summary>
    /// Copia a pasta inteira do executável (programas portáteis precisam dos arquivos vizinhos).
    /// Se false, copia só o executável.
    /// </summary>
    public bool CopyFolder { get; init; } = true;

    public bool ShowOnDesktop { get; init; } = true;

    public bool ShowInStartMenu { get; init; } = true;

    /// <summary>
    /// Pasta raiz escolhida pelo usuário quando o programa veio de "Adicionar pasta" (o exe pode estar numa subpasta).
    /// Vazio = a pasta do próprio executável. Só vale com <see cref="CopyFolder"/>.
    /// </summary>
    public string SourceRoot { get; init; } = string.Empty;

    /// <summary>
    /// Nome de uma pasta que aparece na área de trabalho e abre uma janela com os programas que têm o mesmo nome.
    /// Vazio = o programa não pertence a nenhuma pasta da área de trabalho.
    /// </summary>
    public string DesktopFolder { get; init; } = string.Empty;

    public const string DefaultCategory = "Programas";

    private static readonly string[] AllowedExtensions = [".exe", ".cmd", ".bat", ".com"];

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,40}$", RegexOptions.CultureInvariant);

    /// <summary>Pasta de origem que será copiada.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceFolder => !CopyFolder || string.IsNullOrWhiteSpace(SourceRoot)
        ? Path.GetDirectoryName(Path.GetFullPath(SourceExe))!
        : Path.GetFullPath(SourceRoot).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>true se o executável está dentro de <see cref="SourceFolder"/> (sempre, exceto raiz mal informada).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ExeIsInsideSourceFolder =>
        Path.GetFullPath(SourceExe).StartsWith(SourceFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gera um id único a partir do nome ("HWiNFO 64" → "hwinfo-64").</summary>
    public static string MakeId(string name, IEnumerable<string> existingIds)
    {
        var builder = new StringBuilder();
        foreach (var ch in (name ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue; // tira acentos
            }

            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            slug = "programa";
        }

        if (slug.Length > 36)
        {
            slug = slug[..36].TrimEnd('-');
        }

        var taken = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var candidate = slug;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{slug}-{n}";
        }

        return candidate;
    }

    /// <summary>Valida uma lista de programas; devolve mensagens amigáveis (vazia = ok).</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<ProgramEntry> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in programs)
        {
            var label = string.IsNullOrWhiteSpace(p.Name) ? "(sem nome)" : p.Name;

            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60)
            {
                errors.Add($"Programa \"{label}\": informe um nome de até 60 caracteres.");
            }

            var id = p.Id ?? string.Empty;
            if (!IdPattern.IsMatch(id))
            {
                errors.Add($"Programa \"{label}\": identificador interno inválido.");
            }
            else if (!ids.Add(id))
            {
                errors.Add($"Programa \"{label}\": identificador repetido ({p.Id}).");
            }

            if (string.IsNullOrWhiteSpace(p.Category) || p.Category.Length > 40 || p.Category.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                errors.Add($"Programa \"{label}\": categoria inválida (até 40 caracteres, sem \\ / : * ? \" < > |).");
            }

            if ((p.Arguments?.Length ?? 0) > 500)
            {
                errors.Add($"Programa \"{label}\": argumentos longos demais (máximo 500 caracteres).");
            }

            if (string.IsNullOrWhiteSpace(p.SourceExe) || !File.Exists(p.SourceExe))
            {
                errors.Add($"Programa \"{label}\": o arquivo não existe mais: {p.SourceExe}");
                continue;
            }

            if (!AllowedExtensions.Contains(Path.GetExtension(p.SourceExe), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Programa \"{label}\": só são aceitos {string.Join(", ", AllowedExtensions)}.");
            }

            if ((p.DesktopFolder?.Length ?? 0) > 40 || (p.DesktopFolder ?? string.Empty).Any(char.IsControl))
            {
                errors.Add($"Programa \"{label}\": o nome da pasta da área de trabalho deve ter até 40 caracteres.");
            }

            if (p.CopyFolder && !p.ExeIsInsideSourceFolder)
            {
                errors.Add($"Programa \"{label}\": o executável precisa estar dentro da pasta {p.SourceFolder}.");
                continue;
            }

            if (p.CopyFolder && IsProtectedFolder(p.SourceFolder))
            {
                errors.Add($"Programa \"{label}\": copiar a pasta inteira de {p.SourceFolder} traria arquivos demais (pasta do sistema ou do seu perfil). "
                    + "Coloque o programa numa pasta própria ou desmarque \"copiar a pasta inteira\".");
            }
        }

        return errors;
    }

    /// <summary>
    /// Pastas que nunca devem ser copiadas por inteiro: raiz de unidade, Windows, Program Files, o perfil do
    /// usuário, Desktop/Documentos/Downloads — e qualquer pasta que CONTENHA uma delas (ex.: C:\Users).
    /// </summary>
    public static bool IsProtectedFolder(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetPathRoot(full)!.TrimEnd(Path.DirectorySeparatorChar).Equals(full, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var protectedFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            profile,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            string.IsNullOrEmpty(profile) ? string.Empty : Path.Combine(profile, "Downloads"),
        }.Where(f => !string.IsNullOrEmpty(f)).Select(f => Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar));

        foreach (var protectedFolder in protectedFolders)
        {
            if (full.Equals(protectedFolder, StringComparison.OrdinalIgnoreCase) ||
                protectedFolder.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
