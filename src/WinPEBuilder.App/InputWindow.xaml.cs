using System.Windows;

namespace WinPEBuilder.App;

/// <summary>Pergunta simples de uma linha de texto (ex.: nome de uma pasta da área de trabalho).</summary>
public partial class InputWindow : Window
{
    public string Value => ValueBox.Text.Trim();

    private InputWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            MessageBox.Show(this, "Digite um nome.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    /// <summary>Mostra a pergunta; devolve o texto digitado ou null se cancelou.</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial = "")
    {
        var window = new InputWindow(title, prompt, initial) { Owner = owner };
        return window.ShowDialog() == true ? window.Value : null;
    }
}
