using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using CPAD.Core.Models;

using WpfOrientation = System.Windows.Controls.Orientation;

namespace CPAD;

public partial class MainWindow
{
    private static readonly HashSet<string> RepairModeAllowedSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "overview",
            "logs",
            "settings",
            "about",
            "dependency-repair"
        };

    private DependencyCheckResult _dependencyStatus = new()
    {
        IsAvailable = true,
        Summary = "Dependency health has not been checked yet.",
        Detail = "Open Dependency Repair or let startup health checks complete."
    };

    private bool _dependencyRepairLoading;
    private bool _dependencyRepairRefreshPending;
    private bool _dependencyRepairNavigatePending;
    private bool _dependencyRepairing;
    private bool _dependencyRepairShowDetails;
    private string? _dependencyRepairStatusMessage;
    private string? _dependencyRepairError;
    private DateTimeOffset? _dependencyLastCheckedAt;

    private async Task RefreshDependencyStatusAsync(bool force, bool navigateToRepairPage = false)
    {
        if (_dependencyRepairLoading)
        {
            if (force)
            {
                _dependencyRepairRefreshPending = true;
                _dependencyRepairNavigatePending |= navigateToRepairPage;
            }

            return;
        }

        if (!force && _dependencyLastCheckedAt is not null)
        {
            return;
        }

        _dependencyRepairLoading = true;
        _dependencyRepairError = null;
        _dependencyRepairStatusMessage = "Running desktop dependency health checks...";
        RefreshDependencyRepairSection();

        try
        {
            _dependencyStatus = await _dependencyHealthService.EvaluateAsync(_backendProcessManager.CurrentStatus);
            _dependencyLastCheckedAt = DateTimeOffset.Now;
            _dependencyRepairStatusMessage = _dependencyStatus.RequiresRepairMode
                ? $"Repair mode is active. {_dependencyStatus.Issues.Count.ToString(CultureInfo.InvariantCulture)} blocking checks need attention."
                : $"Dependency health check passed at {_dependencyLastCheckedAt.Value.ToLocalTime():HH:mm:ss}.";
        }
        catch (Exception exception)
        {
            _dependencyLastCheckedAt = DateTimeOffset.Now;
            _dependencyRepairError = exception.Message;
            _dependencyRepairStatusMessage = "Dependency health check failed.";
            _dependencyStatus = new DependencyCheckResult
            {
                IsAvailable = false,
                RequiresRepairMode = true,
                Summary = "Dependency health evaluation failed.",
                Detail = exception.Message,
                Issues =
                [
                    new DependencyCheckIssue
                    {
                        Code = "health-evaluation",
                        Title = "Dependency health evaluation failed.",
                        Detail = exception.Message,
                        CanRepairNow = false
                    }
                ]
            };
        }
        finally
        {
            _dependencyRepairLoading = false;
            ApplyRepairModeState(navigateToRepairPage);

            if (_dependencyRepairRefreshPending)
            {
                var pendingNavigate = _dependencyRepairNavigatePending;
                _dependencyRepairRefreshPending = false;
                _dependencyRepairNavigatePending = false;
                await RefreshDependencyStatusAsync(force: true, navigateToRepairPage: pendingNavigate);
            }
        }
    }

    private void ApplyRepairModeState(bool navigateToRepairPage)
    {
        var repairMode = _dependencyStatus.RequiresRepairMode;
        foreach (var section in _sections)
        {
            var enabled = !repairMode || IsSectionAllowedDuringRepairMode(section.Key);
            section.IsEnabled = enabled;
            section.WarningBadge = repairMode && !enabled ? "!" : string.Empty;
        }

        NavigationList.Items.Refresh();
        UpdateRepairModeBanner();
        UpdateFooter();

        var selected = NavigationList.SelectedItem as ShellSection;
        if (repairMode && (navigateToRepairPage || (selected is not null && !selected.IsEnabled)))
        {
            OpenDependencyRepairPage();
            return;
        }

        UpdateSelectedSection();
    }

    private void UpdateRepairModeBanner()
    {
        if (!IsRepairModeActive())
        {
            RepairModeBannerHost.Content = null;
            RepairModeBannerHost.Visibility = Visibility.Collapsed;
            return;
        }

        RepairModeBannerHost.Content = CreateRepairModeBanner();
        RepairModeBannerHost.Visibility = Visibility.Visible;
    }

    private UIElement CreateRepairModeBanner()
    {
        var issueCount = _dependencyStatus.Issues.Count;
        var root = new DockPanel();

        var actions = new WrapPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(CreateActionButton("Open Dependency Repair", () =>
        {
            OpenDependencyRepairPage();
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Re-check", () => RefreshDependencyStatusAsync(force: true)));
        DockPanel.SetDock(actions, Dock.Right);
        root.Children.Add(actions);

        var text = new StackPanel { Orientation = WpfOrientation.Vertical };
        text.Children.Add(CreateText(
            "Dependency Repair Mode",
            16,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        text.Children.Add(CreateText(
            $"{_dependencyStatus.Summary} Blocked checks: {issueCount.ToString(CultureInfo.InvariantCulture)}.",
            12,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        root.Children.Add(text);

        var banner = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Child = root
        };
        banner.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        banner.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        return banner;
    }

    private UIElement BuildDependencyRepairContent()
    {
        if (_dependencyRepairLoading && _dependencyLastCheckedAt is null)
        {
            return CreateStatePanel(
                "Checking desktop dependency health...",
                "Inspecting runtime readiness, backend assets, DPAPI, directory access, update metadata, ports, and resource packs.");
        }

        var repairableCount = _dependencyStatus.Issues.Count(issue => issue.CanRepairNow);
        var blockedRoutes = _sections
            .Where(section => !section.IsEnabled)
            .Select(section => section.Title)
            .ToArray();
        var allowedRoutes = _sections
            .Where(section => section.IsEnabled)
            .Select(section => section.Title)
            .ToArray();

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateDependencyRepairHero(repairableCount));

        if (!string.IsNullOrWhiteSpace(_dependencyRepairStatusMessage))
        {
            root.Children.Add(CreateHintCard("Repair action", _dependencyRepairStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_dependencyRepairError))
        {
            root.Children.Add(CreateHintCard("Repair issue", _dependencyRepairError));
        }

        root.Children.Add(CreateSectionHeader("Repair Summary"));
        root.Children.Add(CreateDependencyRepairSummaryCard(repairableCount));

        root.Children.Add(CreateSectionHeader("Safe Routes"));
        root.Children.Add(CreateDependencyRepairRoutesCard(allowedRoutes, blockedRoutes));

        root.Children.Add(CreateSectionHeader("Repair Actions"));
        root.Children.Add(CreateDependencyRepairActionsCard(repairableCount));

        root.Children.Add(CreateSectionHeader("Issue Details"));
        root.Children.Add(CreateDependencyRepairIssuesCard());

        return root;
    }

    private UIElement CreateDependencyRepairHero(int repairableCount)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Repair mode keeps the shell on safe desktop routes",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            $"Backend state: {_backendProcessManager.CurrentStatus.State} | Last check: {FormatDependencyDateTime(_dependencyLastCheckedAt)}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateText(
            repairableCount > 0
                ? "Automatic repair is available for bundled backend/runtime asset issues. Other failures remain visible so diagnostics and later installer phases can handle them safely."
                : "No current issue can be repaired automatically in this phase. Use the detail list and diagnostics export to continue investigation safely.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateDependencyRepairSummaryCard(int repairableCount)
    {
        return CreateMetricGrid(
            CreateMetricCard("Repair Mode", IsRepairModeActive() ? "Active" : "Inactive", _dependencyStatus.Summary),
            CreateMetricCard("Blocking Issues", _dependencyStatus.Issues.Count.ToString(CultureInfo.InvariantCulture), "Checks currently holding the shell in repair mode."),
            CreateMetricCard("Repairable Now", repairableCount.ToString(CultureInfo.InvariantCulture), repairableCount > 0 ? "Bundled backend asset repair is available now." : "No automatic repair target is available."),
            CreateMetricCard("Backend", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message),
            CreateMetricCard("Diagnostics", Directory.Exists(_pathService.Directories.DiagnosticsDirectory) ? "Ready" : "Will be created", _pathService.Directories.DiagnosticsDirectory),
            CreateMetricCard("Last Checked", FormatDependencyDateTime(_dependencyLastCheckedAt), _dependencyRepairLoading ? "Check in progress." : "Latest completed dependency probe."));
    }

    private UIElement CreateDependencyRepairRoutesCard(
        IReadOnlyList<string> allowedRoutes,
        IReadOnlyList<string> blockedRoutes)
    {
        var grid = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 8)
        };
        AddKeyValue(grid, "Allowed routes", allowedRoutes.Count == 0 ? "-" : string.Join(", ", allowedRoutes));
        AddKeyValue(grid, "Blocked routes", blockedRoutes.Count == 0 ? "None" : string.Join(", ", blockedRoutes));
        AddKeyValue(grid, "Data root", _pathService.Directories.RootDirectory);
        AddKeyValue(grid, "Backend directory", _pathService.Directories.BackendDirectory);
        AddKeyValue(grid, "Settings file", _pathService.Directories.SettingsFilePath);
        AddKeyValue(grid, "Backend config", _pathService.Directories.BackendConfigFilePath);
        return CreateCard(grid, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateDependencyRepairActionsCard(int repairableCount)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "The required repair-mode actions remain available even when the rest of the shell is locked.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var actions = new WrapPanel();

        var repairButton = CreateActionButton(_dependencyRepairing ? "Repairing..." : "Repair Now", RepairDependencyIssuesAsync);
        repairButton.IsEnabled = !_dependencyRepairLoading && !_dependencyRepairing && repairableCount > 0;
        actions.Children.Add(repairButton);

        actions.Children.Add(CreateActionButton(
            _dependencyRepairShowDetails ? "Hide Details" : "View Details",
            ToggleDependencyRepairDetailsAsync));

        var recheckButton = CreateActionButton(_dependencyRepairLoading ? "Checking..." : "Re-check", () => RefreshDependencyStatusAsync(force: true));
        recheckButton.IsEnabled = !_dependencyRepairLoading && !_dependencyRepairing;
        actions.Children.Add(recheckButton);

        actions.Children.Add(CreateActionButton("Export Diagnostics", ExportDependencyDiagnosticsAsync));
        panel.Children.Add(actions);

        if (repairableCount == 0)
        {
            panel.Children.Add(CreateHintCard(
                "Automatic repair scope",
                "This phase only repairs bundled backend/runtime asset issues. Runtime installation, directory permissions, DPAPI availability, and update-chain gaps remain manual or later-phase fixes."));
        }

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateDependencyRepairIssuesCard()
    {
        if (_dependencyStatus.Issues.Count == 0)
        {
            return CreateStatePanel(
                "No blocking dependency issues are active.",
                "The shell is fully available. You can keep this page for manual re-checks or return to the other routes.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        var visibleIssues = _dependencyRepairShowDetails
            ? _dependencyStatus.Issues
            : _dependencyStatus.Issues.Take(3).ToArray();

        foreach (var issue in visibleIssues)
        {
            var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
            panel.Children.Add(CreateText(issue.Title, 15, FontWeights.SemiBold, "PrimaryTextBrush"));
            panel.Children.Add(CreateText(
                $"Code: {issue.Code} | Repair now: {(issue.CanRepairNow ? "Yes" : "No")}",
                12,
                FontWeights.SemiBold,
                "SecondaryTextBrush",
                new Thickness(0, 5, 0, 0)));
            panel.Children.Add(CreateText(
                issue.Detail,
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 5, 0, 0)));
            root.Children.Add(CreateCard(panel, new Thickness(0, 0, 0, 10)));
        }

        if (!_dependencyRepairShowDetails && _dependencyStatus.Issues.Count > visibleIssues.Count)
        {
            root.Children.Add(CreateHintCard(
                "Additional issues",
                $"Showing {visibleIssues.Count.ToString(CultureInfo.InvariantCulture)} of {_dependencyStatus.Issues.Count.ToString(CultureInfo.InvariantCulture)} issues. Use View Details to expand the full list."));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private async Task RepairDependencyIssuesAsync()
    {
        if (_dependencyRepairing)
        {
            return;
        }

        _dependencyRepairing = true;
        _dependencyRepairError = null;
        _dependencyRepairStatusMessage = "Repairing bundled backend/runtime assets and re-checking dependency health...";
        RefreshDependencyRepairSection();

        try
        {
            if (_dependencyStatus.Issues.All(issue => !issue.CanRepairNow))
            {
                _dependencyRepairStatusMessage = "No current dependency issue can be repaired automatically in this phase.";
                return;
            }

            await _backendAssetService.RepairAssetsAsync();
            await RefreshDependencyStatusAsync(force: true);

            if (IsRepairModeActive())
            {
                _dependencyRepairStatusMessage = "Automatic repair completed, but some blocking checks still require manual or later-phase fixes.";
            }
            else
            {
                _dependencyRepairStatusMessage = "Dependency repair completed. Full shell navigation is available again.";
            }
        }
        catch (Exception exception)
        {
            _dependencyRepairError = exception.Message;
            _dependencyRepairStatusMessage = "Automatic dependency repair failed.";
        }
        finally
        {
            _dependencyRepairing = false;
            RefreshDependencyRepairSection();
        }
    }

    private Task ToggleDependencyRepairDetailsAsync()
    {
        _dependencyRepairShowDetails = !_dependencyRepairShowDetails;
        RefreshDependencyRepairSection();
        return Task.CompletedTask;
    }

    private Task ExportDependencyDiagnosticsAsync()
    {
        try
        {
            var packagePath = _diagnosticsService.ExportPackage(
                _backendProcessManager.CurrentStatus,
                BuildCodexDiagnosticsSnapshot(),
                GetDependencyStatus());
            _dependencyRepairStatusMessage = $"Diagnostic package exported: {packagePath}";
        }
        catch (Exception exception)
        {
            _dependencyRepairError = exception.Message;
            _dependencyRepairStatusMessage = "Diagnostic package export failed.";
        }

        RefreshDependencyRepairSection();
        return Task.CompletedTask;
    }

    private void RefreshDependencyRepairSection()
    {
        var selectedKey = (NavigationList.SelectedItem as ShellSection)?.Key;
        if (selectedKey is "dependency-repair" or "overview")
        {
            UpdateSelectedSection();
        }

        UpdateRepairModeBanner();
    }

    private void OpenDependencyRepairPage()
    {
        SelectSection("dependency-repair");
        RestoreFromTray();
    }

    private void SelectSection(string key)
    {
        var index = _sections.FindIndex(section => string.Equals(section.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        if (NavigationList.SelectedIndex != index)
        {
            NavigationList.SelectedIndex = index;
            return;
        }

        UpdateSelectedSection();
    }

    private bool IsRepairModeActive()
    {
        return _dependencyStatus.RequiresRepairMode;
    }

    private static bool IsSectionAllowedDuringRepairMode(string key)
    {
        return RepairModeAllowedSections.Contains(key);
    }

    private static string FormatDependencyDateTime(DateTimeOffset? value)
    {
        return value is null
            ? "Not checked"
            : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
