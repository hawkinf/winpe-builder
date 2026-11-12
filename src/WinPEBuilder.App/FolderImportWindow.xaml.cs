using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using WinPEBuilder.Core.Programs;

namespace WinPEBuilder.App;

/// <summary>Uma linha da grade: um executável encontrado dentro da pasta escolhida.</summary>
public sealed class ExeRow(string fullPath, string relativePath, string name) : INotifyPropertyChanged
{
    private bool _include = true;
    private bool _desktop;
    private bool _startMenu = true;
    private string _name = name;

    public string FullPath { get; } = fullPath;

    public string RelativePath { get; } = relativePath;

    public string Name { get => _name; set => Set(ref _name, value); }

    public bool Include { get => _include; set => Set(ref _include, value); }

    public bool Desktop { get => _desktop; set => Set(ref _desktop, value); }

    public bool StartMenu { get => _startMenu; set => Set(ref _startMenu, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>
/// Escolhe uma pasta, lista os .exe dentro dela e deixa definir, programa a programa, se vai para o menu Iniciar
/// e/ou área de trabalho — ou colocar a pasta inteira na área de trabalho.
/// </summary>
public partial class FolderImportWindow : Window
{
    private readonly ObservableCollection<ExeRow> _rows = [];
    private readonly IReadOnlyList<string> _existingIds;
    private string _root = string.Empty;
    private int _scanVersion;

    /// <summary>Programas a adicionar (preenchido com OK).</summary>
    public IReadOnlyList<ProgramEntry> Results { get; private set; } = [];

    public FolderImportWindow(IEnumerable<string> existingIds)
    {
        InitializeComponent();
        _existingIds = [.. existingIds];
        Grid.ItemsSource = _rows;
        InfoText.Text = "Escolha uma pasta (ex.: a pasta de um programa portátil ou de um pacote de ferramentas).";
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pasta com um ou mais programas (.exe)" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await LoadFolderAsync(dialog.FolderName);
    }

    private async Task LoadFolderAsync(string folder)
    {
        var version = ++_scanVersion;
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        InfoText.Text = "Procurando programas…";
        _rows.Clear();

        if (ProgramEntry.IsProtectedFolder(root))
        {
            FolderBox.Text = root;
            InfoText.Text = "⚠ Esta pasta é do sistema/perfil (ou contém uma): copiar tudo traria arquivos demais. Escolha uma pasta própria do programa.";
            _root = string.Empty;
            return;
        }

        ScanResult scan;
        long bytes;
        try
        {
            (scan, bytes) = await Task.Run(() =>
            {
                var result = ProgramScanner.FindExecutables(root);
                var size = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                return (result, size);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            InfoText.Text = "Não foi possível ler essa pasta: " + ex.Message;
            return;
        }

        if (version != _scanVersion)
        {
            return; // o usuário escolheu outra pasta enquanto esta carregava
        }

        _root = root;
        FolderBox.Text = root;
        var folderName = Path.GetFileName(root);
        CategoryBox.Text = string.IsNullOrWhiteSpace(CategoryBox.Text) ? folderName : CategoryBox.Text;
        if (string.IsNullOrWhiteSpace(FolderNameBox.Text))
        {
            FolderNameBox.Text = folderName;
        }

        foreach (var file in scan.Files)
        {
            _rows.Add(new ExeRow(file, Path.GetRelativePath(root, file), ProgramScanner.SuggestName(file)));
        }

        var mb = bytes / (1024.0 * 1024);
        InfoText.Text = scan.Files.Count == 0
            ? "Nenhum .exe encontrado nessa pasta (procurei até 3 níveis de subpastas)."
            : $"{scan.Files.Count} programa(s) encontrado(s). A pasta inteira ({mb:0.#} MB) é copiada uma única vez."
              + (scan.Truncated ? " ⚠ Havia mais programas; só os primeiros foram listados." : string.Empty)
              + (mb > 500 ? " ⚠ Grande: a ISO vai crescer bastante." : string.Empty);
    }

    private void ShowFolder_Changed(object sender, RoutedEventArgs e) =>
        FolderNameBox.IsEnabled = ShowFolderCheck.IsChecked == true;

    private void IncludeAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Include = true;
        }
    }

    private void IncludeNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Include = false;
        }
    }

    private void AllStartMenu_Click(object sender, RoutedEventArgs e)
    {
        var target = _rows.Any(r => r.Include && !r.StartMenu);
        foreach (var row in _rows.Where(r => r.Include))
        {
            row.StartMenu = target;
        }
    }

    private void AllDesktop_Click(object sender, RoutedEventArgs e)
    {
        var target = _rows.Any(r => r.Include && !r.Desktop);
        foreach (var row in _rows.Where(r => r.Include))
        {
            row.Desktop = target;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        if (string.IsNullOrEmpty(_root))
        {
            Warn("Escolha primeiro uma pasta válida.");
            return;
        }

        var chosen = _rows.Where(r => r.Include).ToList();
        if (chosen.Count == 0)
        {
            Warn("Marque pelo menos um programa.");
            return;
        }

        var showFolder = ShowFolderCheck.IsChecked == true;
        var folderName = FolderNameBox.Text.Trim();
        if (showFolder && folderName.Length == 0)
        {
            Warn("Informe o nome da pasta que aparecerá na área de trabalho.");
            return;
        }

        var category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? ProgramEntry.DefaultCategory : CategoryBox.Text.Trim();
        var ids = _existingIds.ToList();
        var results = new List<ProgramEntry>();
        var problems = new List<string>();
        foreach (var row in chosen)
        {
            var name = row.Name.Trim();
            if (!row.Desktop && !row.StartMenu && !showFolder)
            {
                problems.Add($"\"{(name.Length == 0 ? row.RelativePath : name)}\": escolha menu Iniciar, área de trabalho ou a pasta da área de trabalho.");
            }

            var id = ProgramEntry.MakeId(name, ids);
            ids.Add(id);
            results.Add(new ProgramEntry
            {
                Id = id,
                Name = name,
                Category = category,
                SourceExe = row.FullPath,
                SourceRoot = _root,
                CopyFolder = true,
                ShowOnDesktop = row.Desktop,
                ShowInStartMenu = row.StartMenu,
                DesktopFolder = showFolder ? folderName : string.Empty,
            });
        }

        // Ids já são únicos (MakeId); a validação cobre nome, categoria, tamanhos de texto e pasta protegida.
        problems.AddRange(ProgramEntry.Validate(results));
        if (problems.Count > 0)
        {
            Warn(string.Join(Environment.NewLine, problems.Distinct()));
            return;
        }

        Results = results;
        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "WinPE Builder", MessageBoxButton.OK, MessageBoxImage.Warning);
}
