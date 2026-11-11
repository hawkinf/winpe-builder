using System.IO;
﻿using System.Net.Http;
using System.Windows;
using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.App;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(1) };

    private readonly AppPaths _paths = AppPaths.Default();
    private readonly ProcessRunner _runner = new();
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        RefreshStatus();
    }

    private AdkLocator Locator => AdkLocator.CreateDefault(_paths.KnownPrivateAdkRoots());

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void RefreshStatus()
    {
        AdkStatus status = Locator.GetStatus();
        DeploymentToolsText.Text = Describe("Deployment Tools (DISM + oscdimg)", status.DeploymentTools);
        WinPeX64Text.Text = Describe($"WinPE add-on {WinPeArchitecture.X64.ToDisplayName()}", status.WinPeX64);
        WinPeX86Text.Text = Describe($"WinPE add-on {WinPeArchitecture.X86.ToDisplayName()}", status.WinPeX86);
    }

    private static string Describe(string what, AdkInstall? install) =>
        install is null ? $"✖ {what}: não encontrado" : $"✔ {what}: {install.KitsRoot}";

    private void Log(string line) => Dispatcher.BeginInvoke(() =>
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    });

    private void SetBusy(bool busy)
    {
        InstallX64Button.IsEnabled = !busy;
        InstallX86Button.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

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
            "Se o instalador recusar por já existir um ADK mais novo, use a opção de extrair o boot.wim de uma ISO do Windows 32 bits. Continuar?",
            "Instalar ADK Windows 10 2004 (32 bits)", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            await InstallAsync(release, target);
        }
    }

    private async Task InstallAsync(AdkRelease release, string? installPath)
    {
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<InstallProgress>(p =>
        {
            Progress.IsIndeterminate = p.Fraction is null;
            Progress.Value = p.Fraction ?? 0;
            Log(p.Message);
        });

        try
        {
            Log($"== {release.DisplayName}");
            var installer = new AdkInstaller(_runner, new HttpFileDownloader(Http), _paths.Downloads);
            await installer.InstallAsync(release, installPath, progress, Log, _cts.Token);
            Log("Concluído.");
        }
        catch (OperationCanceledException)
        {
            Log("Cancelado pelo usuário.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException)
        {
            Log("ERRO: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Falha na instalação do ADK", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
            RefreshStatus();
        }
    }
}
