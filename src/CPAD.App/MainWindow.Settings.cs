using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using CPAD.Core.Enums;
using CPAD.Core.Models;

using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace CPAD;

public partial class MainWindow
{
    private bool _settingsLoading;
    private bool _settingsSaving;
    private string? _settingsError;
    private string? _settingsStatusMessage;
    private DateTimeOffset? _settingsLastLoadedAt;
    private AppThemeMode _settingsThemeModeDraft = AppThemeMode.System;
    private bool _settingsStartWithWindowsDraft;
    private bool _settingsMinimizeToTrayOnCloseDraft = true;
    private bool _settingsEnableTrayIconDraft = true;
    private bool _settingsCheckForUpdatesOnStartupDraft = true;
    private bool _settingsUseBetaChannelDraft;
    private AppLogLevel _settingsMinimumLogLevelDraft = AppLogLevel.Information;
    private bool _settingsEnableDebugToolsDraft;
    private bool _settingsStartupRegistered;
    private string? _settingsRootWriteError;
    private string? _settingsConfigWriteError;
    private string? _settingsLogsWriteError;
    private string? _settingsDiagnosticsWriteError;

    private UIElement BuildSettingsContent()
    {
        if (_settingsLoading && _settingsLastLoadedAt is null)
        {
            return CreateStatePanel(
                "Loading desktop settings...",
                "Reading desktop.json, startup registration state, theme preference, directory access, and shell behavior.");
        }

        if (!string.IsNullOrWhiteSpace(_settingsError) && _settingsLastLoadedAt is null)
        {
            return CreateStatePanel("Settings are unavailable.", _settingsError);
        }

        if (_settingsLastLoadedAt is null)
        {
            return CreateStatePanel(
                "No settings snapshot is loaded yet.",
                "Open this route again or reload after desktop configuration is available.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateSettingsHero());

        if (!string.IsNullOrWhiteSpace(_settingsStatusMessage))
        {
            root.Children.Add(CreateHintCard("Settings action", _settingsStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_settingsError))
        {
            root.Children.Add(CreateHintCard("Settings issue", _settingsError));
        }

        root.Children.Add(CreateSectionHeader("Appearance & Shell"));
        root.Children.Add(CreateSettingsAppearanceCard());

        root.Children.Add(CreateSectionHeader("Startup & Tray"));
        root.Children.Add(CreateSettingsStartupCard());

        root.Children.Add(CreateSectionHeader("Directories"));
        root.Children.Add(CreateSettingsDirectoriesCard());

        root.Children.Add(CreateSectionHeader("Update Preferences"));
        root.Children.Add(CreateSettingsUpdatesCard());

        root.Children.Add(CreateSectionHeader("Privacy & Diagnostics"));
        root.Children.Add(CreateSettingsPrivacyCard());

        root.Children.Add(CreateSectionHeader("Persisted Snapshot"));
        root.Children.Add(CreateSettingsSnapshotCard());

        return root;
    }

    private async Task RefreshSettingsAsync(bool force)
    {
        if (_settingsLoading)
        {
            return;
        }

        if (!force && _settingsLastLoadedAt is not null)
        {
            return;
        }

        _settingsLoading = true;
        _settingsError = null;
        _settingsStatusMessage = "Reloading desktop settings, startup registration, and writable directories...";
        RefreshSettingsSection();

        try
        {
            var settings = await _configurationService.LoadAsync();
            var startupRegistered = _startupRegistrationService.IsEnabled();

            _settings = settings;
            _settingsStartupRegistered = startupRegistered;
            LoadSettingsDrafts(settings, startupRegistered);
            RefreshSettingsDirectoryDiagnostics();

            await ApplyThemeAsync(_settings.ThemeMode, persist: false);
            InitializeTrayIcon();
            UpdateFooter();

            _settingsLastLoadedAt = DateTimeOffset.Now;
            _settingsStatusMessage = $"Settings reloaded at {_settingsLastLoadedAt.Value.ToLocalTime():HH:mm:ss}.";
        }
        catch (Exception exception)
        {
            _settingsError = exception.Message;
            _settingsStatusMessage = "Settings reload failed.";
        }
        finally
        {
            _settingsLoading = false;
            RefreshSettingsSection();
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (_settingsSaving)
        {
            return;
        }

        _settingsSaving = true;
        _settingsError = null;
        _settingsStatusMessage = "Saving desktop settings and applying shell behavior...";
        RefreshSettingsSection();

        try
        {
            var savedSettings = new AppSettings
            {
                BackendPort = _settings.BackendPort,
                ManagementKey = _settings.ManagementKey,
                ManagementKeyReference = _settings.ManagementKeyReference,
                PreferredCodexSource = _settings.PreferredCodexSource,
                StartWithWindows = _settingsStartWithWindowsDraft,
                MinimizeToTrayOnClose = _settingsMinimizeToTrayOnCloseDraft,
                EnableTrayIcon = _settingsEnableTrayIconDraft,
                CheckForUpdatesOnStartup = _settingsCheckForUpdatesOnStartupDraft,
                UseBetaChannel = _settingsUseBetaChannelDraft,
                ThemeMode = _settingsThemeModeDraft,
                MinimumLogLevel = _settingsMinimumLogLevelDraft,
                EnableDebugTools = _settingsEnableDebugToolsDraft,
                LastRepositoryPath = _settings.LastRepositoryPath
            };

            _startupRegistrationService.SetEnabled(savedSettings.StartWithWindows);

            _settings = savedSettings;
            _updatesChannelDraft = _settings.UseBetaChannel ? UpdateChannel.Beta : UpdateChannel.Stable;
            _managementKeyDraft = _settings.ManagementKey;

            await _configurationService.SaveAsync(_settings);
            await ApplyThemeAsync(_settings.ThemeMode, persist: false);

            _settingsStartupRegistered = _startupRegistrationService.IsEnabled();
            LoadSettingsDrafts(_settings, _settingsStartupRegistered);
            RefreshSettingsDirectoryDiagnostics();
            InitializeTrayIcon();
            UpdateFooter();
            UpdateSelectedSection();

            _settingsLastLoadedAt = DateTimeOffset.Now;
            _settingsStatusMessage = "Settings were saved to desktop.json and applied to the desktop shell.";
        }
        catch (Exception exception)
        {
            _settingsError = exception.Message;
            _settingsStatusMessage = "Settings save failed.";
        }
        finally
        {
            _settingsSaving = false;
            RefreshSettingsSection();
        }
    }

    private void LoadSettingsDrafts(AppSettings settings, bool? startupRegistered = null)
    {
        _settingsThemeModeDraft = settings.ThemeMode;
        _settingsStartWithWindowsDraft = startupRegistered ?? settings.StartWithWindows;
        _settingsMinimizeToTrayOnCloseDraft = settings.MinimizeToTrayOnClose;
        _settingsEnableTrayIconDraft = settings.EnableTrayIcon;
        _settingsCheckForUpdatesOnStartupDraft = settings.CheckForUpdatesOnStartup;
        _settingsUseBetaChannelDraft = settings.UseBetaChannel;
        _settingsMinimumLogLevelDraft = settings.MinimumLogLevel;
        _settingsEnableDebugToolsDraft = settings.EnableDebugTools;
    }

    private void RefreshSettingsDirectoryDiagnostics()
    {
        _settingsRootWriteError = _directoryAccessService.GetWriteAccessError(_pathService.Directories.RootDirectory);
        _settingsConfigWriteError = _directoryAccessService.GetWriteAccessError(_pathService.Directories.ConfigDirectory);
        _settingsLogsWriteError = _directoryAccessService.GetWriteAccessError(_pathService.Directories.LogsDirectory);
        _settingsDiagnosticsWriteError = _directoryAccessService.GetWriteAccessError(_pathService.Directories.DiagnosticsDirectory);
    }

    private UIElement CreateSettingsHero()
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Desktop shell preferences, startup behavior, and privacy controls",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            $"Settings file: {_pathService.Directories.SettingsFilePath}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateText(
            $"Data mode: {FormatAppDataMode(_pathService.Directories.DataMode)} | Startup registration: {(_settingsStartupRegistered ? "Enabled" : "Disabled")}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        panel.Children.Add(CreateText(
            "This page edits native desktop settings only. Backend secrets remain in DPAPI/Credential storage, startup registration is handled through the current-user Run key, and diagnostics remain redacted.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSettingsAppearanceCard()
    {
        var themeBox = new WpfComboBox
        {
            ItemsSource = Enum.GetValues<AppThemeMode>(),
            SelectedItem = _settingsThemeModeDraft,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5)
        };
        themeBox.SelectionChanged += (_, _) =>
        {
            if (themeBox.SelectedItem is AppThemeMode selected)
            {
                _settingsThemeModeDraft = selected;
            }
        };

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Selected Theme", GetThemeLabel(_settingsThemeModeDraft), "Saved to desktop.json"),
            CreateMetricCard("Active Theme", GetThemeLabel(ResolveEffectiveTheme(_settings.ThemeMode)), "Currently applied shell palette"),
            CreateMetricCard("Shell Version", _buildInfo.ApplicationVersion, _buildInfo.InformationalVersion)));

        root.Children.Add(CreateConfigFieldShell(
            "Theme Mode",
            "Switch between System, Light, and Dark. Save applies the choice to the native WPF shell immediately.",
            themeBox));

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSettingsStartupCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Start With Windows", _settingsStartWithWindowsDraft ? "Enabled" : "Disabled", _settingsStartupRegistered ? "Current-user Run key is present." : "Current-user Run key is absent."),
            CreateMetricCard("Tray Icon", _settingsEnableTrayIconDraft ? "Enabled" : "Disabled", _settingsEnableTrayIconDraft ? "Tray menu stays available." : "The window will not keep a tray icon."),
            CreateMetricCard("Close Button", _settingsMinimizeToTrayOnCloseDraft && _settingsEnableTrayIconDraft ? "Minimize to tray" : "Exit window", _settingsMinimizeToTrayOnCloseDraft ? "Depends on tray icon availability." : "The main window closes directly.")));

        root.Children.Add(CreateConfigFieldGrid(
            CreateSettingsToggleField("Start With Windows", "Registers or removes the current-user startup entry for CPAD.exe.", _settingsStartWithWindowsDraft, value => _settingsStartWithWindowsDraft = value),
            CreateSettingsToggleField("Enable Tray Icon", "Controls whether the native shell creates the tray icon and menu.", _settingsEnableTrayIconDraft, value => _settingsEnableTrayIconDraft = value),
            CreateSettingsToggleField("Minimize To Tray On Close", "When enabled and the tray icon is available, clicking the close button hides the window instead of exiting.", _settingsMinimizeToTrayOnCloseDraft, value => _settingsMinimizeToTrayOnCloseDraft = value)));

        if (!_settingsEnableTrayIconDraft && _settingsMinimizeToTrayOnCloseDraft)
        {
            root.Children.Add(CreateHintCard(
                "Tray behavior note",
                "Minimize-to-tray is selected, but the tray icon is disabled. In this combination the close button exits the window because there is no tray destination."));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSettingsDirectoriesCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Directory checks run through the desktop-side directory access service so the page can report write access before packaging or diagnostics features depend on these paths.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var details = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AddKeyValue(details, "Root", _pathService.Directories.RootDirectory);
        AddKeyValue(details, "Config", _pathService.Directories.ConfigDirectory);
        AddKeyValue(details, "Logs", _pathService.Directories.LogsDirectory);
        AddKeyValue(details, "Backend", _pathService.Directories.BackendDirectory);
        AddKeyValue(details, "Cache", _pathService.Directories.CacheDirectory);
        AddKeyValue(details, "Diagnostics", _pathService.Directories.DiagnosticsDirectory);
        AddKeyValue(details, "Runtime", _pathService.Directories.RuntimeDirectory);
        AddKeyValue(details, "Data Mode", FormatAppDataMode(_pathService.Directories.DataMode));
        root.Children.Add(details);

        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Root Access", string.IsNullOrWhiteSpace(_settingsRootWriteError) ? "Writable" : "Issue", _settingsRootWriteError ?? "Desktop root directory is writable."),
            CreateMetricCard("Config Access", string.IsNullOrWhiteSpace(_settingsConfigWriteError) ? "Writable" : "Issue", _settingsConfigWriteError ?? "Config directory is writable."),
            CreateMetricCard("Logs Access", string.IsNullOrWhiteSpace(_settingsLogsWriteError) ? "Writable" : "Issue", _settingsLogsWriteError ?? "Logs directory is writable."),
            CreateMetricCard("Diagnostics Access", string.IsNullOrWhiteSpace(_settingsDiagnosticsWriteError) ? "Writable" : "Issue", _settingsDiagnosticsWriteError ?? "Diagnostics directory is writable."),
            CreateMetricCard("Settings File", File.Exists(_pathService.Directories.SettingsFilePath) ? "Present" : "Will be created", _pathService.Directories.SettingsFilePath),
            CreateMetricCard("Backend Config", File.Exists(_pathService.Directories.BackendConfigFilePath) ? "Present" : "Managed later", _pathService.Directories.BackendConfigFilePath)));

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Open Root Folder", () =>
        {
            _directoryAccessService.OpenDirectory(_pathService.Directories.RootDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Config Folder", () =>
        {
            _directoryAccessService.OpenDirectory(_pathService.Directories.ConfigDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Logs Folder", () =>
        {
            _directoryAccessService.OpenDirectory(_pathService.Directories.LogsDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Diagnostics Folder", () =>
        {
            _directoryAccessService.OpenDirectory(_pathService.Directories.DiagnosticsDirectory);
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSettingsUpdatesCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Check on Startup", _settingsCheckForUpdatesOnStartupDraft ? "Enabled" : "Disabled", "Background stable checks run after shell load."),
            CreateMetricCard("Preferred Channel", _settingsUseBetaChannelDraft ? "Beta" : "Stable", _settingsUseBetaChannelDraft ? "Beta remains reserved in this phase." : "Stable GitHub Releases are active."),
            CreateMetricCard("Latest Result", _updatesStableResult?.Status ?? "Not checked", _updatesStableResult?.Detail ?? "Open Updates & Version to inspect the latest result.")));

        root.Children.Add(CreateConfigFieldGrid(
            CreateSettingsToggleField("Check For Updates On Startup", "When enabled, the shell checks the stable desktop release channel after launch.", _settingsCheckForUpdatesOnStartupDraft, value => _settingsCheckForUpdatesOnStartupDraft = value),
            CreateSettingsToggleField("Use Beta Channel", "Persists the reserved beta preference now so later packaging phases can enable the channel without changing the settings model.", _settingsUseBetaChannelDraft, value => _settingsUseBetaChannelDraft = value)));

        if (_settingsUseBetaChannelDraft)
        {
            root.Children.Add(CreateHintCard(
                "Beta channel note",
                "Beta is intentionally reserved in this phase. Saving the preference updates desktop.json, but stable GitHub Releases remain the only active update source."));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSettingsPrivacyCard()
    {
        var logLevelBox = new WpfComboBox
        {
            ItemsSource = Enum.GetValues<AppLogLevel>(),
            SelectedItem = _settingsMinimumLogLevelDraft,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5)
        };
        logLevelBox.SelectionChanged += (_, _) =>
        {
            if (logLevelBox.SelectedItem is AppLogLevel selected)
            {
                _settingsMinimumLogLevelDraft = selected;
            }
        };

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Minimum Log Level", _settingsMinimumLogLevelDraft.ToString(), "Higher levels reduce retained local detail."),
            CreateMetricCard("Debug Tools", _settingsEnableDebugToolsDraft ? "Enabled" : "Disabled", _settingsEnableDebugToolsDraft ? "Developer-oriented tools stay available." : "Only standard desktop routes are exposed."),
            CreateMetricCard("Diagnostics Export", "Redacted", "Sensitive values remain redacted before export.")));

        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigFieldShell(
                "Minimum Log Level",
                "This desktop preference controls the intended verbosity policy for local diagnostics and is persisted in desktop.json.",
                logLevelBox),
            CreateSettingsToggleField("Enable Debug Tools", "Keeps desktop-side debug affordances available without storing secrets in plaintext.", _settingsEnableDebugToolsDraft, value => _settingsEnableDebugToolsDraft = value)));

        root.Children.Add(CreateHintCard(
            "Privacy baseline",
            "Management secrets stay out of desktop.json, sensitive diagnostics are redacted, and DPAPI/Credential-backed storage remains the source of truth for protected values."));

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSettingsSnapshotCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "This snapshot echoes the values that will be persisted on the next save, plus the actual startup registration state read from the machine.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));
        root.Children.Add(CreateReadOnlyLogBox(BuildSettingsSnapshotPreview(), minHeight: 220));
        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateSettingsToggleField(string label, string detail, bool value, Action<bool> setter)
    {
        var checkbox = new WpfCheckBox
        {
            IsChecked = value,
            Margin = new Thickness(0, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        checkbox.SetResourceReference(WpfControl.ForegroundProperty, "PrimaryTextBrush");
        checkbox.Checked += (_, _) => setter(true);
        checkbox.Unchecked += (_, _) => setter(false);
        return CreateConfigFieldShell(label, detail, checkbox);
    }

    private string BuildSettingsSnapshotPreview()
    {
        var lines = new[]
        {
            $"themeMode: {_settingsThemeModeDraft}",
            $"startWithWindows: {_settingsStartWithWindowsDraft}",
            $"startupRegistered: {_settingsStartupRegistered}",
            $"enableTrayIcon: {_settingsEnableTrayIconDraft}",
            $"minimizeToTrayOnClose: {_settingsMinimizeToTrayOnCloseDraft}",
            $"checkForUpdatesOnStartup: {_settingsCheckForUpdatesOnStartupDraft}",
            $"useBetaChannel: {_settingsUseBetaChannelDraft}",
            $"minimumLogLevel: {_settingsMinimumLogLevelDraft}",
            $"enableDebugTools: {_settingsEnableDebugToolsDraft}",
            $"preferredCodexSource: {_settings.PreferredCodexSource}",
            $"lastRepositoryPath: {_settings.LastRepositoryPath ?? "(empty)"}",
            $"dataMode: {FormatAppDataMode(_pathService.Directories.DataMode)}",
            $"settingsFile: {_pathService.Directories.SettingsFilePath}",
            $"rootDirectory: {_pathService.Directories.RootDirectory}",
            $"logsDirectory: {_pathService.Directories.LogsDirectory}",
            $"diagnosticsDirectory: {_pathService.Directories.DiagnosticsDirectory}",
            $"lastLoadedAt: {FormatDateTime(_settingsLastLoadedAt)}"
        };
        return string.Join(Environment.NewLine, lines);
    }

    private void RefreshSettingsSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "settings")
        {
            UpdateSelectedSection();
        }
    }

    private static string FormatAppDataMode(AppDataMode mode)
    {
        return mode switch
        {
            AppDataMode.Portable => "Portable",
            AppDataMode.Development => "Development",
            _ => "Installed"
        };
    }
}
