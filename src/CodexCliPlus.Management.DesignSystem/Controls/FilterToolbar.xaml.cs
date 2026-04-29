using System.Windows;
using System.Windows.Controls;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class FilterToolbar : UserControl
{
    public static readonly RoutedEvent SearchTextChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SearchTextChanged),
        RoutingStrategy.Bubble,
        typeof(TextChangedEventHandler),
        typeof(FilterToolbar)
    );

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText),
        typeof(string),
        typeof(FilterToolbar),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault
        )
    );

    public static readonly DependencyProperty LeadingContentProperty = DependencyProperty.Register(
        nameof(LeadingContent),
        typeof(object),
        typeof(FilterToolbar),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty TrailingContentProperty = DependencyProperty.Register(
        nameof(TrailingContent),
        typeof(object),
        typeof(FilterToolbar),
        new PropertyMetadata(null)
    );

    public FilterToolbar()
    {
        InitializeComponent();
    }

    public event TextChangedEventHandler SearchTextChanged
    {
        add => AddHandler(SearchTextChangedEvent, value);
        remove => RemoveHandler(SearchTextChangedEvent, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RaiseEvent(new TextChangedEventArgs(SearchTextChangedEvent, UndoAction.None));
    }
}
