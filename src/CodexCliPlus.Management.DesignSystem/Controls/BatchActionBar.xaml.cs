using System.Windows;
using System.Windows.Controls;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class BatchActionBar : UserControl
{
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
        nameof(Summary),
        typeof(string),
        typeof(BatchActionBar),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions),
        typeof(object),
        typeof(BatchActionBar),
        new PropertyMetadata(null)
    );

    public BatchActionBar()
    {
        InitializeComponent();
    }

    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
