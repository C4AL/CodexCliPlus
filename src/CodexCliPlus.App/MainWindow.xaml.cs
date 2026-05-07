using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
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
using CodexCliPlus.Core.Models.LocalEnvironment;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Codex;
using CodexCliPlus.Infrastructure.LocalEnvironment;
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

    private readonly record struct WindowLayoutSnapshot(
        double Left,
        double Top,
        double Width,
        double Height,
        WindowState WindowState
    );

    private const string AppHostName = "codexcliplus-webui.local";
    private const string UiTestModeEnvironmentVariable = "CODEXCLIPLUS_UI_TEST_MODE";
    private const string UiTestWebViewUserDataFolderEnvironmentVariable =
        "CODEXCLIPLUS_WEBVIEW2_USER_DATA_FOLDER";
    private const string UiTestWebViewRemoteDebuggingPortEnvironmentVariable =
        "CODEXCLIPLUS_WEBVIEW2_REMOTE_DEBUGGING_PORT";
    private const int FirstRunConfirmationSeconds = 5;
    private const double MainWindowDefaultWidth = 1440;
    private const double MainWindowDefaultHeight = 920;
    private const double MainWindowMinWidth = 960;
    private const double MainWindowMinHeight = 640;
    private const double ShellTitleBarHeight = 50;
    private const double AuthenticationCompactWindowWidth = 320;
    private const double AuthenticationCompactWindowHeight = 460;
    private const double NavigationDockRestingWidth = 56;
    private const double NavigationDockEdgeIntentWidth = 18;
    private const double NavigationDockIconsWidth = 92;
    private const double NavigationDockExpandedWidth = 244;
    private const double NavigationDockPanelIconsWidth = 58;
    private const double NavigationDockPanelExpandedWidth = 188;
    private const double NavigationDockPanelRestingHeight = 132;
    private const double NavigationDockPanelRestingOffset = -8;
    private const double NavigationDockPanelOpenOffset = -18;
    private const double NavigationDockLabelExpandedWidth = 112;
    private const double NavigationDockMeasuredLabelWidthLimit = 118;
    private const int ShellThemeTransitionMilliseconds = 180;
    private static readonly TimeSpan MinimumPreparationDisplayDuration = TimeSpan.FromMilliseconds(
        300
    );
    private static readonly TimeSpan ExitPersistenceSyncTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UsageSnapshotSyncCooldown = TimeSpan.FromMinutes(2);
    private static readonly Uri AppEntryUri = new($"http://{AppHostName}/index.html");
    private static readonly JsonSerializerOptions WebMessageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
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
    private readonly IManagementApiClient _managementApiClient;
    private readonly IManagementOverviewService _managementOverviewService;
    private readonly IManagementConfigurationService _managementConfigurationService;
    private readonly IManagementAuthService _managementAuthService;
    private readonly LocalDependencyHealthService _localDependencyHealthService;
    private readonly LocalDependencyRepairService _localDependencyRepairService;
    private readonly CodexConfigService _codexConfigService;
    private readonly WebUiAssetLocator _webUiAssetLocator;
    private readonly ShellNotificationService _notificationService;
    private readonly ManagementChangeBroadcastService _changeBroadcastService;

    private AppSettings _settings = new();
    private StartupState _startupState = StartupState.Preparing;
    private string? _bootstrapScriptId;
    private readonly string _desktopSessionId = Guid.NewGuid().ToString("N");
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
    private WindowLayoutSnapshot? _preAuthenticationWindowLayout;
    private ManagementSettingsSummarySnapshot? _settingsOverview;
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
    private bool _isAuthenticationCompactWindowMode;
    private bool _initialPresentationRevealed;
    private bool _isManagementEntryTransitionActive;
    private bool _sidebarCollapsed;
    private bool _isMainWindowActive;
    private bool _isExitRequested;
    private long _shellThemePersistenceVersion;
    private long _managementNavigationNonce;
    private Task? _applicationExitTask;
    private TaskCompletionSource<bool>? _webViewNavigationCompletion;
    private CancellationTokenSource? _usageStatsSyncDebounceCts;
    private CancellationTokenSource? _postStartupPersistenceCts;
    private readonly SemaphoreSlim _shellThemePersistenceLock = new(1, 1);
    private readonly object _localDependencySnapshotLock = new();
    private Task<LocalDependencySnapshot>? _localDependencySnapshotTask;
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
        IManagementApiClient managementApiClient,
        IManagementOverviewService managementOverviewService,
        IManagementConfigurationService managementConfigurationService,
        IManagementAuthService managementAuthService,
        LocalDependencyHealthService localDependencyHealthService,
        LocalDependencyRepairService localDependencyRepairService,
        CodexConfigService codexConfigService,
        WebUiAssetLocator webUiAssetLocator,
        ShellNotificationService notificationService,
        ManagementChangeBroadcastService changeBroadcastService
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
        _managementApiClient = managementApiClient;
        _managementOverviewService = managementOverviewService;
        _managementConfigurationService = managementConfigurationService;
        _managementAuthService = managementAuthService;
        _localDependencyHealthService = localDependencyHealthService;
        _localDependencyRepairService = localDependencyRepairService;
        _codexConfigService = codexConfigService;
        _webUiAssetLocator = webUiAssetLocator;
        _notificationService = notificationService;
        _changeBroadcastService = changeBroadcastService;

        _navigationDockCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(520),
        };
        _navigationDockCollapseTimer.Tick += NavigationDockCollapseTimer_Tick;

        DataContext = _viewModel;
        InitializeComponent();
        PrepareHiddenAuthenticationStartupWindow();
        BindStartupFlowEvents();

        _backendProcessManager.StatusChanged += BackendProcessManager_StatusChanged;
        _notificationService.NotificationRequested +=
            ShellNotificationService_NotificationRequested;
        _changeBroadcastService.DataChanged += ManagementChangeBroadcastService_DataChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }
}
