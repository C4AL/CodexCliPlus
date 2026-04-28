using System.Windows;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class DiffDialog : Window
{
    public DiffDialog(string beforeText, string afterText, string summary)
    {
        InitializeComponent();
        DataContext = new DiffDialogViewModel(beforeText, afterText, summary);
    }

    public static bool Confirm(Window? owner, string beforeText, string afterText, string summary)
    {
        var dialog = new DiffDialog(beforeText, afterText, summary)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed record DiffDialogViewModel(string BeforeText, string AfterText, string Summary);
}
