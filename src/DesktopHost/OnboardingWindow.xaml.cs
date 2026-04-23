using System.IO;
using System.Windows;
using System.Windows.Controls;
using MediaBrush = System.Windows.Media.Brush;

using DesktopHost.Core.Abstractions.Configuration;
using DesktopHost.Core.Abstractions.Logging;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Enums;
using DesktopHost.Core.Models;
using DesktopHost.Infrastructure.Backend;
using DesktopHost.Infrastructure.Codex;
using DesktopHost.Services;

using MessageBox = System.Windows.MessageBox;

namespace DesktopHost;

public partial class OnboardingWindow : Window
{
    private readonly IDesktopConfigurationService _configurationService;
    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly WebView2RuntimeService _webView2RuntimeService;
    private readonly BackendAssetService _backendAssetService;
    private readonly BackendConfigWriter _backendConfigWriter;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly CodexLocator _codexLocator;
    private readonly CodexVersionReader _codexVersionReader;
    private readonly CodexConfigService _codexConfigService;

    private readonly Border[] _stepCards;
    private readonly FrameworkElement[] _stepPanels;

    private DesktopSettings _settings = new();
    private BackendAssetLayout? _backendAssetLayout;
    private bool _automationMode;
    private bool _dependencyChecksCompleted;
    private bool _configurationInitialized;
    private bool _completionDone;
    private bool _isBusy;
    private bool _allowClose;
    private int _currentStepIndex;

    public OnboardingWindow(
        IDesktopConfigurationService configurationService,
        IPathService pathService,
        IAppLogger logger,
        WebView2RuntimeService webView2RuntimeService,
        BackendAssetService backendAssetService,
        BackendConfigWriter backendConfigWriter,
        BackendProcessManager backendProcessManager,
        CodexLocator codexLocator,
        CodexVersionReader codexVersionReader,
        CodexConfigService codexConfigService)
    {
        _configurationService = configurationService;
        _pathService = pathService;
        _logger = logger;
        _webView2RuntimeService = webView2RuntimeService;
        _backendAssetService = backendAssetService;
        _backendConfigWriter = backendConfigWriter;
        _backendProcessManager = backendProcessManager;
        _codexLocator = codexLocator;
        _codexVersionReader = codexVersionReader;
        _codexConfigService = codexConfigService;

        InitializeComponent();

        _stepCards = [StepCard0, StepCard1, StepCard2, StepCard3, StepCard4];
        _stepPanels = [WelcomePanel, DependencyPanel, InitializationPanel, SourcePanel, CompletionPanel];

        Loaded += OnboardingWindow_Loaded;
        Closing += OnboardingWindow_Closing;
    }

    public void ConfigureAutomation(bool enabled)
    {
        _automationMode = enabled;
    }

    private async void OnboardingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _configurationService.LoadAsync();
        if (_settings.PreferredCodexSource == CodexSourceKind.Cpa)
        {
            CpaSourceRadioButton.IsChecked = true;
        }
        else
        {
            OfficialSourceRadioButton.IsChecked = true;
        }

        SetStep(0);

