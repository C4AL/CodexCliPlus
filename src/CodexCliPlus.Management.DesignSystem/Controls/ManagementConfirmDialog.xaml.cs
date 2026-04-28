using System.Windows;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class ManagementConfirmDialog : Window
{
    public ManagementConfirmDialog(string title, string message, string confirmText, string cancelText)
    {
        InitializeComponent();
        Title = title;
        DataContext = new DialogViewModel(message, confirmText, cancelText);
    }

    public static bool Confirm(Window? owner, string title, string message, string confirmText = "确定", string cancelText = "取消")
    {
        var dialog = new ManagementConfirmDialog(title, message, confirmText, cancelText)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed record DialogViewModel(string Message, string ConfirmText, string CancelText);
}
