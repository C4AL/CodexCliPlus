using System.Windows;
using System.Windows.Controls;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class SecondaryPageShell : UserControl
{
    public static readonly RoutedEvent BackRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(BackRequested),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(SecondaryPageShell)
    );

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SecondaryPageShell),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(SecondaryPageShell),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
        nameof(BodyContent),
        typeof(object),
        typeof(SecondaryPageShell),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty HeaderActionsProperty = DependencyProperty.Register(
        nameof(HeaderActions),
        typeof(object),
        typeof(SecondaryPageShell),
        new PropertyMetadata(null, OnLayoutPropertyChanged)
    );

    public static readonly DependencyProperty FooterContentProperty = DependencyProperty.Register(
        nameof(FooterContent),
        typeof(object),
        typeof(SecondaryPageShell),
        new PropertyMetadata(null, OnLayoutPropertyChanged)
    );

    public static readonly DependencyProperty CanGoBackProperty = DependencyProperty.Register(
        nameof(CanGoBack),
        typeof(bool),
        typeof(SecondaryPageShell),
        new PropertyMetadata(false, OnLayoutPropertyChanged)
    );

    public static readonly DependencyProperty BackLabelProperty = DependencyProperty.Register(
        nameof(BackLabel),
        typeof(string),
        typeof(SecondaryPageShell),
        new PropertyMetadata("返回")
    );

    public SecondaryPageShell()
    {
        InitializeComponent();
        UpdateLayoutState();
    }

    public event RoutedEventHandler BackRequested
    {
        add => AddHandler(BackRequestedEvent, value);
        remove => RemoveHandler(BackRequestedEvent, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? BodyContent
    {
        get => GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public bool CanGoBack
    {
        get => (bool)GetValue(CanGoBackProperty);
        set => SetValue(CanGoBackProperty, value);
    }

    public string BackLabel
    {
        get => (string)GetValue(BackLabelProperty);
        set => SetValue(BackLabelProperty, value);
    }

    private static void OnLayoutPropertyChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        ((SecondaryPageShell)d).UpdateLayoutState();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(BackRequestedEvent, this));
    }

    private void UpdateLayoutState()
    {
        BackButton.Visibility = CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        HeaderActionsPresenter.Visibility = HeaderActions is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        FooterPresenter.Visibility = FooterContent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        TopBarPanel.Visibility =
            CanGoBack || HeaderActions is not null ? Visibility.Visible : Visibility.Collapsed;
    }
}
