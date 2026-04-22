using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPAD.Contracts;

namespace CPAD.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public IAsyncRelayCommand RefreshCommand { get; }

    public MainWindowViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
    }

    [ObservableProperty]
    private string shellTitle = "CPAD";

    [ObservableProperty]
    private string backendUrl = "http://127.0.0.1:17320";

    [ObservableProperty]
    private string backendStatus = "Waiting for CPAD.Service";

    [ObservableProperty]
    private string previewStatus = "Waiting for embedded preview";

    [ObservableProperty]
    private string lastRefreshDisplay = "Never refreshed";

    [ObservableProperty]
    private string installRoot = "Not loaded yet";

    [ObservableProperty]
    private string serviceSummary = "Service state is not loaded yet.";

    [ObservableProperty]
    private string managerSummary = "Windows service state is not loaded yet.";

    [ObservableProperty]
    private string codexSummary = "Codex mode is not loaded yet.";

    [ObservableProperty]
    private string cpaSummary = "CPA runtime state is not loaded yet.";

    [ObservableProperty]
    private string pluginSummary = "Plugin market is not loaded yet.";

    [ObservableProperty]
    private string updateSummary = "Update center is not loaded yet.";

    [ObservableProperty]
    private string managementPreviewUrl = "http://127.0.0.1:17320";

    [ObservableProperty]
    private bool isRefreshing;

    partial void OnIsRefreshingChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            var endpoint = $"{BackendUrl.TrimEnd('/')}/api/system/status";
            var snapshot = await _httpClient.GetFromJsonAsync<HostSnapshotDto>(endpoint);
            if (snapshot is null)
            {
                BackendStatus = "CPAD.Service returned an empty snapshot";
                LastRefreshDisplay = $"Refresh failed at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}";
                return;
            }

            ApplySnapshot(snapshot);
            BackendStatus = "CPAD.Service online";
            LastRefreshDisplay = $"Last refresh {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception ex)
        {
            BackendStatus = $"CPAD.Service unavailable: {ex.Message}";
            PreviewStatus = "Embedded preview will retry after the next refresh";
            LastRefreshDisplay = $"Refresh failed at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplySnapshot(HostSnapshotDto snapshot)
    {
        InstallRoot = string.IsNullOrWhiteSpace(snapshot.InstallRoot)
            ? "Install root is not available"
            : snapshot.InstallRoot;

        ServiceSummary = FormatServiceSummary(snapshot);
        ManagerSummary = FormatManagerSummary(snapshot.ManagerStatus);
        CodexSummary = FormatCodexSummary(snapshot.Codex);
        CpaSummary = FormatCpaSummary(snapshot.CpaRuntime);
        PluginSummary = FormatPluginSummary(snapshot.PluginMarket);
        UpdateSummary = FormatUpdateSummary(snapshot.UpdateCenter);
        ManagementPreviewUrl = ResolveManagementPreviewUrl(snapshot);
        PreviewStatus = "Refreshing embedded preview target";
    }

    private static string FormatServiceSummary(HostSnapshotDto snapshot)
    {
        if (snapshot.ServiceState is null)
        {
            return "The host service state file has not been written yet.";
        }

        var updatedAt = snapshot.ServiceState.UpdatedAt == default
            ? "unknown update time"
            : snapshot.ServiceState.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        return $"{snapshot.ServiceState.Phase} / {snapshot.ServiceState.Mode}. {snapshot.ServiceState.Message} Updated {updatedAt}.";
    }

    private static string FormatManagerSummary(ManagerStatusDto status)
    {
        if (!status.Installed)
        {
            return $"{status.ServiceName} is not installed.";
        }

        var startType = string.IsNullOrWhiteSpace(status.StartType) ? "unknown start type" : status.StartType;
        var binaryPath = string.IsNullOrWhiteSpace(status.BinaryPath) ? "binary path unavailable" : status.BinaryPath;
        return $"{status.State} / {startType}. {binaryPath}";
    }

    private static string FormatCodexSummary(CodexShimResolutionDto status)
    {
        var launchState = status.LaunchReady ? "ready" : "not ready";
        var targetPath = string.IsNullOrWhiteSpace(status.TargetPath) ? "runtime target not resolved" : status.TargetPath;
        return $"{status.Mode} mode is {launchState}. {status.LaunchMessage} Target: {targetPath}.";
    }

    private static string FormatCpaSummary(CpaRuntimeStatusDto status)
    {
        var runtimeState = status.Running ? $"running with pid {status.Pid}" : "not running";
        var healthState = status.HealthCheck.Checked
            ? status.HealthCheck.Healthy
                ? $"healthy ({status.HealthCheck.StatusCode})"
                : $"unhealthy ({status.HealthCheck.StatusCode})"
            : status.HealthCheck.Message;
        var managementUrl = string.IsNullOrWhiteSpace(status.ConfigInsight.ManagementUrl)
            ? "no management URL resolved"
            : status.ConfigInsight.ManagementUrl;

        return $"{status.Phase} / {runtimeState}. {status.Message} Health: {healthState}. Management: {managementUrl}.";
    }

    private static string FormatPluginSummary(PluginMarketStatusDto status)
    {
        if (status.Plugins.Count == 0)
        {
            return "No plugin catalog entries are loaded yet.";
        }

        var installed = status.Plugins.Count(plugin => plugin.Installed);
        var enabled = status.Plugins.Count(plugin => plugin.Enabled);
        var needsUpdate = status.Plugins.Count(plugin => plugin.NeedsUpdate);
        return $"{status.Plugins.Count} plugins, {installed} installed, {enabled} enabled, {needsUpdate} pending updates.";
    }

    private static string FormatUpdateSummary(UpdateCenterStatusDto status)
    {
        if (status.Sources.Count == 0)
        {
            return "No update sources have been recorded yet.";
        }

        var available = status.Sources.Count(source => source.Available);
        var dirty = status.Sources.Count(source => source.Dirty);
        return $"{status.Sources.Count} sources, {available} available, {dirty} dirty worktrees.";
    }

    private static string ResolveManagementPreviewUrl(HostSnapshotDto snapshot)
    {
        var managementUrl = snapshot.CpaRuntime.ConfigInsight.ManagementUrl;
        return string.IsNullOrWhiteSpace(managementUrl)
            ? "http://127.0.0.1:17320"
            : managementUrl;
    }
}
