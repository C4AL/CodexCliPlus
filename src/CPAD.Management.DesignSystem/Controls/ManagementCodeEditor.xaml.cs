using System.Windows;
using System.Windows.Controls;

namespace CPAD.Management.DesignSystem.Controls;

public partial class ManagementCodeEditor : UserControl
{
    private bool _updatingFromDependencyProperty;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ManagementCodeEditor), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(ManagementCodeEditor), new PropertyMetadata(false));

    public ManagementCodeEditor()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ManagementCodeEditor)d;
        var nextText = e.NewValue as string ?? string.Empty;
        if (editor.Editor.Text == nextText)
        {
            return;
        }

        editor._updatingFromDependencyProperty = true;
        editor.Editor.Text = nextText;
        editor._updatingFromDependencyProperty = false;
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromDependencyProperty)
        {
            return;
        }

        SetCurrentValue(TextProperty, Editor.Text);
    }
}