        if (_automationMode)
        {
            _ = RunAutomationAsync();
        }
    }

    private void OnboardingWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_automationMode)
        {
            Environment.ExitCode = Environment.ExitCode == 0 ? -1 : Environment.ExitCode;
            return;
        }

        var result = MessageBox.Show(
            "首次运行向导尚未完成，确定直接退出桌面宿主吗？",
            "退出 CPAD",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_currentStepIndex)
        {
            case 0:
                SetStep(1);
                await RunStepAsync(RunDependencyChecksAsync);
                break;
            case 1:
                SetStep(2);
                break;
            case 2:
                await RunStepAsync(InitializeConfigurationAsync);
                SetStep(3);
                break;
            case 3:
                await RunStepAsync(CompleteOnboardingAsync);
                SetStep(4);
                break;
            case 4:
                _allowClose = true;
                DialogResult = true;
                Close();
                break;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentStepIndex is 0 or 4)
        {
            return;
        }

        SetStep(_currentStepIndex - 1);
    }

    private async Task RunAutomationAsync()
    {
        try
        {
            SetStep(1);
            await RunDependencyChecksAsync();
            SetStep(2);
            await InitializeConfigurationAsync();
            SetStep(3);
            await CompleteOnboardingAsync();
            _allowClose = true;
            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            _logger.LogError("首次运行向导自动验证失败。", exception);
            Environment.ExitCode = 1;
            _allowClose = true;
            DialogResult = false;
            Close();
        }
    }

    private async Task RunStepAsync(Func<Task> action)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            _isBusy = true;
            UpdateNavigationState();
            await action();
        }
        catch (Exception exception)
        {
            _logger.LogError("首次运行向导执行失败。", exception);
            FooterTextBlock.Text = exception.Message;
            MessageBox.Show(
                exception.Message,
                "CPAD 首次运行向导",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            UpdateNavigationState();
        }
    }

    private async Task RunDependencyChecksAsync()
    {
        if (_dependencyChecksCompleted)
        {
            FooterTextBlock.Text = "依赖检查已完成。";
            return;
        }

        FooterTextBlock.Text = "正在检查 WebView2 Runtime、Codex CLI 和后端资源...";

        var webViewRuntime = _webView2RuntimeService.Check();
        WebViewDependencyTextBlock.Text = webViewRuntime.IsAvailable
            ? $"{webViewRuntime.Summary}\n{webViewRuntime.Detail}"
            : $"{webViewRuntime.Summary}\n{webViewRuntime.Detail}\n可继续完成初始化，但进入主界面前需要安装 Runtime。";

        var codexPath = await _codexLocator.LocateAsync();
        if (codexPath is null)
        {
            CodexDependencyTextBlock.Text = "未找到 Codex CLI。\n不阻断首次运行；安装后可在主界面重新检测。";
        }
        else
        {
            var version = await _codexVersionReader.ReadAsync();
            CodexDependencyTextBlock.Text = string.IsNullOrWhiteSpace(version)
                ? $"已找到：{codexPath}"
                : $"已找到：{codexPath}\n版本：{version}";
        }

        _backendAssetLayout = await _backendAssetService.EnsureAssetsAsync();
        BackendDependencyTextBlock.Text =
            $"后端可执行文件：{_backendAssetLayout.ExecutablePath}\n管理页：{_backendAssetLayout.ManagementHtmlPath}";

        _dependencyChecksCompleted = true;
        FooterTextBlock.Text = "依赖检查完成。";
    }

    private async Task InitializeConfigurationAsync()
    {
        if (_configurationInitialized)
        {
            FooterTextBlock.Text = "初始化配置已完成。";
            return;
        }

        if (!_dependencyChecksCompleted)
        {
            await RunDependencyChecksAsync();
        }

        FooterTextBlock.Text = "正在创建目录并写入桌面配置...";

        await _pathService.EnsureCreatedAsync();
        _settings = await _configurationService.LoadAsync();
        _settings.LastRepositoryPath ??= TryDetectRepositoryRoot();
        _settings.PreferredCodexSource = GetSelectedSource();

        _backendAssetLayout ??= await _backendAssetService.EnsureAssetsAsync();
        var runtime = await _backendConfigWriter.WriteAsync(_settings, _backendAssetLayout);
        await _codexConfigService.ApplyDesktopModeAsync(runtime.Port, _settings.PreferredCodexSource);
        await _configurationService.SaveAsync(_settings);

        AppRootTextBlock.Text = $"应用目录：{_pathService.Directories.RootDirectory}";
        DesktopConfigTextBlock.Text = $"桌面配置：{_pathService.Directories.DesktopConfigFilePath}";
        BackendConfigTextBlock.Text = $"后端配置：{runtime.ConfigPath}";
        InitializationStatusTextBlock.Text =
            $"已写入运行目录与初始配置。\n后端端口：{runtime.Port}\n默认源：{CodexConfigService.GetSourceName(_settings.PreferredCodexSource)}";

        _configurationInitialized = true;
        FooterTextBlock.Text = "桌面配置写入完成。";
    }

    private async Task CompleteOnboardingAsync()
    {
        if (_completionDone)
        {
            FooterTextBlock.Text = "首次运行已完成。";
            return;
        }

        if (!_configurationInitialized)
        {
            await InitializeConfigurationAsync();
        }

        FooterTextBlock.Text = "正在应用默认源并启动后端...";

        _settings.PreferredCodexSource = GetSelectedSource();
        await _codexConfigService.ApplyDesktopModeAsync(_settings.BackendPort, _settings.PreferredCodexSource);

        var backendStatus = await _backendProcessManager.StartAsync();
        if (backendStatus.State != BackendStateKind.Running || backendStatus.Runtime is null)
        {
            throw new InvalidOperationException(backendStatus.LastError ?? backendStatus.Message);
        }

        _settings.OnboardingCompleted = true;
        await _configurationService.SaveAsync(_settings);

        CompletionStatusTextBlock.Text =
            $"已完成首次运行初始化。\n默认源：{CodexConfigService.GetSourceName(_settings.PreferredCodexSource)}\n后端地址：{backendStatus.Runtime.ManagementPageUrl}";
        CompletionDetailTextBlock.Text =
            $"桌面配置已保存到：{_pathService.Directories.DesktopConfigFilePath}\n" +
            $"Codex 用户配置目录：{_codexConfigService.GetUserConfigDirectory()}\n" +
            "下一步将进入主界面，并继续用 WebView2 托管原版 management.html。";

        _completionDone = true;
        FooterTextBlock.Text = "首次运行闭环已完成。";
    }

    private void SetStep(int index)
    {
        _currentStepIndex = index;

        for (var i = 0; i < _stepPanels.Length; i++)
        {
            _stepPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            _stepCards[i].BorderBrush = i == index
                ? (MediaBrush)FindResource("AppPrimaryBrush")
                : (MediaBrush)FindResource("AppBorderBrush");
            _stepCards[i].BorderThickness = i == index ? new Thickness(2) : new Thickness(1);
            _stepCards[i].Opacity = i == index ? 1 : 0.62;
        }

        (StepTitleTextBlock.Text, StepSubtitleTextBlock.Text) = index switch
        {
            0 => ("欢迎", "首次运行将按顺序完成依赖检查、配置写入、默认源选择与后端启动。"),
            1 => ("依赖检查", "先确认 WebView2 Runtime、Codex CLI 和后端资源的可用性。"),
            2 => ("写入配置", "现在创建桌面目录、写入 desktop.json 与 cliproxyapi.yaml。"),
            3 => ("选择默认源", "选择桌面宿主默认使用的 Codex 源，之后仍可在主界面一键切换。"),
            4 => ("完成", "初始化已经闭环，接下来进入桌面宿主主界面。"),
            _ => (StepTitleTextBlock.Text, StepSubtitleTextBlock.Text)
        };

        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        BackButton.Visibility = _currentStepIndex is 0 or 4 ? Visibility.Collapsed : Visibility.Visible;
        BackButton.IsEnabled = !_isBusy;
        NextButton.IsEnabled = !_isBusy;

        NextButton.Content = _currentStepIndex switch
        {
            0 => "开始",
            1 => "继续",
            2 => _configurationInitialized ? "继续" : "写入配置",
            3 => _completionDone ? "继续" : "完成初始化",
            4 => "进入桌面宿主",
            _ => "下一步"
        };
    }

    private CodexSourceKind GetSelectedSource()
    {
        return CpaSourceRadioButton.IsChecked == true
            ? CodexSourceKind.Cpa
            : CodexSourceKind.Official;
    }

    private static string? TryDetectRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; current is not null && depth < 10; depth++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CliProxyApiDesktop.sln"))
                || Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
        }

        return null;
    }
}
