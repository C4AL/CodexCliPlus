using System.Windows;
using System.Windows.Controls;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class ManagementEmptyState : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ManagementEmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(ManagementEmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(ManagementEmptyState), new PropertyMetadata(null, OnActionContentChanged));

    public ManagementEmptyState()
    {
        InitializeComponent();
        UpdateActionVisibility();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    private static void OnActionContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ManagementEmptyState)d).UpdateActionVisibility();
    }

    private void UpdateActionVisibility()
    {
        ActionPresenter.Visibility = ActionContent is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
