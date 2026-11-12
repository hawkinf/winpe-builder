namespace WinPEBuilder.Core.Programs;

/// <summary>
/// Estado editável do editor visual: a lista de programas e as posições dos ícones na área de trabalho.
/// Trabalha sobre uma CÓPIA; quem chama decide gravar (usar <see cref="Programs"/>/<see cref="Layout"/>) ou descartar.
/// Toda a lógica das operações (mover, trocar, colocar em pasta, remover…) fica aqui para ser testável sem tela.
/// </summary>
public sealed class DesignerModel
{
    private readonly List<ProgramEntry> _programs;
    private Dictionary<string, DesktopSlot> _layout;
    private List<string> _menuOrder;

    public DesignerModel(IEnumerable<ProgramEntry> programs, IReadOnlyDictionary<string, DesktopSlot>? layout, IEnumerable<string>? menuOrder = null)
    {
        ArgumentNullException.ThrowIfNull(programs);
        _programs = [.. programs];
        _menuOrder = [.. menuOrder ?? []];
        _layout = DesktopLayoutHelper.Normalize(DesktopLayoutHelper.Items(_programs), layout);
    }

    public IReadOnlyList<ProgramEntry> Programs => _programs;

    /// <summary>Posições de TODOS os ícones atuais (sempre completo e consistente).</summary>
    public IReadOnlyDictionary<string, DesktopSlot> Layout => _layout;

    /// <summary>true se algo mudou desde a abertura (para perguntar antes de descartar).</summary>
    public bool Dirty { get; private set; }

    public IReadOnlyList<DesktopItem> Items => DesktopLayoutHelper.Items(_programs);

    public IReadOnlyList<StartMenuGroup> StartMenu => StartMenuModel.Build(_programs, _menuOrder);

    /// <summary>Ordem completa e explicita do menu Iniciar (categorias e programas), pronta para gravar.</summary>
    public IReadOnlyList<string> MenuOrder => StartMenuModel.Flatten(StartMenu);

    public ProgramEntry? Find(string id) => _programs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public DesktopItem? FindItem(string key) => Items.FirstOrDefault(i => i.Key == key);

    public IReadOnlyList<string> Ids => [.. _programs.Select(p => p.Id)];

    public IReadOnlyList<string> Categories => [.. _programs.Select(p => p.Category).Distinct(StringComparer.CurrentCultureIgnoreCase)];

    // ---- mover ----

    /// <summary>
    /// Move um ícone para uma célula (limitada à grade). Se já houver outro ícone lá, os dois trocam de lugar.
    /// </summary>
    public bool MoveTo(string key, DesktopSlot target)
    {
        if (!_layout.TryGetValue(key, out var from))
        {
            return false;
        }

        target = new DesktopSlot(Math.Clamp(target.Col, 0, DesktopLayoutHelper.Columns - 1), Math.Clamp(target.Row, 0, DesktopLayoutHelper.Rows - 1));
        if (target == from)
        {
            return false;
        }

        var other = _layout.FirstOrDefault(p => p.Value == target && p.Key != key);
        if (other.Key is not null)
        {
            _layout[other.Key] = from;
        }

        _layout[key] = target;
        Dirty = true;
        return true;
    }

    /// <summary>Arruma todos os ícones de novo, em ordem alfabética (descarta as posições manuais).</summary>
    public void AutoArrange()
    {
        _layout = DesktopLayoutHelper.Normalize(Items, new Dictionary<string, DesktopSlot>());
        Dirty = true;
    }

    // ---- adicionar / editar / remover ----

    /// <summary>
    /// Adiciona programas. Se <paramref name="at"/> for informado, o primeiro que aparecer como ícone solto vai para essa
    /// célula (ou para a primeira livre se estiver ocupada); os demais vão para as livres.
    /// </summary>
    public void Add(IEnumerable<ProgramEntry> entries, DesktopSlot? at = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var added = entries.ToList();
        foreach (var entry in added)
        {
            if (Find(entry.Id) is not null)
            {
                throw new InvalidOperationException($"Já existe um programa com o id {entry.Id}.");
            }
        }

        _programs.AddRange(added);
        var first = added.FirstOrDefault(p => p.ShowOnDesktop);
        Refresh(first is null || at is null ? null : (DesktopLayoutHelper.ProgramKey(first), at));
        Dirty |= added.Count > 0;
    }

    /// <summary>Troca um programa pela versão editada (mesmo id), mantendo a posição quando possível.</summary>
    public void Replace(ProgramEntry updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        var index = _programs.FindIndex(p => string.Equals(p.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException($"Programa inexistente: {updated.Id}.");
        }

        var previous = _programs[index];
        _programs[index] = updated;

        // Se o programa saiu de uma pasta e foi para outra com outro nome, o ícone da pasta antiga some sozinho.
        Refresh(null);
        Dirty |= previous != updated;
    }

    public bool Remove(string id)
    {
        var removed = _programs.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Refresh(null);
            Dirty = true;
        }

        return removed;
    }

