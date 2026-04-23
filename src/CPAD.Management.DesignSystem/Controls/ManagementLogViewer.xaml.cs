using System.Windows;
using System.Windows.Controls;

namespace CPAD.Management.DesignSystem.Controls;

public partial class ManagementLogViewer : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ManagementLogViewer), new PropertyMetadata(string.Empty, OnDisplayPropertyChanged));

    public static readonly DependencyProperty EmptyTitleProperty =
        DependencyProperty.Register(nameof(EmptyTitle), typeof(string), typeof(ManagementLogViewer), new PropertyMetadata("暂无日志内容", OnDisplayPropertyChanged));

    public static readonly DependencyProperty EmptyDescriptionProperty =
        DependencyProperty.Register(nameof(EmptyDescription), typeof(string), typeof(ManagementLogViewer), new PropertyMetadata("可在当前页面中刷新、清空或复制日志。", OnDisplayPropertyChanged));

    public static readonly DependencyProperty CopyButtonTextProperty =
        DependencyProperty.Register(nameof(CopyButtonText), typeof(string), typeof(ManagementLogViewer), new PropertyMetadata("复制内容"));

    public ManagementLogViewer()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string EmptyTitle
    {
        get => (string)GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public string EmptyDescription
    {
        get => (string)GetValue(EmptyDescriptionProperty);
        set => SetValue(EmptyDescriptionProperty, value);
    }

    public string CopyButtonText
    {
        get => (string)GetValue(CopyButtonTextProperty);
        set => SetValue(CopyButtonTextProperty, value);
    }

    private static void OnDisplayPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ManagementLogViewer)d).UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        var hasText = !string.IsNullOrWhiteSpace(Text);
        ViewerBorder.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateControl.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.IsEnabled = hasText;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            return;
        }

        Clipboard.SetText(Text);
    }
}
