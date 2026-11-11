using System.Windows;
using WinPEBuilder.Core;
using WinPEBuilder.Core.Adk;

namespace WinPEBuilder.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RefreshStatus();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void RefreshStatus()
    {
        AdkStatus status = AdkLocator.CreateDefault().GetStatus();
        DeploymentToolsText.Text = Describe("Deployment Tools (DISM + oscdimg)", status.DeploymentTools);
        WinPeX64Text.Text = Describe($"WinPE add-on {WinPeArchitecture.X64.ToDisplayName()}", status.WinPeX64);
        WinPeX86Text.Text = Describe($"WinPE add-on {WinPeArchitecture.X86.ToDisplayName()}", status.WinPeX86);
    }

    private static string Describe(string what, AdkInstall? install) =>
        install is null ? $"✖ {what}: não encontrado" : $"✔ {what}: {install.KitsRoot}";
}
