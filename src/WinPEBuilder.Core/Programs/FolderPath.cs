namespace WinPEBuilder.Core.Programs;

/// <summary>
/// Caminho de uma pasta da área de trabalho: níveis separados por "/" ("Ferramentas/Discos"). As pastas não existem
/// sozinhas: são derivadas do campo <see cref="ProgramEntry.DesktopFolder"/> dos programas.
/// </summary>
public static class FolderPath
{
    public const char Separator = '/';
    public const int MaxDepth = 4;
    public const int MaxSegmentLength = 40;
    public const int MaxTotalLength = 120;

    /// <summary>Níveis do caminho, já sem espaços nas pontas e sem níveis vazios.</summary>
    public static IReadOnlyList<string> Segments(string? path) =>
        (path ?? string.Empty).Split(Separator).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>
    /// Caminho normalizado ("A / B/" → "A/B"); string vazia se não há pasta; null se for inválido
    /// (nível longo demais, caractere de controle, profundidade ou tamanho total acima do limite).
    /// </summary>
    public static string? Normalize(string? path)
    {
        var segments = Segments(path);
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        if (segments.Count > MaxDepth || segments.Any(s => s.Length > MaxSegmentLength || s.Any(char.IsControl)))
        {
            return null;
        }

        var joined = string.Join(Separator, segments);
        return joined.Length > MaxTotalLength ? null : joined;
    }

    /// <summary>Chave estável da pasta (caixa e espaços não importam): "f:FERRAMENTAS/DISCOS".</summary>
    public static string Key(string? path) => "f:" + (Normalize(path) ?? (path ?? string.Empty).Trim()).ToUpperInvariant();

    /// <summary>Primeiro nível ("A/B/C" → "A"); vazio se não há pasta.</summary>
    public static string Top(string? path) => Segments(path).FirstOrDefault() ?? string.Empty;

    /// <summary>Último nível ("A/B/C" → "C").</summary>
    public static string Leaf(string? path) => Segments(path).LastOrDefault() ?? string.Empty;

    /// <summary>Pasta de cima ("A/B/C" → "A/B"; "A" → vazio).</summary>
    public static string Parent(string? path)
    {
        var segments = Segments(path);
        return segments.Count <= 1 ? string.Empty : string.Join(Separator, segments.Take(segments.Count - 1));
    }

    public static int Depth(string? path) => Segments(path).Count;

    /// <summary>true se <paramref name="path"/> é <paramref name="folder"/> ou está dentro dela (ignora caixa).</summary>
    public static bool IsSameOrInside(string? path, string? folder)
    {
        var a = Segments(path);
        var b = Segments(folder);
        return b.Count > 0 && a.Count >= b.Count && b.Select((s, i) => string.Equals(a[i], s, StringComparison.OrdinalIgnoreCase)).All(x => x);
    }

    public static bool Same(string? a, string? b) => Key(a) == Key(b);

    /// <summary>Troca o prefixo <paramref name="oldPrefix"/> de <paramref name="path"/> por <paramref name="newPrefix"/> (pode ser vazio).</summary>
    public static string Replace(string path, string oldPrefix, string newPrefix)
    {
        var rest = Segments(path).Skip(Segments(oldPrefix).Count);
        return string.Join(Separator, Segments(newPrefix).Concat(rest));
    }

    public static string Combine(string? parent, string name) =>
        string.Join(Separator, Segments(parent).Concat(Segments(name)));
}
