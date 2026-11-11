using System.Windows;
using System.Windows.Controls;
using WinPEBuilder.Core.Build;

namespace WinPEBuilder.App;

/// <summary>Escolha do pendrive com confirmação digitada (a gravação formata o disco).</summary>
public partial class UsbWindow : Window
{
    public UsbWindow(IReadOnlyList<RemovableDrive> drives)
    {
        InitializeComponent();
        DriveBox.ItemsSource = drives;
        if (drives.Count == 1)
        {
            DriveBox.SelectedIndex = 0;
        }
    }

    public RemovableDrive? SelectedDrive { get; private set; }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (OkButton is null || ConfirmBox is null)
        {
            return; // eventos disparados durante o InitializeComponent
        }

        var drive = DriveBox.SelectedItem as RemovableDrive;
        OkButton.IsEnabled = drive is not null
            && string.Equals(ConfirmBox.Text.Trim().TrimEnd(':'), drive.Letter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedDrive = DriveBox.SelectedItem as RemovableDrive;
        DialogResult = SelectedDrive is not null;
    }
}
