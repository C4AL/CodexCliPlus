using System.Windows;
using System.Windows.Controls;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class FloatingActionBar : UserControl
{
    public static readonly DependencyProperty LeadingContentProperty =
        DependencyProperty.Register(nameof(LeadingContent), typeof(object), typeof(FloatingActionBar), new PropertyMetadata(null));

    public static readonly DependencyProperty TrailingContentProperty =
        DependencyProperty.Register(nameof(TrailingContent), typeof(object), typeof(FloatingActionBar), new PropertyMetadata(null));

    public FloatingActionBar()
    {
        InitializeComponent();
    }

    public object? LeadingContent
    {
        get => GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public object? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }
}
