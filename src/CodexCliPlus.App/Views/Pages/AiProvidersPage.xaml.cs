using System.Windows;
using System.Windows.Controls;

using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Management.DesignSystem.Controls;
using CodexCliPlus.Services.SecondaryRoutes;
using CodexCliPlus.ViewModels.Pages;

namespace CodexCliPlus.Views.Pages;

public partial class AiProvidersPage : Page
{
    private readonly AiProvidersPageViewModel _viewModel;
    private readonly IManagementNavigationService _navigationService;
    private readonly AiProvidersRouteState _routeState;
    private readonly ManagementSecondaryRouteHost _routeHost;

    public AiProvidersPage(
        AiProvidersPageViewModel viewModel,
        IManagementAuthService authService,
        IManagementNavigationService navigationService,
        IUnsavedChangesGuard unsavedChangesGuard,
        AiProvidersRouteState routeState)
    {
        _viewModel = viewModel;
        _navigationService = navigationService;
        _routeState = routeState;

        DataContext = _viewModel;
        InitializeComponent();

        ManagementSecondaryRouteHost? routeHost = null;
        var routeFactory = new AiProvidersSecondaryRouteViewFactory(
            _viewModel,
            authService,
            this,
            _routeState,
            RefreshAndRenderAsync,
            (routeKey, description) => routeHost?.TryNavigate(routeKey, description) ?? false);
        _routeHost = routeHost = new ManagementSecondaryRouteHost(
            DetailShell,
            "ai-providers",
            _navigationService,
            unsavedChangesGuard,
            routeFactory);

        Loaded += async (_, _) => await RefreshAndRenderAsync();
        _navigationService.RouteChanged += NavigationService_RouteChanged;
    }

    private void NavigationService_RouteChanged(object? sender, EventArgs e)
    {
        if (string.Equals(_navigationService.SelectedPrimaryRouteKey, "ai-providers", StringComparison.OrdinalIgnoreCase))
        {
            Render();
        }
    }

    private async Task RefreshAndRenderAsync()
    {
        await _viewModel.RefreshAsync();
        NormalizeRouteSelection();
        Render();
    }

    private void Render()
    {
        StatusBadge.Text = _viewModel.Status;
        StatusBadge.Tone = ManagementPageSupport.GetTone(_viewModel.Error);
        ErrorTextBlock.Text = _viewModel.Error;
        ErrorTextBlock.Visibility = string.IsNullOrWhiteSpace(_viewModel.Error) ? Visibility.Collapsed : Visibility.Visible;

        ConfigureCard(GeminiCard, "gemini", "Gemini", "Native route pages for Gemini keys and model aliases.", $"{_viewModel.Gemini.Count} entries");
        ConfigureCard(CodexCard, "codex", "Codex", "Base URL, prefix, excluded models and headers.", $"{_viewModel.Codex.Count} entries");
        ConfigureCard(ClaudeCard, "claude", "Claude", "Entry editor plus dedicated model definitions route.", $"{_viewModel.Claude.Count} entries");
        ConfigureCard(VertexCard, "vertex", "Vertex", "Provider key forms without JSON fallback.", $"{_viewModel.Vertex.Count} entries");
        ConfigureCard(OpenAiCard, "openai", "OpenAI Compatibility", "Compatibility providers and model definitions.", $"{_viewModel.OpenAi.Count} entries");
        ConfigureCard(AmpcodeCard, "ampcode", "AmpCode", "Base settings, model mappings and upstream key mappings.", $"{(_viewModel.AmpCode?.ModelMappings.Count ?? 0)} mappings");

        RouteTextBlock.Text = _navigationService.CurrentRoute?.ParentKey == "ai-providers"
            ? _navigationService.CurrentRoute.Path
            : "/ai-providers";

        _routeHost.Refresh();
    }

    private void ConfigureCard(ProviderCard card, string key, string title, string subtitle, string meta)
    {
        card.Tag = key;
        card.Title = title;
        card.Subtitle = subtitle;
        card.MetaText = meta;
        card.BadgeText = string.Equals(_routeState.SelectedProviderKey, key, StringComparison.OrdinalIgnoreCase)
            ? "Current"
            : "Open";
        card.IsSelected = string.Equals(_routeState.SelectedProviderKey, key, StringComparison.OrdinalIgnoreCase);
    }

