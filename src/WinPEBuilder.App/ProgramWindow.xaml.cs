using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinPEBuilder.Core.Programs;

namespace WinPEBuilder.App;

/// <summary>Diálogo para adicionar/editar um programa que aparecerá no desktop/menu Iniciar do WinPE.</summary>
public partial class ProgramWindow : Window
{
    private readonly ProgramEntry? _existing;
    private readonly IReadOnlyList<string> _otherIds;
    private bool _nameEditedByUser;
    private int _sizeVersion;

    /// <summary>Preenchido quando o usuário confirma com OK.</summary>
    public ProgramEntry? Result { get; private set; }

    public ProgramWindow(ProgramEntry? existing, IEnumerable<string> categories, IEnumerable<string> allIds)
    {
        InitializeComponent();
        _existing = existing;
        _otherIds = [.. allIds.Where(id => !string.Equals(id, existing?.Id, StringComparison.OrdinalIgnoreCase))];

        foreach (var category in categories.Append(ProgramEntry.DefaultCategory).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            CategoryBox.Items.Add(category);
        }

        if (existing is null)
        {
            CategoryBox.Text = ProgramEntry.DefaultCategory;
        }
        else
        {
            Title = "Editar programa";
            ExeBox.Text = existing.SourceExe;
            NameBox.Text = existing.Name;
            _nameEditedByUser = true;
            CategoryBox.Text = existing.Category;
            ArgumentsBox.Text = existing.Arguments;
            DesktopFolderBox.Text = existing.DesktopFolder;
            DesktopCheck.IsChecked = existing.ShowOnDesktop;
            StartMenuCheck.IsChecked = existing.ShowInStartMenu;
            CopyFolderCheck.IsChecked = existing.CopyFolder;
        }

        NameBox.TextChanged += (_, _) =>
        {
            if (NameBox.IsKeyboardFocusWithin)
            {
                _nameEditedByUser = true;
            }
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha o programa",
            Filter = "Programas (*.exe;*.cmd;*.bat;*.com)|*.exe;*.cmd;*.bat;*.com|Todos os arquivos (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            ExeBox.Text = dialog.FileName;
        }
    }

    private void ExeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var path = ExeBox.Text.Trim();
        if (!_nameEditedByUser && File.Exists(path))
        {
            NameBox.Text = SuggestName(path);
        }

        UpdateSize();
    }

    private void CopyFolder_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateSize();
        }
    }

    /// <summary>Nome do produto gravado no .exe (ex.: "HWiNFO"); se não houver, o nome do arquivo.</summary>
    internal static string SuggestName(string path)
    {
        try
        {
            var product = FileVersionInfo.GetVersionInfo(path).ProductName?.Trim();
            if (!string.IsNullOrWhiteSpace(product) && product.Length <= 60)
            {
                return product;
            }
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or UnauthorizedAccessException)
        {
            // sem versão: usa o nome do arquivo
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>Calcula o tamanho que será copiado, em segundo plano (a pasta pode ser grande).</summary>
    private async void UpdateSize()
    {
        var version = ++_sizeVersion;
        var path = ExeBox.Text.Trim();
        var copyFolder = CopyFolderCheck.IsChecked == true;
        var root = File.Exists(path) ? SafeRoot(path) : string.Empty;
        if (!File.Exists(path))
        {
            SizeText.Text = string.Empty;
            return;
        }

        SizeText.Text = "Calculando tamanho…";
        string text;
        try
        {
            text = await Task.Run(() => DescribeSize(path, copyFolder, root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            text = "Não foi possível calcular o tamanho.";
        }

        if (version == _sizeVersion)
        {
            SizeText.Text = text;
        }
    }

    private static string DescribeSize(string path, bool copyFolder, string root)
    {
        var entry = new ProgramEntry { Name = "x", SourceExe = path, CopyFolder = copyFolder, SourceRoot = root };
        if (copyFolder && ProgramEntry.IsProtectedFolder(entry.SourceFolder))
        {
            return "⚠ Esta pasta é do sistema/perfil: copiar tudo traria arquivos demais. Desmarque \"copiar a pasta inteira\" ou mova o programa para uma pasta própria.";
        }

        var mb = ProgramStaging.TotalBytes([entry]) / (1024.0 * 1024);
        var text = $"Será copiado: {mb:0.#} MB.";
        return mb > 500 ? text + " ⚠ Grande: a ISO vai crescer bastante." : text;
    }

    private string SafeRoot(string exePath)
    {
        try
        {
            return KeepRootFor(exePath);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>Mantém a raiz da importação de pasta enquanto o executável continuar dentro dela.</summary>
    private string KeepRoot() => KeepRootFor(ExeBox.Text.Trim());

    private string KeepRootFor(string exePath)
    {
        var root = _existing?.SourceRoot ?? string.Empty;
        if (root.Length == 0 || CopyFolderCheck.IsChecked != true)
        {
            return string.Empty;
        }

        var exe = Path.GetFullPath(exePath);
        var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return exe.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ? root : string.Empty;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var entry = new ProgramEntry
        {
            Id = _existing?.Id ?? ProgramEntry.MakeId(name, _otherIds),
            Name = name,
            Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? ProgramEntry.DefaultCategory : CategoryBox.Text.Trim(),
            SourceExe = ExeBox.Text.Trim(),
            Arguments = ArgumentsBox.Text.Trim(),
            CopyFolder = CopyFolderCheck.IsChecked == true,
            SourceRoot = KeepRoot(),
            DesktopFolder = DesktopFolderBox.Text.Trim(),
            ShowOnDesktop = DesktopCheck.IsChecked == true,
            ShowInStartMenu = StartMenuCheck.IsChecked == true,
        };

        var errors = ProgramEntry.Validate([entry]).ToList();
        if (!entry.ShowOnDesktop && !entry.ShowInStartMenu && entry.DesktopFolder.Length == 0)
        {
            errors.Add("Marque pelo menos um lugar: área de trabalho, menu Iniciar ou uma pasta da área de trabalho.");
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "WinPE Builder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = entry;
        DialogResult = true;
    }
}
