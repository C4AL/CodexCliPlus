using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CPAD.Management.DesignSystem.Controls;

public partial class ProviderCard : UserControl
{
    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ProviderCard));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ProviderCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ProviderCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MetaTextProperty =
        DependencyProperty.Register(nameof(MetaText), typeof(string), typeof(ProviderCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(ProviderCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(ProviderCard), new PropertyMetadata(false, OnSelectionChanged));

    public ProviderCard()
    {
        InitializeComponent();
        UpdateSelectionState();
    }

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
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

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ProviderCard)d).UpdateSelectionState();
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }

    private void UpdateSelectionState()
    {
        var resources = Application.Current.Resources;
        var border = (Border)CardButton.Content;
        border.Background = IsSelected
            ? (Brush)resources["ManagementSurfaceAccentBrush"]
            : (Brush)resources["ManagementSurfaceBrush"];
        border.BorderBrush = IsSelected
            ? (Brush)resources["ManagementAccentBrush"]
            : (Brush)resources["ManagementBorderBrush"];
    }
}
