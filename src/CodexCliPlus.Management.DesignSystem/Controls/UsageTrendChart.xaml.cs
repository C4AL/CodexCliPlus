using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace CodexCliPlus.Management.DesignSystem.Controls;

public partial class UsageTrendChart : UserControl
{
    private readonly CartesianChart _chart = new();

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(UsageTrendChart),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IReadOnlyDictionary<string, long>),
        typeof(UsageTrendChart),
        new PropertyMetadata(null, OnPointsChanged)
    );

    public UsageTrendChart()
    {
        InitializeComponent();
        ChartHost.Content = _chart;
        RenderChart();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IReadOnlyDictionary<string, long>? Points
    {
        get => (IReadOnlyDictionary<string, long>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UsageTrendChart)d).RenderChart();
    }

    private void RenderChart()
    {
        var labels = new List<string>();
        var values = new ObservableCollection<ObservablePoint>();

        if (Points is not null)
        {
            var index = 0;
            foreach (var pair in Points.OrderBy(item => item.Key).TakeLast(14))
            {
                labels.Add(pair.Key);
                values.Add(new ObservablePoint(index, pair.Value));
                index++;
            }
        }

        _chart.Series = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Values = values,
                Fill = null,
                GeometrySize = 8,
                Stroke = new SolidColorPaint(new SKColor(15, 118, 110), 3),
            },
        };

        _chart.XAxes = new[]
        {
            new Axis
            {
                Labels = labels,
                LabelsRotation = 0,
                TextSize = 11,
            },
        };

        _chart.YAxes = new[] { new Axis { TextSize = 11 } };
    }
}
