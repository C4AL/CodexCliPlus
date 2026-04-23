using System.Windows;
using System.Windows.Controls;

namespace CPAD.Management.DesignSystem.Controls;

public partial class ManagementFieldRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ManagementFieldRow), new PropertyMetadata(string.Empty, OnDisplayPropertyChanged));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(ManagementFieldRow), new PropertyMetadata(string.Empty, OnDisplayPropertyChanged));

    public static readonly DependencyProperty FieldContentProperty =
        DependencyProperty.Register(nameof(FieldContent), typeof(object), typeof(ManagementFieldRow), new PropertyMetadata(null));

    public ManagementFieldRow()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public object? FieldContent
    {
        get => GetValue(FieldContentProperty);
        set => SetValue(FieldContentProperty, value);
    }

    private static void OnDisplayPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ManagementFieldRow)d).UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        LabelBlock.Visibility = string.IsNullOrWhiteSpace(Label) ? Visibility.Collapsed : Visibility.Visible;
        HintBlock.Visibility = string.IsNullOrWhiteSpace(Hint) ? Visibility.Collapsed : Visibility.Visible;
    }
}
