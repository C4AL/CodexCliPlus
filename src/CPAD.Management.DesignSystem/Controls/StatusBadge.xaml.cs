using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CPAD.Management.DesignSystem.Controls;

public partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(BadgeTone), typeof(StatusBadge), new PropertyMetadata(BadgeTone.Neutral, OnToneChanged));

    public StatusBadge()
    {
        InitializeComponent();
        ApplyTone();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public BadgeTone Tone
    {
        get => (BadgeTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StatusBadge)d).ApplyTone();
    }

    private void ApplyTone()
    {
        var resources = Application.Current.Resources;
        var (backgroundKey, foregroundKey) = Tone switch
        {
            BadgeTone.Accent => ("ManagementSurfaceAccentBrush", "ManagementAccentStrongBrush"),
            BadgeTone.Success => ("ManagementSuccessSoftBrush", "ManagementSuccessBrush"),
            BadgeTone.Warning => ("ManagementWarningSoftBrush", "ManagementWarningBrush"),
            BadgeTone.Danger => ("ManagementDangerSoftBrush", "ManagementDangerBrush"),
            _ => ("ManagementSurfaceSubtleBrush", "ManagementSecondaryTextBrush")
        };

        BadgeBorder.Background = (Brush)resources[backgroundKey];
        BadgeTextBlock.Foreground = (Brush)resources[foregroundKey];
    }
}
