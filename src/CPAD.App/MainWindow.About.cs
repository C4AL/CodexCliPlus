using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using CPAD.Core.About;
using CPAD.Core.Constants;

using WpfOrientation = System.Windows.Controls.Orientation;

namespace CPAD;

public partial class MainWindow
{
    private bool _aboutExporting;
    private string? _aboutStatusMessage;
    private string? _aboutError;
    private string? _aboutLastDiagnosticPackage;

    private UIElement BuildAboutContent()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateAboutHero());

        if (!string.IsNullOrWhiteSpace(_aboutStatusMessage))
        {
            root.Children.Add(CreateHintCard("About action", _aboutStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_aboutError))
        {
            root.Children.Add(CreateHintCard("About issue", _aboutError));
        }

        root.Children.Add(CreateSectionHeader("Version Information"));
        root.Children.Add(CreateAboutVersionCard());

        root.Children.Add(CreateSectionHeader("Licenses"));
        root.Children.Add(CreateAboutLicensesCard());

        root.Children.Add(CreateSectionHeader("Component Sources"));
        root.Children.Add(CreateAboutSourcesCard());

        root.Children.Add(CreateSectionHeader("Diagnostics Entry"));
        root.Children.Add(CreateAboutDiagnosticsCard());

        return root;
    }

    private Task ExportAboutDiagnosticsAsync()
    {
        if (_aboutExporting)
        {
            return Task.CompletedTask;
        }

        _aboutExporting = true;
        _aboutError = null;
        _aboutStatusMessage = "Exporting a redacted diagnostic package from the About page...";
        RefreshAboutSection();

        try
        {
            _aboutLastDiagnosticPackage = _diagnosticsService.ExportPackage(
                _backendProcessManager.CurrentStatus,
                BuildCodexDiagnosticsSnapshot(),
                GetDependencyStatus());
            _aboutStatusMessage = $"Diagnostic package exported: {_aboutLastDiagnosticPackage}";
        }
        catch (Exception exception)
        {
            _aboutError = exception.Message;
            _aboutStatusMessage = "Diagnostic package export failed.";
        }
        finally
        {
            _aboutExporting = false;
            RefreshAboutSection();
        }

        return Task.CompletedTask;
    }

    private UIElement CreateAboutHero()
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Native desktop shell for CLIProxyAPI",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            $"{AppConstants.ProductName} runs as {AppConstants.ExecutableName} with AppUserModelID {AppConstants.AppUserModelId}.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateText(
            "This page lists the desktop build, backend/runtime sources, license files, and redacted diagnostics entry points without returning to WebView2 hosting.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 4, 0, 0)));
        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateAboutVersionCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Desktop Version", _buildInfo.ApplicationVersion, "Assembly version"),
            CreateMetricCard("Informational Version", _buildInfo.InformationalVersion, "Build metadata"),
            CreateMetricCard("Backend State", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message),
            CreateMetricCard("Backend Version", _updatesBackendCurrentVersion ?? "Unknown", $"Commit: {_updatesBackendCommit ?? "unknown"}"),
            CreateMetricCard("Backend Build", FormatBuildDate(_updatesBackendBuildDate), "Read from Management API metadata when available"),
            CreateMetricCard("Data Mode", FormatAppDataMode(_pathService.Directories.DataMode), _pathService.Directories.RootDirectory)));

        var details = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AddKeyValue(details, "Product", AppConstants.ProductName);
        AddKeyValue(details, "Executable", AppConstants.ExecutableName);
        AddKeyValue(details, "Installer Prefix", AppConstants.InstallerNamePrefix);
        AddKeyValue(details, "AppUserModelID", AppConstants.AppUserModelId);
        AddKeyValue(details, "Settings File", _pathService.Directories.SettingsFilePath);
        AddKeyValue(details, "Backend Runtime", Path.Combine(_pathService.Directories.BackendDirectory, BackendExecutableFileName));
        root.Children.Add(details);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateAboutLicensesCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Desktop License", "MIT", ResolveLicensePath("CPAD.LICENSE.txt", "LICENSE.txt")),
            CreateMetricCard("Backend License", "MIT", ResolveLicensePath("CLIProxyAPI.LICENSE.txt", Path.Combine("resources", "backend", "windows-x64", "LICENSE"))),
            CreateMetricCard("Diagnostics", "Redacted", "Diagnostic packages redact secrets before export.")));

        root.Children.Add(CreateConfigFieldGrid(
            CreateConfigFieldShell(
                "Desktop License Preview",
                ResolveLicensePath("CPAD.LICENSE.txt", "LICENSE.txt"),
                CreateReadOnlyLogBox(ReadLicensePreview("CPAD.LICENSE.txt", "LICENSE.txt"), minHeight: 120)),
            CreateConfigFieldShell(
                "Backend License Preview",
                ResolveLicensePath("CLIProxyAPI.LICENSE.txt", Path.Combine("resources", "backend", "windows-x64", "LICENSE")),
                CreateReadOnlyLogBox(ReadLicensePreview("CLIProxyAPI.LICENSE.txt", Path.Combine("resources", "backend", "windows-x64", "LICENSE")), minHeight: 120))));

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateAboutSourcesCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "The native desktop shell is owned by the Blackblock desktop repository. Runtime and API semantics remain aligned with the audited reference baselines recorded in build_steps.md.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        foreach (var source in AboutCatalog.ComponentSources)
        {
            var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
            panel.Children.Add(CreateText(source.Name, 15, FontWeights.SemiBold, "PrimaryTextBrush"));
            panel.Children.Add(CreateText(
                $"{source.Role} | License: {source.License}",
                12,
                FontWeights.SemiBold,
                "SecondaryTextBrush",
                new Thickness(0, 5, 0, 0)));
            panel.Children.Add(CreateText(
                $"Origin: {source.Origin}",
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 5, 0, 0)));
            panel.Children.Add(CreateText(
                source.Notes,
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 5, 0, 0)));
            root.Children.Add(CreateCard(panel, new Thickness(0, 0, 0, 10)));
        }

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateAboutDiagnosticsCard()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("Backend Snapshot", _backendProcessManager.CurrentStatus.State.ToString(), _backendProcessManager.CurrentStatus.Message),
            CreateMetricCard("Dependency Snapshot", GetDependencyStatus().IsAvailable ? "Ready" : "Attention", GetDependencyStatus().Summary),
            CreateMetricCard("Last Export", string.IsNullOrWhiteSpace(_aboutLastDiagnosticPackage) ? "Not exported" : "Available", _aboutLastDiagnosticPackage ?? _pathService.Directories.DiagnosticsDirectory)));
        root.Children.Add(CreateText(
            "Diagnostic export includes a redacted environment report, desktop log, desktop settings, and backend config when present. It does not require the backend to be running.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 12)));

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton(_aboutExporting ? "Exporting..." : "Export Diagnostic Package", ExportAboutDiagnosticsAsync));
        actions.Children.Add(CreateActionButton("Open Diagnostics Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.DiagnosticsDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Data Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.RootDirectory);
            return Task.CompletedTask;
        }));
        actions.Children.Add(CreateActionButton("Open Logs Folder", () =>
        {
            ProcessStartFolder(_pathService.Directories.LogsDirectory);
            return Task.CompletedTask;
        }));
        root.Children.Add(actions);

        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private void RefreshAboutSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "about")
        {
            UpdateSelectedSection();
        }
    }

    private static string ReadLicensePreview(string outputFileName, string repositoryRelativePath)
    {
        var path = ResolveLicensePath(outputFileName, repositoryRelativePath);
        if (!File.Exists(path))
        {
            return $"License file not found: {path}";
        }

        return string.Join(Environment.NewLine, File.ReadLines(path).Take(12));
    }

    private static string ResolveLicensePath(string outputFileName, string repositoryRelativePath)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Licenses", outputFileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, repositoryRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return outputPath;
    }
}
