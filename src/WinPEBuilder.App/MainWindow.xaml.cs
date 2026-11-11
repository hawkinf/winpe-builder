using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Build;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.App;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(1) };

    private readonly AppPaths _paths = AppPaths.Default();
    private readonly ProcessRunner _runner = new();
    private readonly Dictionary<string, CheckBox> _componentBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<WinPeArchitecture> _builtThisSession = [];
    private readonly string _settingsPath;
    private readonly bool _ready;

    private CancellationTokenSource? _cts;
    private LogFile? _logFile;
    private AdkStatus _status = new(null, null, null);
    private string _lastOutputDefault = string.Empty;
    private bool _closeAfterOperation;

    public MainWindow()
    {
        InitializeComponent();
        _settingsPath = Path.Combine(_paths.Root, "settings.json");
        BuildComponentBoxes();
        ApplySettings(UserSettings.Load(_settingsPath));
        _ready = true;
        UpdateSourceState();
        RefreshStatus();
        Closing += MainWindow_Closing;
    }

    private AdkLocator Locator => AdkLocator.CreateDefault(_paths.KnownPrivateAdkRoots());

    private WinPeArchitecture SelectedArchitecture => ArchX86Radio.IsChecked == true ? WinPeArchitecture.X86 : WinPeArchitecture.X64;

    private WinPeSourceKind SelectedSource => SourceIsoRadio.IsChecked == true ? WinPeSourceKind.BootWimFromIso : WinPeSourceKind.Adk;

    private string DefaultOutputPath(WinPeArchitecture arch) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinPEBuilder", $"WinPE_{arch.ToAdkName()}.iso");

    // ---- configurações ------------------------------------------------------------------------------

    private void BuildComponentBoxes()
    {
        foreach (var component in OptionalComponents.All)
        {
            var box = new CheckBox
            {
                Content = component.DisplayName,
                Tag = component,
                Margin = new Thickness(0, 2, 16, 2),
                ToolTip = component.Requires.Length == 0 ? component.Name : $"{component.Name} (requer: {string.Join(", ", component.Requires)})",
            };
            box.Click += ComponentBox_Click;
            _componentBoxes[component.Name] = box;
            ComponentsPanel.Children.Add(box);
        }
    }

    /// <summary>Marcar um pacote marca o que ele exige; desmarcar um pacote desmarca quem depende dele.</summary>
    private void ComponentBox_Click(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        var component = (OptionalComponent)box.Tag;
        if (box.IsChecked == true)
        {
            foreach (var requirement in component.Requires)
            {
                _componentBoxes[requirement].IsChecked = true;
            }
        }
        else
        {
            foreach (var other in OptionalComponents.All.Where(c => c.Requires.Contains(component.Name)))
            {
                _componentBoxes[other.Name].IsChecked = false;
            }
        }
    }

    private void ApplySettings(UserSettings s)
    {
        (s.Architecture == WinPeArchitecture.X86 ? ArchX86Radio : ArchX64Radio).IsChecked = true;
        (s.Source == WinPeSourceKind.BootWimFromIso ? SourceIsoRadio : SourceAdkRadio).IsChecked = true;
        IsoPathBox.Text = s.SourceIsoPath;
        foreach (var (name, box) in _componentBoxes)
        {
            box.IsChecked = s.Components.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        KeyboardBox.Text = s.InputLocale;
        LabelBox.Text = s.VolumeLabel;
        FilesFolderBox.Text = s.FilesFolder;
        (s.FilesMode == FilesMode.InsideImage ? FilesInsideRadio : FilesOnIsoRadio).IsChecked = true;
        _lastOutputDefault = DefaultOutputPath(s.Architecture);
        OutputBox.Text = string.IsNullOrWhiteSpace(s.OutputIsoPath) ? _lastOutputDefault : s.OutputIsoPath;
    }

    private UserSettings CollectSettings() => new()
    {
        Architecture = SelectedArchitecture,
        Source = SelectedSource,
        SourceIsoPath = IsoPathBox.Text.Trim(),
        Components = [.. _componentBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key)],
        InputLocale = KeyboardBox.Text.Trim(),
        VolumeLabel = LabelBox.Text.Trim(),
        FilesFolder = FilesFolderBox.Text.Trim(),
        FilesMode = FilesInsideRadio.IsChecked == true ? FilesMode.InsideImage : FilesMode.OnIso,
        OutputIsoPath = OutputBox.Text.Trim(),
    };

    /// <summary>Monta as opções de geração a partir da tela; null (com mensagem) se algo estiver inválido.</summary>
    private BuildOptions? ReadOptions()
    {
        var s = CollectSettings();
        BuildOptions options;
        try
        {
            options = new BuildOptions
            {
                Architecture = s.Architecture,
                Source = s.Source,
                SourceIsoPath = s.Source == WinPeSourceKind.BootWimFromIso ? Path.GetFullPath(s.SourceIsoPath) : null,
                WorkDirectory = _paths.WorkDirectory(s.Architecture),
                OutputIsoPath = Path.GetFullPath(s.OutputIsoPath),
                OptionalComponents = s.Source == WinPeSourceKind.Adk ? s.Components : [],
                InputLocale = s.InputLocale,
                VolumeLabel = s.VolumeLabel,
                FilesFolder = string.IsNullOrEmpty(s.FilesFolder) ? null : Path.GetFullPath(s.FilesFolder),
                FilesMode = string.IsNullOrEmpty(s.FilesFolder) ? FilesMode.None : s.FilesMode,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowError("Algum caminho informado é inválido. Confira os campos de ISO, arquivos e saída.");
            return null;
        }

        var errors = options.Validate();
        if (errors.Count > 0)
        {
            ShowError(string.Join(Environment.NewLine, errors));
            return null;
        }

        return options;
    }

    // ---- requisitos -------------------------------------------------------------------------------------

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        _status = Locator.GetStatus();
        DeploymentToolsText.Text = Describe("Deployment Tools (DISM + oscdimg)", _status.DeploymentTools);
        WinPeX64Text.Text = Describe($"WinPE add-on {WinPeArchitecture.X64.ToDisplayName()}", _status.WinPeX64);
        WinPeX86Text.Text = Describe($"WinPE add-on {WinPeArchitecture.X86.ToDisplayName()}", _status.WinPeX86);
        UpdateSourceState();
    }

    private static string Describe(string what, AdkInstall? install) =>
        install is null ? $"✖ {what}: não encontrado" : $"✔ {what}: {install.KitsRoot}";

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        // Acompanha o nome padrão da ISO quando o usuário ainda não personalizou o caminho.
        if (string.Equals(OutputBox.Text.Trim(), _lastOutputDefault, StringComparison.OrdinalIgnoreCase))
        {
            _lastOutputDefault = DefaultOutputPath(SelectedArchitecture);
            OutputBox.Text = _lastOutputDefault;
        }

        UpdateSourceState();
    }

    private void UpdateSourceState()
    {
        if (!_ready)
        {
            return;
        }

        var useIso = SelectedSource == WinPeSourceKind.BootWimFromIso;
        IsoSourcePanel.IsEnabled = useIso;
        ComponentsPanel.IsEnabled = !useIso;

        var arch = SelectedArchitecture;
        if (useIso)
        {
            SourceHint.Text = "Usa o boot.wim da ISO (WinPE básico, sem pacotes opcionais). Ainda precisa do Deployment Tools para gerar a ISO. "
                + $"A ISO deve ser {arch.ToDisplayName()}.";
        }
        else if (_status.WinPeFor(arch) is null)
        {
            SourceHint.Text = arch == WinPeArchitecture.X86
                ? "WinPE add-on de 32 bits não instalado. Ele só existe no ADK do Windows 10 2004 (botão acima) — ou use \"boot.wim de uma ISO\" com uma ISO do Windows 32 bits."
                : "WinPE add-on de 64 bits não instalado. Use o botão \"Instalar ADK (64 bits)\".";
        }
        else
        {
            SourceHint.Text = "WinPE add-on encontrado: pronto para gerar.";
        }
    }

    // ---- escolha de arquivos --------------------------------------------------------------------------

    private void IsoBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ISO do Windows (*.iso)|*.iso", Title = "ISO de instalação do Windows" };
        if (dialog.ShowDialog(this) == true)
        {
            IsoPathBox.Text = dialog.FileName;
        }
    }

    private void FilesBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pasta com os arquivos que irão para o WinPE" };
        if (dialog.ShowDialog(this) == true)
        {
            FilesFolderBox.Text = dialog.FolderName;
        }
    }

    private void OutputBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ISO (*.iso)|*.iso",
            Title = "Salvar a ISO do WinPE",
            FileName = Path.GetFileName(OutputBox.Text.Trim()),
            DefaultExt = ".iso",
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputBox.Text = dialog.FileName;
        }
    }

    // ---- execução (compartilhada entre instalar / gerar / gravar) ---------------------------------------

    private void Log(string line)
    {
        _logFile?.WriteLine(line);
        Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "WinPE Builder", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void SetBusy(bool busy)
    {
        ConfigPanel.IsEnabled = !busy;
        BuildButton.IsEnabled = !busy;
        UsbButton.IsEnabled = !busy && _builtThisSession.Contains(SelectedArchitecture);
        CancelButton.IsEnabled = busy;
        if (!busy)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Cancelando…";
        _cts?.Cancel();
    }

    /// <summary>Roda uma operação longa com log em arquivo, cancelamento e tratamento de erros amigável.</summary>
    private async Task<bool> RunOperationAsync(string title, Func<CancellationToken, Task> operation)
    {
        SetBusy(true);
        _cts = new CancellationTokenSource();
        _logFile = LogFile.Create(Path.Combine(_paths.Root, "logs"));
        StatusText.Text = title;
        Log($"=== {title}");
        try
        {
            await operation(_cts.Token);
            StatusText.Text = "Concluído.";
            Log("Concluído.");
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelado.";
            Log("Cancelado pelo usuário.");
        }
        catch (BuildException ex)
        {
            Fail(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException or Win32Exception)
        {
            Log("ERRO TÉCNICO: " + ex);
            Fail($"Ocorreu um erro inesperado. Os detalhes estão no log:{Environment.NewLine}{_logFile?.Path}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _logFile?.Dispose();
            _logFile = null;
            SetBusy(false);
            RefreshStatus();
            if (_closeAfterOperation)
            {
                Close();
            }
        }

        return false;
    }

    private void Fail(string message)
    {
        StatusText.Text = "Falhou.";
        Log("ERRO: " + message);
        MessageBox.Show(this, message, "Não foi possível concluir", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // ---- instalar ADK -------------------------------------------------------------------------------------

    private async void InstallX64Button_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Será baixado e instalado o Windows ADK (Deployment Tools + WinPE add-on) da Microsoft no local padrão. Continuar?",
            "Instalar ADK (64 bits)", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            await InstallAsync(AdkCatalog.Current, installPath: null);
        }
    }

    private async void InstallX86Button_Click(object sender, RoutedEventArgs e)
    {
        var release = AdkCatalog.Legacy2004;
        var target = _paths.PrivateAdkRoot(release.Id);
        var confirm = MessageBox.Show(
            this,
            "O WinPE de 32 bits só existe no ADK do Windows 10 2004 (a Microsoft removeu o x86 das versões novas).\n\n" +
            $"Será instalado numa pasta própria do aplicativo:\n{target}\n\n" +
            "Se o instalador recusar por já existir um ADK mais novo, use a origem \"boot.wim de uma ISO\" com uma ISO do Windows 32 bits. Continuar?",
            "Instalar ADK Windows 10 2004 (32 bits)", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            await InstallAsync(release, target);
        }
    }

    private Task<bool> InstallAsync(AdkRelease release, string? installPath) =>
        RunOperationAsync($"Instalando: {release.DisplayName}", async ct =>
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                Progress.IsIndeterminate = p.Fraction is null;
                Progress.Value = p.Fraction ?? 0;
                StatusText.Text = p.Message;
                Log(p.Message);
            });
            var installer = new AdkInstaller(_runner, new HttpFileDownloader(Http), _paths.Downloads);
            await installer.InstallAsync(release, installPath, progress, Log, ct);
        });

    // ---- gerar ISO ----------------------------------------------------------------------------------------

    private async void BuildButton_Click(object sender, RoutedEventArgs e)
    {
        var options = ReadOptions();
        if (options is null)
        {
            return;
        }

        CollectSettings().TrySave(_settingsPath);

        if (File.Exists(options.OutputIsoPath) &&
            MessageBox.Show(this, $"A ISO já existe e será substituída:\n{options.OutputIsoPath}\n\nContinuar?",
                "Substituir ISO", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!HasEnoughDiskSpace(options))
        {
            return;
        }

        var service = new WinPeBuildService(_runner, Locator, _paths.Root);
        var ok = await RunOperationAsync($"Gerando WinPE {options.Architecture.ToDisplayName()}", async ct =>
        {
            var progress = new Progress<BuildProgress>(p =>
            {
                Progress.IsIndeterminate = false;
                Progress.Value = p.Total == 0 ? 0 : (double)(p.Step - 1) / p.Total;
                StatusText.Text = $"[{p.Step}/{p.Total}] {p.Message}";
            });
            await service.BuildAsync(options, progress, Log, ct);
        });

        if (ok)
        {
            _builtThisSession.Add(options.Architecture);
            OpenFolderButton.Tag = options.OutputIsoPath;
            OpenFolderButton.IsEnabled = true;
            UsbButton.IsEnabled = SelectedArchitecture == options.Architecture;
            Progress.Value = 1;
            MessageBox.Show(this, $"ISO gerada com sucesso:\n{options.OutputIsoPath}", "WinPE Builder", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>O copype + imagem montada + ISO precisam de alguns GB livres; avisa antes de começar.</summary>
    private bool HasEnoughDiskSpace(BuildOptions options)
    {
        const long needed = 4L * 1024 * 1024 * 1024;
        try
        {
            Directory.CreateDirectory(_paths.Root);
            var work = new DriveInfo(Path.GetPathRoot(_paths.Root)!);
            var output = new DriveInfo(Path.GetPathRoot(options.OutputIsoPath)!);
            var low = new[] { work, output }.Where(d => d.IsReady && d.AvailableFreeSpace < needed).Select(d => d.Name).Distinct().ToList();
            if (low.Count > 0)
            {
                return MessageBox.Show(this,
                    $"Pouco espaço livre em {string.Join(", ", low)} (menos de 4 GB). A geração pode falhar. Continuar mesmo assim?",
                    "Pouco espaço em disco", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Não conseguiu medir o espaço: não bloqueia.
        }

        return true;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (OpenFolderButton.Tag is string iso && File.Exists(iso))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{iso}\"") { UseShellExecute = true });
        }
    }

    // ---- pendrive -----------------------------------------------------------------------------------------

    private async void UsbButton_Click(object sender, RoutedEventArgs e)
    {
        var arch = SelectedArchitecture;
        var kit = Locator.FindWinPe(arch);
        if (kit is null || !_builtThisSession.Contains(arch))
        {
            ShowError("Gere o WinPE desta arquitetura primeiro. A gravação em pendrive também exige o WinPE add-on do ADK.");
            return;
        }

        var writer = new UsbMediaWriter(_runner);
        var drives = writer.GetCandidates();
        if (drives.Count == 0)
        {
            ShowError("Nenhum pendrive removível encontrado. Conecte o pendrive e tente novamente.");
            return;
        }

        var dialog = new UsbWindow(drives) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedDrive is null)
        {
            return;
        }

        var letter = dialog.SelectedDrive.Letter;
        await RunOperationAsync($"Gravando no pendrive {letter}:", ct =>
            writer.WriteAsync(kit, _paths.WorkDirectory(arch), letter, Log, ct));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_cts is not null)
        {
            var confirm = MessageBox.Show(this, "Há uma operação em andamento. Cancelar e sair?", "WinPE Builder",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            // Não fecha agora: o cancelamento precisa terminar (desmontar a imagem com /Discard) antes de sair,
            // senão o WIM ficaria montado e travaria a próxima geração.
            _closeAfterOperation = true;
            StatusText.Text = "Cancelando e limpando antes de sair…";
            _cts.Cancel();
            e.Cancel = true;
            return;
        }

        CollectSettings().TrySave(_settingsPath);
    }
}
