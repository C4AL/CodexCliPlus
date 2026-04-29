using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class AuthFileCard : UserControl
{
    public static readonly RoutedEvent CheckedChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(CheckedChanged),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(AuthFileCard)
    );

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(AuthFileCard),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle),
        typeof(string),
        typeof(AuthFileCard),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty MetaTextProperty = DependencyProperty.Register(
        nameof(MetaText),
        typeof(string),
        typeof(AuthFileCard),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(AuthFileCard),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked),
        typeof(bool),
        typeof(AuthFileCard),
        new FrameworkPropertyMetadata(
            false,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnVisualStateChanged
        )
    );

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected),
        typeof(bool),
        typeof(AuthFileCard),
        new PropertyMetadata(false, OnVisualStateChanged)
    );

    public AuthFileCard()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    public event RoutedEventHandler CheckedChanged
    {
        add => AddHandler(CheckedChangedEvent, value);
        remove => RemoveHandler(CheckedChangedEvent, value);
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

    public string MetaText
    {
        get => (string)GetValue(MetaTextProperty);
        set => SetValue(MetaTextProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    private static void OnVisualStateChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e
    )
    {
        var card = (AuthFileCard)d;
        card.UpdateVisualState();
        if (e.Property == IsCheckedProperty)
        {
            card.RaiseEvent(new RoutedEventArgs(CheckedChangedEvent, card));
        }
    }

    private void UpdateVisualState()
    {
        var resources = Application.Current.Resources;
        CardBorder.Background = IsSelected
            ? (Brush)resources["ManagementSurfaceAccentBrush"]
            : (Brush)resources["ManagementSurfaceBrush"];
        CardBorder.BorderBrush =
            (IsSelected || IsChecked)
                ? (Brush)resources["ManagementAccentBrush"]
                : (Brush)resources["ManagementBorderBrush"];
    }
}
