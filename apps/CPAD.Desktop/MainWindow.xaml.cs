using System.ComponentModel;
using CPAD.Desktop.ViewModels;

namespace CPAD.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private bool _browserReady;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            _viewModel.PreviewStatus = "Loading WebView2 runtime";
            await Browser.EnsureCoreWebView2Async();
            _browserReady = true;
            _viewModel.PreviewStatus = "Embedded preview ready";
        }
        catch (Exception ex)
        {
            _browserReady = false;
            _viewModel.PreviewStatus = $"WebView2 unavailable: {ex.Message}";
        }

        await _viewModel.RefreshAsync();
        TryNavigateBrowser();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ManagementPreviewUrl) or nameof(MainWindowViewModel.BackendUrl))
        {
            TryNavigateBrowser();
        }
    }

    private void TryNavigateBrowser()
    {
        if (!_browserReady)
        {
            return;
        }

        var target = string.IsNullOrWhiteSpace(_viewModel.ManagementPreviewUrl)
            ? _viewModel.BackendUrl
            : _viewModel.ManagementPreviewUrl;

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            _viewModel.PreviewStatus = "Preview target is not a valid absolute URL";
            return;
        }

        Browser.Source = uri;
        _viewModel.PreviewStatus = $"Previewing {uri}";
    }
}
