using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

using CPAD.ViewModels.Pages;

namespace CPAD.Views.Pages;

public partial class LogsPage : Page
{
    private readonly LogsPageViewModel _viewModel;
    private bool _hasLoaded;

    public LogsPage(LogsPageViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += LogsPage_Loaded;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.State.PropertyChanged += State_PropertyChanged;
    }

    private async void LogsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            Render();
            return;
        }

        _hasLoaded = true;
        _viewModel.State.MarkFeedMode(incremental: false);
        await _viewModel.RefreshAsync();
        Render();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogsPageViewModel.Status) or
            nameof(LogsPageViewModel.Error) or
            nameof(LogsPageViewModel.IsBusy) or
            nameof(LogsPageViewModel.Snapshot) or
            nameof(LogsPageViewModel.ErrorLogs))
        {
            Render();
        }
    }

    private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogsPageState.RequestId) or
            nameof(LogsPageState.RequestLogContent) or
            nameof(LogsPageState.RequestLogError) or
            nameof(LogsPageState.RequestLogFound) or
            nameof(LogsPageState.IsIncrementalResult))
        {
            Render();
        }
    }

    private void Render()
    {
        StatusBadge.Text = _viewModel.Status;
        StatusBadge.Tone = ManagementPageSupport.GetTone(_viewModel.Error);
        ErrorTextBlock.Text = _viewModel.Error;
        ErrorTextBlock.Visibility = string.IsNullOrWhiteSpace(_viewModel.Error) ? Visibility.Collapsed : Visibility.Visible;

        var snapshot = _viewModel.Snapshot;
        var visibleLineCount = snapshot?.Lines.Count ?? 0;
        var totalLineCount = snapshot?.LineCount ?? 0;
        var latestTimestamp = ManagementPageSupport.FormatUnixTimestamp(snapshot?.LatestTimestamp);

        VisibleLineCountText.Text = visibleLineCount.ToString(CultureInfo.CurrentCulture);
        TotalLineCountText.Text = totalLineCount.ToString(CultureInfo.CurrentCulture);
        LatestTimestampText.Text = latestTimestamp;
        FeedModeText.Text = _viewModel.State.FeedModeText;

        LogFeedViewer.Text = string.Join(Environment.NewLine, snapshot?.Lines ?? []);

        var errorLogItems = _viewModel.ErrorLogs
            .Select(file => new ErrorLogViewItem(
                file.Name,
                ManagementPageSupport.FormatFileSize(file.Size),
                ManagementPageSupport.FormatUnixTimestamp(file.Modified)))
            .ToArray();

        ErrorLogItems.ItemsSource = errorLogItems;
        ErrorLogItems.Visibility = errorLogItems.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorLogsEmptyState.Visibility = errorLogItems.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        RequestLogSummaryText.Text = GetRequestLogSummary();
        LoadRequestLogButton.IsEnabled = !_viewModel.IsBusy;
    }

    private string GetRequestLogSummary()
    {
        if (_viewModel.State.HasRequestLogContent)
        {
            return $"当前查看请求编号：{_viewModel.State.RequestId}";
        }

        if (_viewModel.State.HasRequestLogError)
        {
            return _viewModel.State.RequestLogError;
        }

        return "输入请求编号后会在当前页面内展示详情。";
    }

    private async void RefreshIncrementalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.State.MarkFeedMode(incremental: true);
        await _viewModel.RefreshAsync(_viewModel.Snapshot?.LatestTimestamp ?? 0);
        Render();
    }

    private async void RefreshFullButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.State.MarkFeedMode(incremental: false);
        await _viewModel.RefreshAsync();
        Render();
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.State.MarkFeedMode(incremental: false);
        await _viewModel.ClearAsync();
        Render();
    }

    private async void LoadRequestLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        var requestId = _viewModel.State.RequestId.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            _viewModel.State.ApplyRequestLogResult(string.Empty, RequestLogLookupResult.Failed("请输入请求编号。"));
            Render();
            return;
        }

        var result = await _viewModel.LookupRequestLogAsync(requestId);
        _viewModel.State.ApplyRequestLogResult(requestId, result);
        Render();
    }
}
