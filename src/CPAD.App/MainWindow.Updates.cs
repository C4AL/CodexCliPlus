using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using CPAD.Core.Enums;
using CPAD.Core.Models;

using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace CPAD;

public partial class MainWindow
{
    private UpdateCheckResult? _updatesStableResult;
    private UpdateCheckResult? _updatesBetaResult;
    private bool _updatesLoading;
    private string? _updatesError;
    private string? _updatesStatusMessage;
    private DateTimeOffset? _updatesLastCheckedAt;
    private UpdateChannel _updatesChannelDraft = UpdateChannel.Stable;
    private string? _updatesBackendCurrentVersion;
    private string? _updatesBackendCommit;
    private string? _updatesBackendBuildDate;
    private string? _updatesBackendLatestVersion;
    private string? _updatesBackendLatestVersionError;

    private UIElement BuildUpdatesContent()
    {
        if (_updatesLoading && _updatesStableResult is null && _updatesBetaResult is null)
        {
            return CreateStatePanel(
                "Checking desktop and backend versions...",
                "Querying GitHub Releases for the stable desktop channel and reading backend version diagnostics from the Management API when it is available.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateUpdatesHero());

        if (!string.IsNullOrWhiteSpace(_updatesStatusMessage))
        {
            root.Children.Add(CreateHintCard("Update action", _updatesStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_updatesError))
        {
            root.Children.Add(CreateHintCard("Update issue", _updatesError));
        }

        root.Children.Add(CreateSectionHeader("Channel Summary"));
        root.Children.Add(CreateUpdatesChannelCard());

        root.Children.Add(CreateSectionHeader("Desktop Stable Release"));
        root.Children.Add(CreateUpdatesStableReleaseCard());

        root.Children.Add(CreateSectionHeader("Backend Version Diagnostics"));
        root.Children.Add(CreateUpdatesBackendVersionCard());

        root.Children.Add(CreateSectionHeader("Release Assets"));
        root.Children.Add(CreateUpdatesAssetsCard());

        return root;
    }

    private async Task RefreshUpdatesAsync(bool force)
    {
        if (_updatesLoading)
        {
            return;
        }

        if (!force && _updatesLastCheckedAt is not null)
        {
            return;
        }

        _updatesLoading = true;
        _updatesError = null;
        _updatesStatusMessage = "Checking stable desktop releases and backend version metadata...";
        RefreshUpdatesSection();

        try
        {
            var currentVersion = _buildInfo.ApplicationVersion;
            var stableTask = _updateCheckService.CheckAsync(currentVersion, UpdateChannel.Stable);
            var betaTask = _updateCheckService.CheckAsync(currentVersion, UpdateChannel.Beta);
            var configTask = _managementConfigurationService.GetConfigAsync();
            var latestBackendTask = _systemService.GetLatestVersionAsync();

            _updatesStableResult = await stableTask;
            _updatesBetaResult = await betaTask;

            var backendErrors = new List<string>();
            try
            {
                var config = await configTask;
                _updatesBackendCurrentVersion = config.Metadata.Version;
                _updatesBackendCommit = config.Metadata.Commit;
                _updatesBackendBuildDate = config.Metadata.BuildDate;
            }
            catch (Exception exception)
            {
                _updatesBackendCurrentVersion = null;
                _updatesBackendCommit = null;
                _updatesBackendBuildDate = null;
                backendErrors.Add($"Backend metadata: {exception.Message}");
            }

            try
            {
                var latestBackend = await latestBackendTask;
                _updatesBackendLatestVersion = latestBackend.Value.LatestVersion;
                _updatesBackendLatestVersionError = null;
                _updatesBackendCurrentVersion ??= latestBackend.Metadata.Version;
                _updatesBackendCommit ??= latestBackend.Metadata.Commit;
                _updatesBackendBuildDate ??= latestBackend.Metadata.BuildDate;
            }
            catch (Exception exception)
            {
                _updatesBackendLatestVersion = null;
                _updatesBackendLatestVersionError = exception.Message;
                backendErrors.Add($"Backend latest-version: {exception.Message}");
            }

            _updatesLastCheckedAt = DateTimeOffset.Now;
            _updatesStatusMessage = BuildUpdatesStatusMessage(backendErrors);
            _updatesError = _updatesStableResult.IsCheckSuccessful
                ? (backendErrors.Count == 0 ? null : string.Join(" | ", backendErrors))
                : _updatesStableResult.ErrorMessage ?? _updatesStableResult.Detail;
        }
        catch (Exception exception)
        {
            _updatesError = exception.Message;
            _updatesStatusMessage = "Desktop update check failed before release information could be loaded.";
        }
        finally
        {
            _updatesLoading = false;
            RefreshUpdatesSection();
        }
    }

    private async Task SaveUpdatesChannelPreferenceAsync()
    {
        _settings.UseBetaChannel = _updatesChannelDraft == UpdateChannel.Beta;
        await _configurationService.SaveAsync(_settings);
        _updatesStatusMessage = _updatesChannelDraft == UpdateChannel.Beta
            ? "Beta preference saved. The beta channel is reserved, so stable releases remain the only active update source."
            : "Stable preference saved. GitHub Releases stable checks remain active.";
        await RefreshUpdatesAsync(force: true);
    }

    private UIElement CreateUpdatesHero()
    {
        var selected = _settings.UseBetaChannel ? UpdateChannel.Beta : UpdateChannel.Stable;
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Desktop update checks backed by GitHub Releases",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            $"Desktop version: {_buildInfo.ApplicationVersion} | Informational: {_buildInfo.InformationalVersion}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateText(
            $"Preferred channel: {FormatUpdateChannel(selected)} | Last check: {FormatDateTime(_updatesLastCheckedAt)}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        panel.Children.Add(CreateText(
            "Stable queries the Blackblock desktop repository's latest GitHub Release. Beta is kept as an explicit reserved entry so future package channels can be enabled without changing the desktop settings model.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateUpdatesChannelCard()
    {
        var channelBox = new WpfComboBox
        {
            ItemsSource = Enum.GetValues<UpdateChannel>(),
            SelectedItem = _updatesChannelDraft,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5)
        };
        channelBox.SelectionChanged += (_, _) =>
        {
            if (channelBox.SelectedItem is UpdateChannel selected)
            {
                _updatesChannelDraft = selected;
            }
        };

        var betaDetail = _updatesBetaResult is null
            ? "Beta reservation has not been evaluated yet."
            : _updatesBetaResult.Detail;

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Preferred Channel", FormatUpdateChannel(_settings.UseBetaChannel ? UpdateChannel.Beta : UpdateChannel.Stable), "Persisted in desktop.json"),
            CreateMetricCard("Check on Startup", _settings.CheckForUpdatesOnStartup ? "Enabled" : "Disabled", "Startup check runs without starting or stopping unrelated processes."),
            CreateMetricCard("Stable Source", "GitHub Releases", _updatesStableResult?.ApiUrl ?? "https://api.github.com/repos/Blackblock-inc/Cli-Proxy-API-Desktop/releases/latest"),
            CreateMetricCard("Beta Source", "Reserved", betaDetail),
            CreateMetricCard("Current Desktop", _buildInfo.ApplicationVersion, _buildInfo.InformationalVersion),
            CreateMetricCard("Result", _updatesStableResult?.Status ?? "Not checked", _updatesStableResult?.Detail ?? "Run Check Updates to query the stable channel.")));

        root.Children.Add(CreateConfigFieldShell(
            "Channel Preference",
            "Stable is active; Beta is stored as a reserved preference until a beta release line exists.",
            channelBox));

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton(_updatesLoading ? "Checking..." : "Check Updates", () => RefreshUpdatesAsync(force: true)));
        actions.Children.Add(CreateActionButton("Save Channel Preference", SaveUpdatesChannelPreferenceAsync));
        actions.Children.Add(CreateActionButton("Open Release Page", () =>
        {
            OpenUpdatesReleasePage();
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateUpdatesStableReleaseCard()
    {
        if (_updatesStableResult is null)
        {
            return CreateStatePanel(
                "No stable release check has been run yet.",
                "Use Check Updates to query the desktop repository's latest GitHub Release.");
        }

        var result = _updatesStableResult;
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Current", result.CurrentVersion, "Installed desktop application"),
            CreateMetricCard("Latest", result.LatestVersion ?? "Unavailable", result.IsNoReleasePublished ? "No stable release is published." : "Latest stable release tag"),
            CreateMetricCard("Status", result.Status, result.Detail),
            CreateMetricCard("Published", FormatDateTime(result.PublishedAt), "GitHub release published_at"),
            CreateMetricCard("Assets", result.Assets.Count.ToString(CultureInfo.InvariantCulture), "Release asset count"),
            CreateMetricCard("Checked", FormatDateTime(result.CheckedAt), result.IsCheckSuccessful ? "Release endpoint responded." : result.ErrorMessage ?? "Check failed.")));

        var details = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AddKeyValue(details, "Repository", result.Repository);
        AddKeyValue(details, "API endpoint", result.ApiUrl);
        AddKeyValue(details, "Release page", result.ReleasePageUrl ?? "Unavailable");
        AddKeyValue(details, "Channel", FormatUpdateChannel(result.Channel));
        root.Children.Add(details);

        if (result.IsNoReleasePublished)
        {
            root.Children.Add(CreateHintCard(
                "Stable release state",
                "The desktop repository currently has no stable release, so the correct user-facing state is 'No stable release' rather than an artificial update."));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateUpdatesBackendVersionCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Backend version diagnostics are read from the audited Management API path used by the upstream Management Center: /config response headers for current metadata and /latest-version for upstream comparison.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Backend Current", _updatesBackendCurrentVersion ?? "Unavailable", $"Commit: {_updatesBackendCommit ?? "unknown"}"),
            CreateMetricCard("Backend Latest", _updatesBackendLatestVersion ?? "Unavailable", string.IsNullOrWhiteSpace(_updatesBackendLatestVersionError) ? "Read from /latest-version" : _updatesBackendLatestVersionError),
            CreateMetricCard("Backend Build", FormatBuildDate(_updatesBackendBuildDate), "X-CPA-BUILD-DATE metadata"),
            CreateMetricCard("Backend State", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message),
            CreateMetricCard("Management API", _backendProcessManager.CurrentStatus.Runtime?.ManagementApiBaseUrl ?? "Unavailable", "Live backend route"),
            CreateMetricCard("Backend Result", BuildBackendUpdateComparison(), "Numeric comparison mirrors the upstream Management Center behavior.")));

        if (!string.IsNullOrWhiteSpace(_updatesBackendLatestVersionError))
        {
            root.Children.Add(CreateHintCard(
                "Backend latest-version detail",
                _updatesBackendLatestVersionError));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateUpdatesAssetsCard()
    {
        var result = _updatesStableResult;
        if (result is null)
        {
            return CreateStatePanel(
                "Release assets are not loaded.",
                "Run a stable update check first.");
        }

        if (result.Assets.Count == 0)
        {
            return CreateStatePanel(
                result.IsNoReleasePublished ? "No stable release assets are available." : "The latest stable release has no assets.",
                result.IsNoReleasePublished
                    ? "This is expected while the desktop repository has no published release."
                    : "Open the release page to inspect source archives or package availability.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Assets are listed directly from the latest stable GitHub Release document. Download and installer execution are intentionally left for the packaging/update chain phases.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        foreach (var asset in result.Assets)
        {
            var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
            panel.Children.Add(CreateText(asset.Name, 14, FontWeights.SemiBold, "PrimaryTextBrush"));
            panel.Children.Add(CreateText(
                $"{FormatUpdateAssetBytes(asset.Size)} | {asset.DownloadUrl}",
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 4, 0, 0)));
            if (!string.IsNullOrWhiteSpace(asset.Digest))
            {
                panel.Children.Add(CreateText(
                    asset.Digest,
                    12,
                    FontWeights.Normal,
                    "SecondaryTextBrush",
                    new Thickness(0, 4, 0, 0)));
            }

            root.Children.Add(CreateCard(panel, new Thickness(0, 0, 0, 10)));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private string BuildUpdatesStatusMessage(IReadOnlyCollection<string> backendErrors)
    {
        var selected = _settings.UseBetaChannel ? UpdateChannel.Beta : UpdateChannel.Stable;
        var channelNote = selected == UpdateChannel.Beta
            ? "Beta is selected but reserved; stable release state is still shown for safety."
            : "Stable channel is active.";
        var backendNote = backendErrors.Count == 0
            ? "Backend version diagnostics loaded."
            : "Backend version diagnostics are partially unavailable.";
        return $"Update check completed at {DateTimeOffset.Now.ToLocalTime():HH:mm:ss}. {channelNote} {backendNote}";
    }

    private string BuildBackendUpdateComparison()
    {
        if (!string.IsNullOrWhiteSpace(_updatesBackendLatestVersionError))
        {
            return "Check failed";
        }

        if (string.IsNullOrWhiteSpace(_updatesBackendCurrentVersion) || string.IsNullOrWhiteSpace(_updatesBackendLatestVersion))
        {
            return "Unavailable";
        }

        var comparison = CompareUpdateVersions(_updatesBackendLatestVersion, _updatesBackendCurrentVersion);
        return comparison switch
        {
            > 0 => "Update available",
            <= 0 => "Up to date",
            _ => "Review latest"
        };
    }

    private void OpenUpdatesPage(bool startCheck)
    {
        var index = _sections.FindIndex(section => section.Key == "updates");
        if (index >= 0)
        {
            NavigationList.SelectedIndex = index;
        }

        RestoreFromTray();
        if (startCheck)
        {
            _ = RefreshUpdatesAsync(force: true);
        }
    }

    private void OpenUpdatesReleasePage()
    {
        var url = _updatesStableResult?.ReleasePageUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = "https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases";
        }

        ProcessStartUrl(url);
    }

    private static void ProcessStartUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void RefreshUpdatesSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "updates")
        {
            UpdateSelectedSection();
        }
    }

    private static string FormatUpdateChannel(UpdateChannel channel)
    {
        return channel == UpdateChannel.Beta ? "Beta" : "Stable";
    }

    private static string FormatDateTime(DateTimeOffset? value)
    {
        return value is null
            ? "Not checked"
            : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatUpdateAssetBytes(long value)
    {
        if (value >= 1024L * 1024L * 1024L)
        {
            return $"{value / (1024d * 1024d * 1024d):0.0} GB";
        }

        if (value >= 1024L * 1024L)
        {
            return $"{value / (1024d * 1024d):0.0} MB";
        }

        if (value >= 1024L)
        {
            return $"{value / 1024d:0.0} KB";
        }

        return $"{value.ToString(CultureInfo.InvariantCulture)} B";
    }

    private static int? CompareUpdateVersions(string latestVersion, string currentVersion)
    {
        var latestParts = ParseUpdateVersionParts(latestVersion);
        var currentParts = ParseUpdateVersionParts(currentVersion);
        if (latestParts.Count == 0 || currentParts.Count == 0)
        {
            return null;
        }

        var length = Math.Max(latestParts.Count, currentParts.Count);
        for (var index = 0; index < length; index++)
        {
            var latest = index < latestParts.Count ? latestParts[index] : 0;
            var current = index < currentParts.Count ? currentParts[index] : 0;
            if (latest > current)
            {
                return 1;
            }

            if (latest < current)
            {
                return -1;
            }
        }

        return 0;
    }

    private static IReadOnlyList<int> ParseUpdateVersionParts(string version)
    {
        return version
            .Split([".", "-", "_", "+", " ", "v", "V"], StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (int?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
    }
}
