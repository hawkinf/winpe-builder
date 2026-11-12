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

    public DesignerModel(IEnumerable<ProgramEntry> programs, IReadOnlyDictionary<string, DesktopSlot>? layout)
    {
        ArgumentNullException.ThrowIfNull(programs);
        _programs = [.. programs];
        _layout = DesktopLayoutHelper.Normalize(DesktopLayoutHelper.Items(_programs), layout);
    }

    public IReadOnlyList<ProgramEntry> Programs => _programs;

    /// <summary>Posições de TODOS os ícones atuais (sempre completo e consistente).</summary>
    public IReadOnlyDictionary<string, DesktopSlot> Layout => _layout;

    /// <summary>true se algo mudou desde a abertura (para perguntar antes de descartar).</summary>
    public bool Dirty { get; private set; }

    public IReadOnlyList<DesktopItem> Items => DesktopLayoutHelper.Items(_programs);

    public IReadOnlyList<StartMenuGroup> StartMenu => StartMenuModel.Build(_programs);

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
