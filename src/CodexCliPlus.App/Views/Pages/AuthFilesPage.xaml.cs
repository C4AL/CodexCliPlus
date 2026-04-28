using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Management.DesignSystem.Controls;
using CodexCliPlus.Services.SecondaryRoutes;
using CodexCliPlus.ViewModels.Pages;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace CodexCliPlus.Views.Pages;

public partial class AuthFilesPage : Page
{
    private readonly AuthFilesPageViewModel _viewModel;
    private readonly IManagementNavigationService _navigationService;
    private readonly AuthFilesRouteState _routeState;
    private readonly ManagementSecondaryRouteHost _routeHost;
    private readonly ObservableCollection<AuthFileEntryItem> _items = [];

    public AuthFilesPage(
        AuthFilesPageViewModel viewModel,
        IManagementAuthService authService,
        IManagementNavigationService navigationService,
        IUnsavedChangesGuard unsavedChangesGuard,
        AuthFilesRouteState routeState)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        _routeState = routeState;

        DataContext = _viewModel;
        InitializeComponent();
        AuthFilesListBox.ItemsSource = _items;

        ManagementSecondaryRouteHost? routeHost = null;
        var routeFactory = new AuthFilesSecondaryRouteViewFactory(
            _viewModel,
            authService,
            this,
            _routeState,
            RefreshAndRenderAsync,
            (routeKey, description) => routeHost?.TryNavigate(routeKey, description) ?? false);
        _routeHost = routeHost = new ManagementSecondaryRouteHost(
            DetailShell,
            "auth-files",
            _navigationService,
            unsavedChangesGuard,
            routeFactory);

        Loaded += async (_, _) => await RefreshAndRenderAsync();
        _navigationService.RouteChanged += NavigationService_RouteChanged;
    }

    private void NavigationService_RouteChanged(object? sender, EventArgs e)
    {
        if (string.Equals(_navigationService.SelectedPrimaryRouteKey, "auth-files", StringComparison.OrdinalIgnoreCase))
        {
            Render();
        }
    }

    private async Task RefreshAndRenderAsync()
    {
        await _viewModel.RefreshAsync();
        RebuildItems();
        SyncSelection();
        Render();
    }

    private void RebuildItems()
    {
        foreach (var item in _items)
        {
            item.PropertyChanged -= EntryItem_PropertyChanged;
        }

        _items.Clear();
        foreach (var file in _viewModel.Files)
        {
            var entry = new AuthFileEntryItem(file);
            entry.PropertyChanged += EntryItem_PropertyChanged;
            _items.Add(entry);
        }
    }

    private void SyncSelection()
    {
        var selectedItem = _items.FirstOrDefault(item => string.Equals(item.Source.Name, _routeState.SelectedFileName, StringComparison.OrdinalIgnoreCase));
        AuthFilesListBox.SelectedItem = selectedItem;
        foreach (var item in _items)
        {
            item.IsSelected = item == selectedItem;
        }

        if (selectedItem is null)
        {
            _routeState.SetSelectedFile(null);
            _routeState.SetSelectedModels([]);
            _routeState.SetDraftFields(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private void EntryItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthFileEntryItem.IsChecked))
        {
            UpdateBatchBar();
        }
    }

    private void Render()
    {
        StatusBadge.Text = _viewModel.Status;
        StatusBadge.Tone = ManagementPageSupport.GetTone(_viewModel.Error);
        ErrorTextBlock.Text = _viewModel.Error;
        ErrorTextBlock.Visibility = string.IsNullOrWhiteSpace(_viewModel.Error) ? Visibility.Collapsed : Visibility.Visible;
        RouteTextBlock.Text = _navigationService.CurrentRoute?.ParentKey == "auth-files"
            ? _navigationService.CurrentRoute.Path
            : "/auth-files";
        UpdateBatchBar();
        _routeHost.Refresh();
    }

    private void UpdateBatchBar()
    {
        var selectedCount = _items.Count(item => item.IsChecked);
        BatchActionBar.Summary = selectedCount == 0
            ? string.Empty
            : $"Selected {selectedCount} auth files";
        BatchActionBar.Visibility = selectedCount == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAndRenderAsync();
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files|*.json",
            Multiselect = true
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var uploads = dialog.FileNames
            .Select(file => new ManagementAuthFileUpload
            {
                FileName = Path.GetFileName(file),
                Content = File.ReadAllBytes(file)
            })
            .ToArray();

        await _viewModel.UploadAsync(uploads);
        await RefreshAndRenderAsync();
    }

    private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var names = _items.Where(item => item.IsChecked).Select(item => item.Source.Name).ToArray();
        if (names.Length == 0)
        {
            return;
        }

        await _viewModel.DeleteAsync(names);
        await RefreshAndRenderAsync();
    }

    private async void AuthFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var item in _items)
        {
            item.IsSelected = item == AuthFilesListBox.SelectedItem;
        }

        if (AuthFilesListBox.SelectedItem is not AuthFileEntryItem selected)
        {
            _routeState.SetSelectedFile(null);
            _routeState.SetSelectedModels([]);
            _routeState.SetDraftFields(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            _routeState.MarkClean();
            Render();
            return;
        }

        _routeState.SetSelectedFile(selected.Source.Name);
        _routeState.MarkClean();
        await LoadSelectedFileContextAsync(selected.Source);
        Render();
    }

    private async Task LoadSelectedFileContextAsync(ManagementAuthFileItem file)
    {
        try
        {
            _routeState.SetSelectedModels((await _viewModel.GetModelsAsync(file.Name)).Value);
        }
        catch
        {
            _routeState.SetSelectedModels([]);
        }

        try
        {
            var payload = (await _viewModel.DownloadAsync(file.Name)).Value;
            var draft = ParseEditableFields(payload);
            _routeState.SetDraftFields(draft.Prefix, draft.ProxyUrl, draft.Headers);
        }
        catch
        {
            _routeState.SetDraftFields(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static AuthFileEditableFields ParseEditableFields(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var prefix = GetOptionalString(root, "prefix");
        var proxyUrl = GetOptionalString(root, "proxy-url", "proxy_url", "proxyUrl");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetProperty(root, out var headersElement, "headers") && headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in headersElement.EnumerateObject())
            {
                headers[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }
        }

        return new AuthFileEditableFields(prefix, proxyUrl, headers);
    }

    private static string? GetOptionalString(JsonElement element, params string[] names)
    {
        return TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record AuthFileEditableFields(
        string? Prefix,
        string? ProxyUrl,
        IReadOnlyDictionary<string, string> Headers);

    private sealed partial class AuthFileEntryItem : ObservableObject
    {
        [ObservableProperty]
        private bool _isChecked;

        [ObservableProperty]
        private bool _isSelected;

        public AuthFileEntryItem(ManagementAuthFileItem source)
        {
            Source = source;
        }

        public ManagementAuthFileItem Source { get; }

        public string Title => Source.Label ?? Source.Name;

        public string Subtitle => $"{ManagementPageSupport.FormatValue(Source.Email, "No email")} · {ManagementPageSupport.FormatValue(Source.AccountType, "Unknown type")}";

        public string MetaText => $"{ManagementPageSupport.FormatValue(Source.Provider, "Unknown provider")} · {ManagementPageSupport.FormatFileSize(Source.Size)}";

        public string StatusText => Source.Disabled ? "Disabled" : (Source.StatusMessage ?? Source.Status ?? "Available");
    }
}