    private void NormalizeRouteSelection()
    {
        var selectedProvider = _routeState.SelectedProviderKey;

        if (string.Equals(selectedProvider, "ampcode", StringComparison.OrdinalIgnoreCase))
        {
            _routeState.SetRoute("ampcode", "ai-providers-ampcode", 0);
            if (_navigationService.CurrentRoute?.ParentKey != "ai-providers")
            {
                _routeHost.TryNavigate("ai-providers-ampcode", "AmpCode");
            }

            return;
        }

        var routeKey = _navigationService.CurrentSecondaryRouteKey;
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            NavigateToProvider(selectedProvider, preferNew: GetProviderEntryCount(selectedProvider) == 0);
            return;
        }

        var count = GetProviderEntryCount(selectedProvider);
        if (count == 0)
        {
            _routeState.SetRoute(selectedProvider, GetEditableRouteKey(selectedProvider, isNew: true), 0);
            return;
        }

        if (!IsNewRoute(routeKey) && _routeState.GetSelectedIndex(selectedProvider, count) >= count)
        {
            _routeState.SetRoute(selectedProvider, GetEditableRouteKey(selectedProvider, isNew: false), count - 1);
        }
    }

    private int GetProviderEntryCount(string providerKey)
    {
        return providerKey switch
        {
            "gemini" => _viewModel.Gemini.Count,
            "codex" => _viewModel.Codex.Count,
            "claude" => _viewModel.Claude.Count,
            "vertex" => _viewModel.Vertex.Count,
            "openai" => _viewModel.OpenAi.Count,
            "ampcode" => 1,
            _ => 0
        };
    }

    private void NavigateToProvider(string providerKey, bool preferNew)
    {
        if (string.Equals(providerKey, "ampcode", StringComparison.OrdinalIgnoreCase))
        {
            var previousAmpProvider = _routeState.SelectedProviderKey;
            var previousAmpRoute = _routeState.SelectedRouteKey;
            var previousAmpIndex = _routeState.GetSelectedIndex(previousAmpProvider, int.MaxValue);
            _routeState.SetRoute("ampcode", "ai-providers-ampcode", 0);
            if (!_routeHost.TryNavigate("ai-providers-ampcode", "AmpCode"))
            {
                _routeState.SetRoute(previousAmpProvider, previousAmpRoute, previousAmpIndex);
                return;
            }

            Render();
            return;
        }

        var count = GetProviderEntryCount(providerKey);
        var isNew = preferNew || count == 0;
        var index = isNew ? count : _routeState.GetSelectedIndex(providerKey, count);
        var routeKey = GetEditableRouteKey(providerKey, isNew);

        var previousProvider = _routeState.SelectedProviderKey;
        var previousRoute = _routeState.SelectedRouteKey;
        var previousIndex = _routeState.GetSelectedIndex(previousProvider, int.MaxValue);

        _routeState.SetRoute(providerKey, routeKey, index);
        if (!_routeHost.TryNavigate(routeKey, $"Open {providerKey}"))
        {
            _routeState.SetRoute(previousProvider, previousRoute, previousIndex);
            return;
        }

        Render();
    }

    private static bool IsNewRoute(string? routeKey)
    {
        return routeKey?.EndsWith("-new", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string GetEditableRouteKey(string providerKey, bool isNew)
    {
        return providerKey switch
        {
            "gemini" => isNew ? "ai-providers-gemini-new" : "ai-providers-gemini-edit",
            "codex" => isNew ? "ai-providers-codex-new" : "ai-providers-codex-edit",
            "claude" => isNew ? "ai-providers-claude-new" : "ai-providers-claude-edit",
            "vertex" => isNew ? "ai-providers-vertex-new" : "ai-providers-vertex-edit",
            "openai" => isNew ? "ai-providers-openai-new" : "ai-providers-openai-edit",
            _ => "ai-providers"
        };
    }

    private void ProviderCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ProviderCard card || card.Tag is not string provider)
        {
            return;
        }

        NavigateToProvider(provider, preferNew: false);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAndRenderAsync();
    }
}
