using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CodexCliPlus.Core.Abstractions.Build;
using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Persistence;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Security;
using CodexCliPlus.Services;
using CodexCliPlus.Services.Notifications;
using CodexCliPlus.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace CodexCliPlus;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow, IDisposable
{
    private enum StartupState
    {
        UpgradeNotice,
        Preparing,
        FirstRunKeyReveal,
        NativeLogin,
        LoadingManagement,
        Blocked,
    }

    private enum NavigationDockVisualState
    {
        Resting,
        Icons,
        Expanded,
    }

    private const string AppHostName = "codexcliplus-webui.local";
    private const string UiTestModeEnvironmentVariable = "CODEXCLIPLUS_UI_TEST_MODE";
    private const string UiTestWebViewUserDataFolderEnvironmentVariable =
        "CODEXCLIPLUS_WEBVIEW2_USER_DATA_FOLDER";
    private const string UiTestWebViewRemoteDebuggingPortEnvironmentVariable =
        "CODEXCLIPLUS_WEBVIEW2_REMOTE_DEBUGGING_PORT";
    private const int FirstRunConfirmationSeconds = 5;
    private const double NavigationDockRestingWidth = 56;
    private const double NavigationDockEdgeIntentWidth = 18;
    private const double NavigationDockIconsWidth = 92;
    private const double NavigationDockExpandedWidth = 244;
    private const double NavigationDockPanelIconsWidth = 58;
    private const double NavigationDockPanelExpandedWidth = 188;
    private const double NavigationDockPanelRestingHeight = 132;
    private const double NavigationDockPanelOpenHeight = 392;
    private const double NavigationDockPanelRestingOffset = -8;
    private const double NavigationDockPanelOpenOffset = -18;
    private const double NavigationDockLabelExpandedWidth = 112;
    private const double NavigationDockMeasuredLabelWidthLimit = 118;
    private static readonly TimeSpan MinimumPreparationDisplayDuration = TimeSpan.FromMilliseconds(
        300
    );
    private static readonly TimeSpan UsageSnapshotSyncCooldown = TimeSpan.FromMinutes(2);
    private static readonly Uri AppEntryUri = new($"http://{AppHostName}/index.html");
    private static readonly JsonSerializerOptions WebMessageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly MainWindowViewModel _viewModel;
    private readonly BackendProcessManager _backendProcessManager;
    private readonly BackendConfigWriter _backendConfigWriter;
    private readonly IManagementSessionService _sessionService;
    private readonly IAppConfigurationService _appConfigurationService;
    private readonly IPathService _pathService;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly ISecretVault _secretVault;
    private readonly SensitiveConfigMigrationService _configMigrationService;
    private readonly IBuildInfo _buildInfo;
    private readonly IAppLogger _logger;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IUpdateInstallerService _updateInstallerService;
    private readonly IManagementPersistenceService _persistenceService;
    private readonly IManagementOverviewService _managementOverviewService;
    private readonly IManagementConfigurationService _managementConfigurationService;
    private readonly IManagementAuthService _managementAuthService;
    private readonly WebUiAssetLocator _webUiAssetLocator;
    private readonly ShellNotificationService _notificationService;

    private AppSettings _settings = new();
    private StartupState _startupState = StartupState.Preparing;
    private string? _bootstrapScriptId;
    private string _firstRunManagementKey = string.Empty;
    private string _shellConnectionStatus = "disconnected";
    private string _shellApiBase = string.Empty;
    private string _shellBackendVersion = BackendReleaseMetadata.Version;
    private string _shellTheme = "auto";
    private string _shellResolvedTheme = "light";
    private string _activeWebUiPath = "/";
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();
    private long _lastStartupMarkMilliseconds;
    private CancellationTokenSource? _firstRunConfirmCountdown;
    private DateTimeOffset? _preparationPanelShownAt;
    private ManagementSettingsSummarySnapshot? _settingsOverview;
    private Window? _settingsWindow;
    private Grid? _settingsWindowRoot;
    private CancellationTokenSource? _settingsOverviewRefreshCts;
    private int _settingsOverviewRefreshRequestId;
    private NavigationDockVisualState _navigationDockState = NavigationDockVisualState.Resting;
    private bool _suppressFollowSystemChange;
    private readonly DispatcherTimer _navigationDockCollapseTimer;
    private bool _allowClose;
    private bool _isInitializing;
    private bool _webViewConfigured;
    private bool _settingsOverlayOpen;
    private bool _isShellBrandDockClosing;
    private bool _wasShellBrandDockOpenBeforeButtonClick;
    private bool _sidebarCollapsed;
    private bool _isMainWindowActive;
    private CancellationTokenSource? _usageStatsSyncDebounceCts;
    private CancellationTokenSource? _postStartupPersistenceCts;
    private DateTimeOffset _lastUsageSnapshotSyncAt = DateTimeOffset.MinValue;

    public MainWindow(
        MainWindowViewModel viewModel,
        BackendProcessManager backendProcessManager,
        BackendConfigWriter backendConfigWriter,
        IManagementSessionService sessionService,
        IAppConfigurationService appConfigurationService,
        IPathService pathService,
        ISecureCredentialStore credentialStore,
        ISecretVault secretVault,
        SensitiveConfigMigrationService configMigrationService,
        IBuildInfo buildInfo,
        IAppLogger logger,
        IUpdateCheckService updateCheckService,
        IUpdateInstallerService updateInstallerService,
        IManagementPersistenceService persistenceService,
        IManagementOverviewService managementOverviewService,
        IManagementConfigurationService managementConfigurationService,
        IManagementAuthService managementAuthService,
        WebUiAssetLocator webUiAssetLocator,
        ShellNotificationService notificationService
    )
    {
        _viewModel = viewModel;
        _backendProcessManager = backendProcessManager;
        _backendConfigWriter = backendConfigWriter;
        _sessionService = sessionService;
        _appConfigurationService = appConfigurationService;
        _pathService = pathService;
        _credentialStore = credentialStore;
        _secretVault = secretVault;
        _configMigrationService = configMigrationService;
        _buildInfo = buildInfo;
        _logger = logger;
        _updateCheckService = updateCheckService;
        _updateInstallerService = updateInstallerService;
        _persistenceService = persistenceService;
        _managementOverviewService = managementOverviewService;
        _managementConfigurationService = managementConfigurationService;
        _managementAuthService = managementAuthService;
        _webUiAssetLocator = webUiAssetLocator;
        _notificationService = notificationService;

        _navigationDockCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };
        _navigationDockCollapseTimer.Tick += NavigationDockCollapseTimer_Tick;

        DataContext = _viewModel;
        InitializeComponent();

        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
        _notificationService.NotificationRequested +=
            ShellNotificationService_NotificationRequested;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }
}
