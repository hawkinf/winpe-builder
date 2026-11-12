using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinPEBuilder.Core.Programs;

namespace WinPEBuilder.App;

/// <summary>
/// Editor visual: mostra, sem executar nada, como o desktop e o menu Iniciar do WinPE vão ficar. Dá para arrastar os ícones,
/// adicionar e remover programas e, no fim, gravar ou descartar. A lógica fica em <see cref="DesignerModel"/> (testada);
/// aqui só há desenho e eventos de mouse.
/// </summary>
public partial class DesignerWindow : Window
{
    private const int Cell = DesktopLayoutHelper.CellSize;
    private const string ProgramFormat = "wpb-program";
    private const string FromFolderFormat = "wpb-from-folder";

    private sealed record ListRow(string Id, string Name, string Detail);

    private readonly DesignerModel _model;
    private readonly Dictionary<string, Border> _desktopIcons = new();
    private readonly Dictionary<string, Border> _folderIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Border _highlight;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    private string? _selectedKey;   // ícone selecionado na área de trabalho
    private string? _selectedId;    // programa selecionado (lista, ícone ou janela da pasta)
    private string? _openFolderKey;
    private bool _suppressListEvents;
    private bool _finishing;

    // arrastar um ícone da área de trabalho
    private string? _dragKey;
    private Border? _dragIcon;
    private Point _dragStart;
    private Point _dragOffset;
    private bool _dragging;

    // arrastar da lista / da janela da pasta
    private string? _pendingDragId;
    private bool _pendingDragFromFolder;
    private Point _pendingDragStart;

    /// <summary>true se o usuário escolheu Gravar (o resultado está em Programs/Layout).</summary>
    public bool Saved { get; private set; }

    public IReadOnlyList<ProgramEntry> Programs => _model.Programs;

    public IReadOnlyDictionary<string, DesktopSlot> Layout => _model.Layout;