    /// <summary>Remove um ícone da área de trabalho: programa solto = remove o programa; pasta = remove a pasta E seus programas.</summary>
    public void RemoveItem(string key)
    {
        var item = FindItem(key) ?? throw new InvalidOperationException("Ícone inexistente.");
        if (item.IsFolder)
        {
            foreach (var member in item.Members)
            {
                Remove(member.Id);
            }
        }
        else
        {
            Remove(item.Program!.Id);
        }
    }

    // ---- pastas da área de trabalho ----

    /// <summary>Coloca um programa dentro de uma pasta (ele deixa de ser ícone solto, mas continua no menu Iniciar se estava).</summary>
    public bool MoveIntoFolder(string programId, string folderKey)
    {
        var program = Find(programId);
        var folder = FindItem(folderKey);
        if (program is null || folder is null || !folder.IsFolder)
        {
            return false;
        }

        Replace(program with { DesktopFolder = folder.Label, ShowOnDesktop = false });
        return true;
    }

    /// <summary>Tira um programa da pasta e o põe como ícone solto na célula informada (ou na primeira livre).</summary>
    public bool TakeOutOfFolder(string programId, DesktopSlot? at = null)
    {
        var program = Find(programId);
        if (program is null || string.IsNullOrEmpty(program.DesktopFolder))
        {
            return false;
        }

        _programs[_programs.FindIndex(p => p.Id == program.Id)] = program with { DesktopFolder = string.Empty, ShowOnDesktop = true };
        Refresh(at is null ? null : (DesktopLayoutHelper.ProgramKey(program), at));
        Dirty = true;
        return true;
    }

    /// <summary>Faz um programa (que estava só no menu Iniciar) aparecer na área de trabalho, numa célula escolhida.</summary>
    public bool ShowOnDesktopAt(string programId, DesktopSlot at)
    {
        var program = Find(programId);
        if (program is null)
        {
            return false;
        }

        _programs[_programs.FindIndex(p => p.Id == program.Id)] = program with { ShowOnDesktop = true };
        Refresh((DesktopLayoutHelper.ProgramKey(program), at));
        Dirty = true;
        return true;
    }

    /// <summary>Renomeia uma pasta da área de trabalho (todos os programas dela), mantendo a posição do ícone.</summary>
    public bool RenameFolder(string folderKey, string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        var folder = FindItem(folderKey);
        if (folder is null || !folder.IsFolder || newName.Length == 0 || newName.Length > 40 || newName.Any(char.IsControl))
        {
            return false;
        }

        var oldSlot = _layout[folderKey];
        foreach (var member in folder.Members)
        {
            var index = _programs.FindIndex(p => p.Id == member.Id);
            _programs[index] = _programs[index] with { DesktopFolder = newName };
        }

        var newKey = DesktopLayoutHelper.FolderKey(newName);
        Refresh(newKey == folderKey ? null : (newKey, oldSlot));
        Dirty = true;
        return true;
    }

    /// <summary>Dissolve a pasta: os programas dela deixam de pertencer a pasta alguma e viram ícones soltos.</summary>
    public void DissolveFolder(string folderKey)
    {
        var folder = FindItem(folderKey);
        if (folder is null || !folder.IsFolder)
        {
            return;
        }

        foreach (var member in folder.Members)
        {
            var index = _programs.FindIndex(p => p.Id == member.Id);
            _programs[index] = _programs[index] with { DesktopFolder = string.Empty, ShowOnDesktop = true };
        }

        Refresh(null);
        Dirty = true;
    }

    // ---- ordem do menu Iniciar ----

    /// <summary>
    /// Sobe (delta menor que 0) ou desce uma categoria ("c:...") ou um programa ("p:...") dentro dos seus irmaos
    /// (programas so andam dentro da propria categoria). Devolve false se ja esta no limite ou a chave nao existe.
    /// </summary>
    public bool MoveMenuEntry(string key, int delta)
    {
        var groups = StartMenu.ToList();
        if (key.StartsWith("c:", StringComparison.Ordinal))
        {
            var index = groups.FindIndex(g => g.Key == key);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= groups.Count)
            {
                return false;
            }

            var moved = groups[index];
            groups.RemoveAt(index);
            groups.Insert(target, moved);
            SetMenuOrder(groups);
            return true;
        }

