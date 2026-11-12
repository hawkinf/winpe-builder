namespace WinPEBuilder.Core.Programs;

/// <summary>Célula da grade da área de trabalho (coluna, linha), começando em 0,0 no canto superior esquerdo.</summary>
public sealed record DesktopSlot(int Col, int Row);

/// <summary>Um ícone da área de trabalho: um programa solto ou uma pasta (com os programas que ela contém).</summary>
public sealed record DesktopItem(string Key, bool IsFolder, string Label, ProgramEntry? Program, IReadOnlyList<ProgramEntry> Members);

/// <summary>
/// Grade da área de trabalho do WinPE. Tamanho de referência: tela de 1024x768 (a menor que o WinPE costuma usar),
/// descontando a barra de tarefas — assim nenhum ícone fica fora da tela em qualquer resolução maior.
/// </summary>
public static class DesktopLayoutHelper
{
    /// <summary>Lado de uma célula, em pixels (o launcher e o editor visual usam o mesmo valor).</summary>
    public const int CellSize = 80;

    public const int Columns = 12;

    public const int Rows = 9;

    /// <summary>Largura x altura de referência da tela, em pixels, e altura da barra de tarefas.</summary>
    public const int ScreenWidth = 1024;

    public const int ScreenHeight = 768;

    public const int TaskbarHeight = 42;

    public static string ProgramKey(ProgramEntry program) => "p:" + program.Id;

    public static string FolderKey(string folderName) => "f:" + (folderName ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Ícones da área de trabalho na mesma ordem do launcher: pastas primeiro (alfabética), depois os programas soltos
    /// (alfabética). Um programa dentro de uma pasta só aparece como ícone solto se também marcar "área de trabalho".
    /// </summary>
    public static IReadOnlyList<DesktopItem> Items(IReadOnlyList<ProgramEntry> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var items = new List<DesktopItem>();

        var folders = programs
            .Where(p => !string.IsNullOrWhiteSpace(p.DesktopFolder))
            .GroupBy(p => p.DesktopFolder.Trim(), comparer)
            .OrderBy(g => g.Key, comparer);
        foreach (var folder in folders)
        {
            var members = folder.OrderBy(p => p.Name, comparer).ToList();
            items.Add(new DesktopItem(FolderKey(folder.Key), true, folder.Key, null, members));
        }

        foreach (var program in programs.Where(p => p.ShowOnDesktop).OrderBy(p => p.Name, comparer))
        {
            items.Add(new DesktopItem(ProgramKey(program), false, program.Name, program, []));
        }

        return items;
    }

    public static bool InBounds(DesktopSlot slot) =>
        slot.Col is >= 0 and < Columns && slot.Row is >= 0 and < Rows;

    /// <summary>
    /// Layout completo e consistente: mantém as posições válidas (dentro da grade, sem repetir célula), descarta chaves que
    /// não existem mais e coloca o que ficou sem posição na primeira célula livre (de cima para baixo, depois a próxima coluna).
    /// </summary>
    public static Dictionary<string, DesktopSlot> Normalize(IReadOnlyList<DesktopItem> items, IReadOnlyDictionary<string, DesktopSlot>? layout)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new Dictionary<string, DesktopSlot>(StringComparer.Ordinal);
        var occupied = new HashSet<DesktopSlot>();

        foreach (var item in items)
        {
            if (layout is not null && layout.TryGetValue(item.Key, out var slot) && InBounds(slot) && occupied.Add(slot))
            {
                result[item.Key] = slot;
            }
        }

        foreach (var item in items)
        {
            if (result.ContainsKey(item.Key))
            {
                continue;
            }

            var free = FirstFree(occupied) ?? new DesktopSlot(Columns - 1, Rows - 1); // grade lotada: sobrepõe no canto (não perde o item)
            occupied.Add(free);
            result[item.Key] = free;
        }

        return result;
    }

    /// <summary>Primeira célula livre (coluna por coluna, de cima para baixo), ou null se a grade estiver cheia.</summary>
    public static DesktopSlot? FirstFree(ISet<DesktopSlot> occupied)
    {
        for (var col = 0; col < Columns; col++)
        {
            for (var row = 0; row < Rows; row++)
            {
                var slot = new DesktopSlot(col, row);
                if (!occupied.Contains(slot))
                {
                    return slot;
                }
            }
        }

        return null;
    }

    /// <summary>Célula sob um ponto (em pixels, relativo ao canto da área de ícones), limitada à grade.</summary>
    public static DesktopSlot SlotAt(double x, double y) => new(
        Math.Clamp((int)Math.Floor(x / CellSize), 0, Columns - 1),
        Math.Clamp((int)Math.Floor(y / CellSize), 0, Rows - 1));
}

/// <summary>Grupo do menu Iniciar: categoria (nome como o usuário digitou na primeira ocorrência) e seus programas, já na ordem final.</summary>
public sealed record StartMenuGroup(string Category, IReadOnlyList<ProgramEntry> Programs)
{
    public string Key => StartMenuModel.CategoryKey(Category);
}

/// <summary>
/// Menu Iniciar: só quem marcou "menu Iniciar", agrupado por categoria. A ordem é a escolhida à mão (lista de chaves
/// "c:CATEGORIA" e "p:id"); o que não estiver na lista vem depois, em ordem alfabética.
/// </summary>
public static class StartMenuModel
{
    public static string CategoryKey(string? category) =>
        "c:" + (string.IsNullOrWhiteSpace(category) ? ProgramEntry.DefaultCategory : category.Trim()).ToUpperInvariant();

    public static IReadOnlyList<StartMenuGroup> Build(IReadOnlyList<ProgramEntry> programs, IReadOnlyList<string>? order = null)
    {
        ArgumentNullException.ThrowIfNull(programs);
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < (order?.Count ?? 0); i++)
        {
            position.TryAdd(order![i], i);
        }

        int Pos(string key) => position.TryGetValue(key, out var index) ? index : int.MaxValue;

        return programs
            .Where(p => p.ShowInStartMenu)
            .GroupBy(p => CategoryKey(p.Category))
            .Select(g => new
            {
                Key = g.Key,
                Name = string.IsNullOrWhiteSpace(g.First().Category) ? ProgramEntry.DefaultCategory : g.First().Category.Trim(),
                Programs = g.OrderBy(p => Pos(DesktopLayoutHelper.ProgramKey(p))).ThenBy(p => p.Name, comparer).ToList(),
            })
            .OrderBy(g => Pos(g.Key))
            .ThenBy(g => g.Name, comparer)
            .Select(g => new StartMenuGroup(g.Name, g.Programs))
            .ToList();
    }

    /// <summary>Lista completa e explícita (todas as categorias e programas atuais, na ordem final) para gravar.</summary>
    public static IReadOnlyList<string> Flatten(IReadOnlyList<StartMenuGroup> groups)
    {
        var list = new List<string>();
        foreach (var group in groups)
        {
            list.Add(group.Key);
            list.AddRange(group.Programs.Select(DesktopLayoutHelper.ProgramKey));
        }

        return list;
    }
}