    public DesignerWindow(IEnumerable<ProgramEntry> programs, IReadOnlyDictionary<string, DesktopSlot>? layout)
    {
        InitializeComponent();
        _model = new DesignerModel(programs, layout);
        _highlight = new Border
        {
            Width = Cell,
            Height = Cell,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Panel.SetZIndex(_highlight, 50);

        Clock.Text = DateTime.Now.ToString("HH:mm");
        _clock.Tick += (_, _) => Clock.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        Closed += (_, _) => _clock.Stop();

        Render();
    }

    // ==== desenho ===============================================================================================

    private void Render()
    {
        DesktopCanvas.Children.Clear();
        _desktopIcons.Clear();
        foreach (var item in _model.Items)
        {
            var slot = _model.Layout[item.Key];
            var icon = BuildDesktopIcon(item);
            Canvas.SetLeft(icon, (slot.Col * Cell) + 2);
            Canvas.SetTop(icon, (slot.Row * Cell) + 2);
            DesktopCanvas.Children.Add(icon);
            _desktopIcons[item.Key] = icon;
        }

        DesktopCanvas.Children.Add(_highlight);

        if (_openFolderKey is not null && _model.FindItem(_openFolderKey) is not { IsFolder: true })
        {
            _openFolderKey = null; // a pasta deixou de existir
        }

        if (_selectedKey is not null && _model.FindItem(_selectedKey) is null)
        {
            _selectedKey = null;
        }

        if (_selectedId is not null && _model.Find(_selectedId) is null)
        {
            _selectedId = null;
        }

        RefreshFolderPanel();
        RefreshList();
        ApplySelection();
        EmptyHint.Visibility = _model.Programs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DirtyText.Text = _model.Dirty ? "● Não gravado" : string.Empty;
    }

    private static Border BuildIconVisual(ImageSource image, string label, string tooltip)
    {
        var panel = new StackPanel { Width = Cell - 4 };
        panel.Children.Add(new Image { Source = image, Width = 32, Height = 32, Margin = new Thickness(0, 6, 0, 3) });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 32,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 2, 0),
        });
        return new Border
        {
            Width = Cell - 4,
            Height = Cell - 4,
            Child = panel,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            ToolTip = tooltip,
        };
    }

    private static ImageSource IconFor(ProgramEntry program) => ShellIcon.Get(program.SourceExe);

    private Border BuildDesktopIcon(DesktopItem item)
    {
        var icon = item.IsFolder
            ? BuildIconVisual(ShellIcon.Folder, item.Label, $"{item.Members.Count} programa(s) — duplo clique abre")
            : BuildIconVisual(IconFor(item.Program!), item.Label, item.Program!.SourceExe);
        icon.Tag = item.Key;
        icon.MouseLeftButtonDown += Icon_MouseLeftButtonDown;
        icon.MouseMove += Icon_MouseMove;
        icon.MouseLeftButtonUp += Icon_MouseLeftButtonUp;
        icon.ContextMenu = BuildItemMenu(item);
        return icon;
    }

    private void ApplySelection()
    {
        var on = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        var border = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        foreach (var (key, icon) in _desktopIcons)
        {
            var selected = key == _selectedKey;
            icon.Background = selected ? on : Brushes.Transparent;
            icon.BorderBrush = selected ? border : Brushes.Transparent;
        }

        foreach (var (id, icon) in _folderIcons)
        {
            var selected = string.Equals(id, _selectedId, StringComparison.OrdinalIgnoreCase);
            icon.Background = selected ? new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xF7)) : Brushes.Transparent;
            icon.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0x66, 0xA8, 0xE0)) : Brushes.Transparent;
        }

        var canAct = _selectedId is not null || (_selectedKey is not null && _model.FindItem(_selectedKey) is { IsFolder: true });
        EditButton.IsEnabled = canAct;
        RemoveButton.IsEnabled = canAct;
    }

    private void RefreshList()
    {
        var rows = _model.Programs
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new ListRow(p.Id, p.Name, Describe(p)))
            .ToList();
        _suppressListEvents = true;
        ProgramList.ItemsSource = rows;
        ProgramList.SelectedItem = rows.FirstOrDefault(r => string.Equals(r.Id, _selectedId, StringComparison.OrdinalIgnoreCase));
        _suppressListEvents = false;
    }

    private static string Describe(ProgramEntry p)
    {
        var places = new List<string>();
        if (p.ShowOnDesktop)
        {
            places.Add("Desktop");
        }

        if (p.ShowInStartMenu)
        {
            places.Add("Iniciar");
        }

        if (!string.IsNullOrEmpty(p.DesktopFolder))
        {
            places.Add($"Pasta {p.DesktopFolder}");
        }

        return $"{string.Join(" · ", places)}   |   {p.Category}";
    }

    // ==== janela de pasta ========================================================================================

    private void RefreshFolderPanel()
    {
        FolderItems.Children.Clear();
        _folderIcons.Clear();
        if (_openFolderKey is null || _model.FindItem(_openFolderKey) is not { IsFolder: true } folder)
        {
            FolderPanel.Visibility = Visibility.Collapsed;
            return;
        }

        FolderPanel.Visibility = Visibility.Visible;
        FolderTitle.Text = folder.Label;
        foreach (var member in folder.Members)
        {
            var icon = BuildIconVisual(IconFor(member), member.Name, member.SourceExe);
            // dentro da janela branca o texto precisa ser escuro
            ((TextBlock)((StackPanel)icon.Child).Children[1]).Foreground = Brushes.Black;
            icon.Tag = member.Id;
            icon.Margin = new Thickness(2);
            icon.MouseLeftButtonDown += FolderIcon_MouseLeftButtonDown;
            icon.MouseMove += FolderIcon_MouseMove;
            icon.ContextMenu = BuildMemberMenu(member);
            _folderIcons[member.Id] = icon;
            FolderItems.Children.Add(icon);
        }
    }

    private void CloseFolder_Click(object sender, RoutedEventArgs e)
    {
        _openFolderKey = null;
        RefreshFolderPanel();
    }

    private void OpenFolder(string key)
    {
        _openFolderKey = key;
        RefreshFolderPanel();
        ApplySelection();
    }

    private void FolderIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var id = (string)((Border)sender).Tag;
        _selectedId = id;
        _selectedKey = null;
        ApplySelection();
        RefreshListSelectionOnly();
        if (e.ClickCount == 2)
        {
            EditProgram(id);
        }
        else
        {
            _pendingDragId = id;
            _pendingDragFromFolder = true;
            _pendingDragStart = e.GetPosition(this);
        }

        e.Handled = true;
    }

    private void FolderIcon_MouseMove(object sender, MouseEventArgs e) => TryStartExternalDrag(e, (DependencyObject)sender);

    // ==== seleção ================================================================================================

    private void RefreshListSelectionOnly()
    {
        _suppressListEvents = true;
        ProgramList.SelectedItem = ProgramList.Items.OfType<ListRow>()
            .FirstOrDefault(r => string.Equals(r.Id, _selectedId, StringComparison.OrdinalIgnoreCase));
        _suppressListEvents = false;
    }

    private void SelectKey(string key)
    {
        _selectedKey = key;
        _selectedId = _model.FindItem(key)?.Program?.Id;
        ApplySelection();
        RefreshListSelectionOnly();
    }

    private void ProgramList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressListEvents)
        {
            return;
        }

        _selectedId = (ProgramList.SelectedItem as ListRow)?.Id;
        _selectedKey = _selectedId is not null && _model.Find(_selectedId) is { ShowOnDesktop: true } p
            ? DesktopLayoutHelper.ProgramKey(p)
            : null;
        ApplySelection();
    }

    private void ProgramList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedId is not null)
        {
            EditProgram(_selectedId);
        }
    }

    private void Desktop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // clicou no fundo (os ícones marcam o evento como tratado)
        _selectedKey = null;
        _selectedId = null;
        ApplySelection();
        RefreshListSelectionOnly();
    }

    // ==== arrastar ícones da área de trabalho =======================================================================

    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var icon = (Border)sender;
        var key = (string)icon.Tag;
        SelectKey(key);
        e.Handled = true;

        if (e.ClickCount == 2)
        {
            ActivateItem(key);
            return;
        }

        _dragKey = key;
        _dragIcon = icon;
        _dragStart = e.GetPosition(DesktopCanvas);
        _dragOffset = e.GetPosition(icon);
        _dragging = false;
        icon.CaptureMouse();
    }

    private void Icon_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragIcon is null || !ReferenceEquals(sender, _dragIcon) || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(DesktopCanvas);
        if (!_dragging && (Math.Abs(pos.X - _dragStart.X) > 4 || Math.Abs(pos.Y - _dragStart.Y) > 4))
        {
            _dragging = true;
            Panel.SetZIndex(_dragIcon, 100);
            _dragIcon.Opacity = 0.8;
        }

        if (_dragging)
        {
            Canvas.SetLeft(_dragIcon, pos.X - _dragOffset.X);
            Canvas.SetTop(_dragIcon, pos.Y - _dragOffset.Y);
            ShowHighlight(DesktopLayoutHelper.SlotAt(pos.X, pos.Y));
        }
    }

    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragIcon is null || !ReferenceEquals(sender, _dragIcon))
        {
            return;
        }

        var key = _dragKey!;
        var wasDragging = _dragging;
        var pos = e.GetPosition(DesktopCanvas);
        _dragIcon.ReleaseMouseCapture();
        _dragIcon = null;
        _dragKey = null;
        _dragging = false;
        HideHighlight();

        if (!wasDragging)
        {
            return;
        }

        var slot = DesktopLayoutHelper.SlotAt(pos.X, pos.Y);
        var occupant = _model.Layout.FirstOrDefault(kv => kv.Value == slot && kv.Key != key).Key;
        var dragged = _model.FindItem(key);
        if (occupant is not null && dragged is { IsFolder: false } && _model.FindItem(occupant) is { IsFolder: true })
        {
            _model.MoveIntoFolder(dragged.Program!.Id, occupant); // soltou sobre uma pasta: entra nela
        }
        else
        {
            _model.MoveTo(key, slot); // célula livre: move; ocupada: troca de lugar
        }

        Render();
    }

    private void ShowHighlight(DesktopSlot slot)
    {
        Canvas.SetLeft(_highlight, slot.Col * Cell);
        Canvas.SetTop(_highlight, slot.Row * Cell);
        _highlight.Visibility = Visibility.Visible;
    }

    private void HideHighlight() => _highlight.Visibility = Visibility.Collapsed;

    // ==== arrastar da lista / da pasta para a área de trabalho ======================================================

    private void ProgramList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(ProgramList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        _pendingDragId = (container?.DataContext as ListRow)?.Id;
        _pendingDragFromFolder = false;
        _pendingDragStart = e.GetPosition(this);
    }

    private void ProgramList_PreviewMouseMove(object sender, MouseEventArgs e) => TryStartExternalDrag(e, ProgramList);

    private void TryStartExternalDrag(MouseEventArgs e, DependencyObject source)
    {
        if (_pendingDragId is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _pendingDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _pendingDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(ProgramFormat, _pendingDragId);
        if (_pendingDragFromFolder)
        {
            data.SetData(FromFolderFormat, "1");
        }

        _pendingDragId = null;
        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void Desktop_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProgramFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            var pos = e.GetPosition(DesktopCanvas);
            ShowHighlight(DesktopLayoutHelper.SlotAt(pos.X, pos.Y));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Desktop_DragLeave(object sender, DragEventArgs e) => HideHighlight();

    private void Desktop_Drop(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(DesktopCanvas);
        var slot = DesktopLayoutHelper.SlotAt(pos.X, pos.Y);
        HideHighlight();

        if (e.Data.GetData(ProgramFormat) is string id)
        {
            DropProgram(id, slot, e.Data.GetDataPresent(FromFolderFormat));
            Render();
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            // adia: não bloquear o Explorer enquanto os diálogos estão abertos
            Dispatcher.BeginInvoke(() => AddDropped(paths, slot));
        }

        e.Handled = true;
    }

    private void DropProgram(string id, DesktopSlot slot, bool fromFolder)
    {
        var program = _model.Find(id);
        if (program is null)
        {
            return;
        }

        var occupant = _model.Layout.FirstOrDefault(kv => kv.Value == slot).Key;
        if (occupant is not null && _model.FindItem(occupant) is { IsFolder: true } folder)
        {
            if (!(fromFolder && string.Equals(program.DesktopFolder, folder.Label, StringComparison.CurrentCultureIgnoreCase)))
            {
                _model.MoveIntoFolder(id, occupant); // solto sobre uma pasta: entra nela
            }

            return;
        }

        if (fromFolder)
        {
            _model.TakeOutOfFolder(id, slot);
        }
        else if (program.ShowOnDesktop)
        {
            _model.MoveTo(DesktopLayoutHelper.ProgramKey(program), slot);
        }
        else
        {
            _model.ShowOnDesktopAt(id, slot);
        }
    }

    // ==== adicionar =============================================================================================

    private static readonly string[] ProgramExtensions = [".exe", ".cmd", ".bat", ".com"];

    private void AddDropped(string[] paths, DesktopSlot slot)
    {
        DesktopSlot? at = slot;
        foreach (var path in paths)
        {
            IReadOnlyList<ProgramEntry> results = [];
            if (Directory.Exists(path))
            {
                var dialog = new FolderImportWindow(_model.Ids, path) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    results = dialog.Results;
                }
            }
            else if (File.Exists(path) && ProgramExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                var dialog = new ProgramWindow(null, _model.Categories, _model.Ids, path) { Owner = this };
                if (dialog.ShowDialog() == true && dialog.Result is not null)
                {
                    results = [dialog.Result];
                }
            }

            if (results.Count > 0)
            {
                _model.Add(results, at);
                at = null; // só o primeiro cai na célula do mouse; os outros vão para as livres
                Render();
            }
        }
    }

    private void AddProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProgramWindow(null, _model.Categories, _model.Ids) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _model.Add([dialog.Result]);
            SelectAfterAdd(dialog.Result);
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderImportWindow(_model.Ids) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Results.Count > 0)
        {
            _model.Add(dialog.Results);
            SelectAfterAdd(dialog.Results[0]);
        }
    }

    private void SelectAfterAdd(ProgramEntry program)
    {
        _selectedId = program.Id;
        _selectedKey = program.ShowOnDesktop ? DesktopLayoutHelper.ProgramKey(program) : null;
        Render();
    }

    // ==== editar / remover =======================================================================================

    private void ActivateItem(string key)
    {
        if (_model.FindItem(key) is not { } item)
        {
            return;
        }

        if (item.IsFolder)
        {
            OpenFolder(key);
        }
        else
        {
            EditProgram(item.Program!.Id);
        }
    }

    private void EditProgram(string id)
    {
        if (_model.Find(id) is not { } program)
        {
            return;
        }

        var dialog = new ProgramWindow(program, _model.Categories, _model.Ids) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _model.Replace(dialog.Result);
            _selectedId = dialog.Result.Id;
            Render();
        }
    }

    private void RenameFolder(string key)
    {
        if (_model.FindItem(key) is not { IsFolder: true } folder)
        {
            return;
        }

        var name = InputWindow.Ask(this, "Renomear pasta", "Novo nome da pasta na área de trabalho:", folder.Label);
        if (name is not null && _model.RenameFolder(key, name))
        {
            _selectedKey = DesktopLayoutHelper.FolderKey(name);
            if (_openFolderKey == key)
            {
                _openFolderKey = _selectedKey;
            }

            Render();
        }
    }

    private void EditSelected()
    {
        if (_selectedId is not null)
        {
            EditProgram(_selectedId);
        }
        else if (_selectedKey is not null && _model.FindItem(_selectedKey) is { IsFolder: true })
        {
            RenameFolder(_selectedKey);
        }
    }

    private void RemoveSelected()
    {
        if (_selectedId is not null)
        {
            _model.Remove(_selectedId);
            _selectedId = null;
            _selectedKey = null;
            Render();
        }
        else if (_selectedKey is not null && _model.FindItem(_selectedKey) is { IsFolder: true } folder)
        {
            RemoveFolder(folder);
        }
    }

    private void RemoveFolder(DesktopItem folder)
    {
        var answer = MessageBox.Show(this,
            $"Remover a pasta \"{folder.Label}\"?\n\nSim = remove a pasta E os {folder.Members.Count} programa(s) dela\nNão = só desfaz a pasta (os programas ficam soltos na área de trabalho)",
            "Remover pasta", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            _model.RemoveItem(folder.Key);
        }
        else if (answer == MessageBoxResult.No)
        {
            _model.DissolveFolder(folder.Key);
        }
        else
        {
            return;
        }

        _selectedKey = null;
        _selectedId = null;
        Render();
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    private void AutoArrange_Click(object sender, RoutedEventArgs e)
    {
        _model.AutoArrange();
        Render();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
                RemoveSelected();
                e.Handled = true;
                break;
            case Key.Enter:
                EditSelected();
                e.Handled = true;
                break;
            case Key.F2:
                if (_selectedKey is not null)
                {
                    RenameFolder(_selectedKey);
                }

                e.Handled = true;
                break;
        }
    }

    // ==== menus de contexto ======================================================================================

    private static MenuItem Item(string header, RoutedEventHandler click, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += click;
        return item;
    }

    private ContextMenu BuildItemMenu(DesktopItem item)
    {
        var menu = new ContextMenu();
        if (item.IsFolder)
        {
            menu.Items.Add(Item("Abrir", (_, _) => OpenFolder(item.Key)));
            menu.Items.Add(Item("Renomear…", (_, _) => RenameFolder(item.Key)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Desfazer a pasta (programas ficam soltos)", (_, _) =>
            {
                _model.DissolveFolder(item.Key);
                Render();
            }));
            menu.Items.Add(Item("Remover a pasta e os programas…", (_, _) => RemoveFolder(item)));
            return menu;
        }

        var program = item.Program!;
        menu.Items.Add(Item("Editar…", (_, _) => EditProgram(program.Id)));
        menu.Items.Add(Item("Colocar em uma pasta nova…", (_, _) => PutInNewFolder(program.Id)));
        if (!string.IsNullOrEmpty(program.DesktopFolder))
        {
            menu.Items.Add(Item("Tirar da pasta", (_, _) =>
            {
                _model.TakeOutOfFolder(program.Id);
                Render();
            }));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remover do WinPE", (_, _) =>
        {
            _model.Remove(program.Id);
            Render();
        }));
        return menu;
    }

    private ContextMenu BuildMemberMenu(ProgramEntry member)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Editar…", (_, _) => EditProgram(member.Id)));
        menu.Items.Add(Item("Tirar da pasta", (_, _) =>
        {
            _model.TakeOutOfFolder(member.Id);
            Render();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remover do WinPE", (_, _) =>
        {
            _model.Remove(member.Id);
            Render();
        }));
        return menu;
    }

    private void PutInNewFolder(string programId)
    {
        var name = InputWindow.Ask(this, "Nova pasta", "Nome da nova pasta na área de trabalho:");
        if (name is null || _model.Find(programId) is not { } program)
        {
            return;
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 40 || trimmed.Any(char.IsControl))
        {
            MessageBox.Show(this, "O nome da pasta deve ter até 40 caracteres.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _model.Replace(program with { DesktopFolder = trimmed, ShowOnDesktop = false });
        _selectedKey = DesktopLayoutHelper.FolderKey(trimmed);
        _selectedId = null;
        Render();
    }

    // ==== menu Iniciar (preview) =================================================================================

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = StartButton, Placement = PlacementMode.Top };
        var groups = _model.StartMenu;
        if (groups.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(nenhum programa no menu)", IsEnabled = false });
        }
        else if (groups.Count == 1)
        {
            foreach (var program in groups[0].Programs)
            {
                menu.Items.Add(ProgramMenuItem(program));
            }
        }
        else
        {
            foreach (var group in groups)
            {
                var folder = new MenuItem { Header = group.Category };
                foreach (var program in group.Programs)
                {
                    folder.Items.Add(ProgramMenuItem(program));
                }

                menu.Items.Add(folder);
            }
        }

        var system32 = Environment.SystemDirectory;
        menu.Items.Add(new Separator());
        menu.Items.Add(SystemMenuItem("Prompt de comando", Path.Combine(system32, "cmd.exe")));
        menu.Items.Add(SystemMenuItem("PowerShell", Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe")));
        menu.Items.Add(new Separator());
        menu.Items.Add(SystemMenuItem("Reiniciar", null));
        menu.Items.Add(SystemMenuItem("Desligar", null));
        menu.IsOpen = true;
    }

    private MenuItem ProgramMenuItem(ProgramEntry program)
    {
        var item = new MenuItem
        {
            Header = program.Name,
            Icon = new Image { Source = IconFor(program), Width = 16, Height = 16 },
            ToolTip = "Clique para editar (nada é executado no preview)",
        };
        item.Click += (_, _) => EditProgram(program.Id);
        return item;
    }

    private MenuItem SystemMenuItem(string header, string? iconFile)
    {
        var item = new MenuItem { Header = header, ToolTip = "Item fixo do WinPE (no preview não faz nada)" };
        if (iconFile is not null)
        {
            item.Icon = new Image { Source = ShellIcon.Get(iconFile), Width = 16, Height = 16 };
        }

        item.Click += (_, _) => MessageBox.Show(this, "Item fixo do menu Iniciar do WinPE. No preview nada é executado.", Title,
            MessageBoxButton.OK, MessageBoxImage.Information);
        return item;
    }

    // ==== gravar / descartar =====================================================================================

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Saved = true;
        _finishing = true;
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (_model.Dirty && MessageBox.Show(this, "Descartar todas as alterações feitas nesta tela?", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Saved = false;
        _finishing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_finishing || !_model.Dirty)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            "Há alterações não gravadas.\n\nSim = gravar\nNão = descartar\nCancelar = continuar editando",
            Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        Saved = answer == MessageBoxResult.Yes;
        _finishing = true;
    }
}