        for (var g = 0; g < groups.Count; g++)
        {
            var programs = groups[g].Programs.ToList();
            var index = programs.FindIndex(p => DesktopLayoutHelper.ProgramKey(p) == key);
            if (index < 0)
            {
                continue;
            }

            var target = index + delta;
            if (target < 0 || target >= programs.Count)
            {
                return false;
            }

            var movedProgram = programs[index];
            programs.RemoveAt(index);
            programs.Insert(target, movedProgram);
            groups[g] = groups[g] with { Programs = programs };
            SetMenuOrder(groups);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Arrastar no menu: categoria sobre categoria = vai para antes dela; programa sobre categoria = muda para essa categoria
    /// (no fim); programa sobre programa = vai para antes dele (mudando de categoria, se for outra).
    /// </summary>
    public bool DropMenuEntry(string sourceKey, string targetKey)
    {
        if (sourceKey == targetKey)
        {
            return false;
        }

        var groups = StartMenu.ToList();
        if (sourceKey.StartsWith("c:", StringComparison.Ordinal))
        {
            var from = groups.FindIndex(g => g.Key == sourceKey);
            var group = from < 0 ? null : groups[from];
            var targetCategory = targetKey.StartsWith("c:", StringComparison.Ordinal)
                ? targetKey
                : groups.FirstOrDefault(g => g.Programs.Any(p => DesktopLayoutHelper.ProgramKey(p) == targetKey))?.Key;
            if (group is null || targetCategory is null || targetCategory == sourceKey)
            {
                return false;
            }

            groups.RemoveAt(from);
            groups.Insert(groups.FindIndex(g => g.Key == targetCategory), group);
            SetMenuOrder(groups);
            return true;
        }

        var program = _programs.FirstOrDefault(p => DesktopLayoutHelper.ProgramKey(p) == sourceKey && p.ShowInStartMenu);
        if (program is null)
        {
            return false;
        }

        StartMenuGroup? targetGroup;
        ProgramEntry? before = null;
        if (targetKey.StartsWith("c:", StringComparison.Ordinal))
        {
            targetGroup = groups.FirstOrDefault(g => g.Key == targetKey);
        }
        else
        {
            targetGroup = groups.FirstOrDefault(g => g.Programs.Any(p => DesktopLayoutHelper.ProgramKey(p) == targetKey));
            before = targetGroup?.Programs.First(p => DesktopLayoutHelper.ProgramKey(p) == targetKey);
        }

        if (targetGroup is null)
        {
            return false;
        }

        var moved = program;
        if (targetGroup.Key != StartMenuModel.CategoryKey(program.Category))
        {
            moved = program with { Category = targetGroup.Category };
            _programs[_programs.FindIndex(p => p.Id == program.Id)] = moved;
        }

        // reconstroi os grupos com o programa na posicao pedida (a origem sai do grupo antigo)
        var rebuilt = StartMenuModel.Build(_programs, _menuOrder).ToList();
        for (var i = 0; i < rebuilt.Count; i++)
        {
            rebuilt[i] = rebuilt[i] with { Programs = rebuilt[i].Programs.Where(p => p.Id != moved.Id).ToList() };
        }

        var dest = rebuilt.FindIndex(g => g.Key == targetGroup.Key);
        if (dest < 0)
        {
            rebuilt.Add(new StartMenuGroup(targetGroup.Category, [moved]));
        }
        else
        {
            var list = rebuilt[dest].Programs.ToList();
            var at = before is null ? list.Count : Math.Max(0, list.FindIndex(p => p.Id == before.Id));
            list.Insert(at, moved);
            rebuilt[dest] = rebuilt[dest] with { Programs = list };
        }

        SetMenuOrder(rebuilt.Where(g => g.Programs.Count > 0).ToList());
        return true;
    }

    private void SetMenuOrder(IReadOnlyList<StartMenuGroup> groups)
    {
        _menuOrder = [.. StartMenuModel.Flatten(groups)];
        Dirty = true;
    }

    // ---- interno ----

    /// <summary>Recalcula o layout: poda o que sumiu e posiciona o que é novo; <paramref name="place"/> força uma posição preferida.</summary>
    private void Refresh((string Key, DesktopSlot Slot)? place)
    {
        var items = DesktopLayoutHelper.Items(_programs);
        var wanted = new Dictionary<string, DesktopSlot>(_layout);
        if (place is { } p && DesktopLayoutHelper.InBounds(p.Slot) && items.Any(i => i.Key == p.Key))
        {
            // Quem já estava na célula pedida (e não é o item novo) sai da frente: perde a posição e é recolocado em seguida.
            foreach (var clash in wanted.Where(kv => kv.Value == p.Slot && kv.Key != p.Key).Select(kv => kv.Key).ToList())
            {
                wanted.Remove(clash);
            }

            wanted[p.Key] = p.Slot;
        }

        _layout = DesktopLayoutHelper.Normalize(items, wanted);
    }
}
