using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using CPAD.Core.Enums;
using CPAD.Core.Models;

using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace CPAD;

public partial class MainWindow
{
    private CodexStatusSnapshot? _toolsCodexStatus;
    private bool _toolsLoading;
    private bool _toolsApplying;
    private string? _toolsError;
    private string? _toolsStatusMessage;
    private DateTimeOffset? _toolsLastLoadedAt;
    private CodexSourceKind _toolsSelectedSource = CodexSourceKind.Official;
    private string _toolsRepositoryPathDraft = string.Empty;
    private string _toolsCommandPreview = string.Empty;

    private UIElement BuildToolsContent()
    {
        if (_toolsLoading && _toolsCodexStatus is null)
        {
            return CreateStatePanel(
                "Loading source switching and tool integration...",
                "Inspecting CODEX_HOME, reading the current Codex profile, and discovering desktop tool entry points.");
        }

        if (!string.IsNullOrWhiteSpace(_toolsError) && _toolsCodexStatus is null)
        {
            return CreateStatePanel("Sources and tools are unavailable.", _toolsError);
        }

        if (_toolsCodexStatus is null)
        {
            return CreateStatePanel(
                "No tool state loaded yet.",
                "Open this route again or refresh after the desktop configuration finishes loading.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateToolsHero());

        if (!string.IsNullOrWhiteSpace(_toolsStatusMessage))
        {
            root.Children.Add(CreateHintCard("Source action", _toolsStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_toolsError))
        {
            root.Children.Add(CreateHintCard("Tool issue", _toolsError));
        }

        root.Children.Add(CreateSectionHeader("Source Switching"));
        root.Children.Add(CreateToolsSourceSwitchCard());

        root.Children.Add(CreateSectionHeader("Codex Desktop Integration"));
        root.Children.Add(CreateToolsCodexIntegrationCard());

        root.Children.Add(CreateSectionHeader("Desktop Tool Entries"));
        root.Children.Add(CreateToolsDesktopEntriesCard());

        return root;
    }

    private async Task RefreshToolsAsync(bool force)
    {
        if (_toolsLoading)
        {
            return;
        }

        if (!force && _toolsLastLoadedAt is not null)
        {
            return;
        }

        _toolsLoading = true;
        _toolsError = null;
        _toolsStatusMessage = "Refreshing source switch state and Codex integration...";
        RefreshToolsSection();

        try
        {
            var repositoryPath = NormalizeRepositoryPath(_toolsRepositoryPathDraft);
            var executableTask = _codexLocator.LocateAsync();
            var versionTask = _codexVersionReader.ReadAsync();
            var authTask = _codexAuthStateReader.ReadAsync();

            await executableTask;
            await versionTask;
            await authTask;

            _toolsCodexStatus = await _codexConfigService.InspectAsync(
                repositoryPath,
                executableTask.Result,
                versionTask.Result,
                authTask.Result);
            _toolsCommandPreview = _codexLaunchService.BuildCommand(_toolsSelectedSource, repositoryPath);
            _toolsLastLoadedAt = DateTimeOffset.Now;
            _toolsStatusMessage = $"Source and tool state refreshed at {_toolsLastLoadedAt.Value.ToLocalTime():HH:mm:ss}.";
            _toolsError = _toolsCodexStatus.ErrorMessage;
        }
        catch (Exception exception)
        {
            _toolsError = exception.Message;
            _toolsStatusMessage = "Tool refresh failed.";
        }
        finally
        {
            _toolsLoading = false;
            RefreshToolsSection();
        }
    }

    private async Task ApplyToolsSourceAsync()
    {
        if (_toolsApplying)
        {
            return;
        }

        var repositoryPath = NormalizeRepositoryPath(_toolsRepositoryPathDraft);
        var validationErrors = new List<string>();
        if (!string.IsNullOrWhiteSpace(repositoryPath) && !Directory.Exists(repositoryPath))
        {
            validationErrors.Add("Repository path does not exist.");
        }

        if (validationErrors.Count > 0)
        {
            _toolsStatusMessage = string.Join(" | ", validationErrors);
            RefreshToolsSection();
            return;
        }

        _toolsApplying = true;
        _toolsError = null;
        _toolsStatusMessage = $"Applying {FormatCodexSource(_toolsSelectedSource)} source switch through CODEX_HOME...";
        RefreshToolsSection();

        try
        {
            var backendPort = _settings.BackendPort;
            if (_toolsSelectedSource == CodexSourceKind.Cpa)
            {
                var status = await _backendProcessManager.StartAsync();
                if (status.Runtime is null || status.State != BackendStateKind.Running)
                {
                    throw new InvalidOperationException(status.LastError ?? "Backend runtime is unavailable for CPA source switching.");
                }

                backendPort = status.Runtime.Port;
            }
            else if (_backendProcessManager.CurrentStatus.Runtime is { } runtime)
            {
                backendPort = runtime.Port;
            }

            await _codexConfigService.ApplyDesktopModeAsync(backendPort, _toolsSelectedSource);

            _settings.PreferredCodexSource = _toolsSelectedSource;
            _settings.LastRepositoryPath = string.IsNullOrWhiteSpace(repositoryPath) ? null : repositoryPath;
            await _configurationService.SaveAsync(_settings);
            UpdateFooter();

            var applyStatusMessage = await BuildToolsApplyStatusMessageAsync(backendPort);
            await RefreshToolsAsync(force: true);
            _toolsStatusMessage = applyStatusMessage;
        }
        catch (Exception exception)
        {
            _toolsError = exception.Message;
            _toolsStatusMessage = "Source switch failed.";
        }
        finally
        {
            _toolsApplying = false;
            RefreshToolsSection();
        }
    }

    private Task LaunchSelectedToolAsync()
    {
        var repositoryPath = NormalizeRepositoryPath(_toolsRepositoryPathDraft);
        if (!string.IsNullOrWhiteSpace(repositoryPath) && !Directory.Exists(repositoryPath))
        {
            _toolsStatusMessage = "Repository path does not exist, so Codex could not be launched.";
            RefreshToolsSection();
            return Task.CompletedTask;
        }

        var result = _codexLaunchService.LaunchInTerminal(_toolsSelectedSource, repositoryPath);
        _toolsCommandPreview = result.Command;
        _toolsStatusMessage = result.IsSuccess
            ? $"Launched Codex with the {FormatCodexSource(_toolsSelectedSource)} profile."
            : $"Codex launch failed: {result.ErrorMessage}";
        RefreshToolsSection();
        return Task.CompletedTask;
    }

    private UIElement CreateToolsHero()
    {
        var status = _toolsCodexStatus!;
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Official and CPA source switching for desktop Codex tooling",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            $"CODEX_HOME: {_codexConfigService.GetUserConfigDirectory()}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateText(
            $"Current effective source: {status.EffectiveSource}",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateToolsSourceSwitchCard()
    {
        var status = _toolsCodexStatus!;
        var sourceBox = new WpfComboBox
        {
            ItemsSource = Enum.GetValues<CodexSourceKind>(),
            SelectedItem = _toolsSelectedSource,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 5, 8, 5)
        };
        sourceBox.SelectionChanged += (_, _) =>
        {
            if (sourceBox.SelectedItem is CodexSourceKind selected)
            {
                _toolsSelectedSource = selected;
                _toolsCommandPreview = _codexLaunchService.BuildCommand(_toolsSelectedSource, NormalizeRepositoryPath(_toolsRepositoryPathDraft));
            }
        };

        var repositoryBox = CreateSystemEditorTextBox(_toolsRepositoryPathDraft, minHeight: 0, acceptsReturn: false, fontFamily: null);
        repositoryBox.TextChanged += (_, _) =>
        {
            _toolsRepositoryPathDraft = repositoryBox.Text;
            _toolsCommandPreview = _codexLaunchService.BuildCommand(_toolsSelectedSource, NormalizeRepositoryPath(_toolsRepositoryPathDraft));
        };

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Switching source rewrites the managed Codex profiles in CODEX_HOME and updates auth.json using the audited desktop flow: CPA writes the local backend profile and dummy auth, Official restores a backed-up or preset official auth file when available.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Preferred", FormatCodexSource(_settings.PreferredCodexSource), "Desktop setting persisted to desktop.json"),
            CreateMetricCard("Effective", status.EffectiveSource, "Read back from CODEX_HOME config.toml"),
            CreateMetricCard("Default Profile", status.DefaultProfile, "Resolved by CodexConfigService.InspectAsync"),
            CreateMetricCard("User Config", status.HasUserConfig ? "Present" : "Missing", _codexConfigService.GetUserConfigPath()),
            CreateMetricCard("Project Config", status.HasProjectConfig ? "Present" : "Missing", string.IsNullOrWhiteSpace(_toolsRepositoryPathDraft) ? "No repository path selected" : NormalizeRepositoryPath(_toolsRepositoryPathDraft) ?? _toolsRepositoryPathDraft),
            CreateMetricCard("Backend Route", _toolsSelectedSource == CodexSourceKind.Cpa ? "127.0.0.1" : "chatgpt.com", _toolsSelectedSource == CodexSourceKind.Cpa ? "CPA profile targets the managed local backend." : "Official profile keeps the upstream ChatGPT backend.")));

        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigFieldShell("Target Source", "Switch between official and cpa profiles", sourceBox),
            CreateConfigFieldShell("Repository Path", "Optional path used for project-level inspection and terminal launch", repositoryBox)));

        var details = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AddKeyValue(details, "Managed config", _codexConfigService.GetUserConfigPath());
        AddKeyValue(details, "Current auth", _codexConfigService.GetUserAuthPath());
        AddKeyValue(details, "Official auth backup", _codexConfigService.GetDesktopAuthBackupPath());
        AddKeyValue(details, "Last refresh", _toolsLastLoadedAt is null ? "-" : _toolsLastLoadedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        root.Children.Add(details);

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Apply Source Switch", ApplyToolsSourceAsync));
        actions.Children.Add(CreateActionButton("Launch Selected Source", LaunchSelectedToolAsync));
        actions.Children.Add(CreateActionButton("Open CODEX_HOME", () =>
        {
            ProcessStartFolder(_codexConfigService.GetUserConfigDirectory());
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateToolsCodexIntegrationCard()
    {
        var status = _toolsCodexStatus!;
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "These entries keep the desktop-side Codex workflow available after the native rewrite: executable discovery, version inspection, auth state, and terminal launch all run through the current machine instead of a web host.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var details = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AddKeyValue(details, "Installed", status.IsInstalled ? "Yes" : "No");
        AddKeyValue(details, "Executable", status.ExecutablePath ?? "codex executable was not found");
        AddKeyValue(details, "Version", string.IsNullOrWhiteSpace(status.Version) ? "Unavailable" : status.Version!);
        AddKeyValue(details, "Authentication", status.AuthenticationState);
        AddKeyValue(details, "Profile", status.DefaultProfile);
        AddKeyValue(details, "Effective Source", status.EffectiveSource);
        root.Children.Add(details);

        root.Children.Add(CreateText("Launch Command Preview", 14, FontWeights.SemiBold, "PrimaryTextBrush", new Thickness(0, 0, 0, 8)));
        root.Children.Add(CreateReadOnlyLogBox(
            string.IsNullOrWhiteSpace(_toolsCommandPreview)
                ? _codexLaunchService.BuildCommand(_toolsSelectedSource, NormalizeRepositoryPath(_toolsRepositoryPathDraft))
                : _toolsCommandPreview,
            minHeight: 110));

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(CreateActionButton("Refresh Codex Status", () => RefreshToolsAsync(force: true)));
        actions.Children.Add(CreateActionButton("Open Auth Backup", () =>
        {
            ProcessStartFolder(Path.GetDirectoryName(_codexConfigService.GetDesktopAuthBackupPath())!);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Repository Folder", () =>
        {
            var repositoryPath = NormalizeRepositoryPath(_toolsRepositoryPathDraft);
            if (!string.IsNullOrWhiteSpace(repositoryPath) && Directory.Exists(repositoryPath))
            {
                ProcessStartFolder(repositoryPath);
            }
            else
            {
                _toolsStatusMessage = "Repository path is empty or does not exist.";
                RefreshToolsSection();
            }

            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateToolsDesktopEntriesCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Tool entry points stay in the native shell so operators can move directly into the folders and diagnostics that matter during source switching or desktop troubleshooting.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Open Data Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.RootDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Config Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.ConfigDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Backend Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.BackendDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Logs Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.LogsDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Diagnostics Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.DiagnosticsDirectory);
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private async Task<string> BuildToolsApplyStatusMessageAsync(int backendPort)
    {
        var authPath = _codexConfigService.GetUserAuthPath();
        var authContent = File.Exists(authPath)
            ? await File.ReadAllTextAsync(authPath)
            : string.Empty;

        if (_toolsSelectedSource == CodexSourceKind.Cpa)
        {
            return authContent.Contains("\"OPENAI_API_KEY\": \"sk-dummy\"", StringComparison.Ordinal)
                ? $"Switched to cpa. CODEX_HOME now targets the managed backend on port {backendPort} and auth.json contains the CPA dummy key."
                : $"Switched to cpa on port {backendPort}, but auth.json did not contain the expected CPA dummy key.";
        }

        return authContent.Contains("\"OPENAI_API_KEY\": \"sk-dummy\"", StringComparison.Ordinal)
            ? "Official profile was written, but official auth could not be restored because no backup or preset auth file was found."
            : "Switched to official and restored the local desktop auth context when a backup or preset was available.";
    }

    private void RefreshToolsSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "tools")
        {
            UpdateSelectedSection();
        }
    }

    private static string? NormalizeRepositoryPath(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string FormatCodexSource(CodexSourceKind source)
    {
        return source == CodexSourceKind.Cpa ? "cpa" : "official";
    }
}
