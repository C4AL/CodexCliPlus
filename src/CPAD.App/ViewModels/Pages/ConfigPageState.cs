using System.Collections.ObjectModel;
using System.Globalization;

using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;
using CPAD.Views.Pages;

using CommunityToolkit.Mvvm.ComponentModel;

namespace CPAD.ViewModels.Pages;

public sealed class ConfigPageState : ObservableObject
{
    private const string ScopeName = "配置";

    private readonly IUnsavedChangesGuard _unsavedChangesGuard;
    private readonly HashSet<string> _changedFields = new(StringComparer.OrdinalIgnoreCase);

    private bool _suspendTracking;
    private ConfigFieldSnapshot _baseline = ConfigFieldSnapshot.Empty;
    private bool _debug;
    private string _proxyUrl = string.Empty;
    private bool _webSocketAuth;
    private string _requestRetry = string.Empty;
    private string _maxRetryInterval = string.Empty;
    private bool _forceModelPrefix;
    private string _routingStrategy = string.Empty;
    private bool _requestLog;
    private bool _loggingToFile;
    private bool _usageStatisticsEnabled;
    private string _logsMaxTotalSizeMb = string.Empty;
    private string _errorLogsMaxFiles = string.Empty;
    private bool _switchProject;
    private bool _switchPreviewModel;
    private string _antigravityCreditsDisplay = "未设置";
    private string _serverYaml = string.Empty;
    private string _advancedYamlDraft = string.Empty;
    private string _yamlSearchQuery = string.Empty;

    public ConfigPageState(IUnsavedChangesGuard unsavedChangesGuard)
    {
        _unsavedChangesGuard = unsavedChangesGuard;
    }

    public bool Debug
    {
        get => _debug;
        set
        {
            if (SetProperty(ref _debug, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public string ProxyUrl
    {
        get => _proxyUrl;
        set
        {
            if (SetProperty(ref _proxyUrl, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public bool WebSocketAuth
    {
        get => _webSocketAuth;
        set
        {
            if (SetProperty(ref _webSocketAuth, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public string RequestRetry
    {
        get => _requestRetry;
        set
        {
            if (SetProperty(ref _requestRetry, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public string MaxRetryInterval
    {
        get => _maxRetryInterval;
        set
        {
            if (SetProperty(ref _maxRetryInterval, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public bool ForceModelPrefix
    {
        get => _forceModelPrefix;
        set
        {
            if (SetProperty(ref _forceModelPrefix, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public string RoutingStrategy
    {
        get => _routingStrategy;
        set
        {
            if (SetProperty(ref _routingStrategy, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public bool RequestLog
    {
        get => _requestLog;
        set
        {
            if (SetProperty(ref _requestLog, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public bool LoggingToFile
    {
        get => _loggingToFile;
        set
        {
            if (SetProperty(ref _loggingToFile, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public bool UsageStatisticsEnabled
    {
        get => _usageStatisticsEnabled;
        set
        {
            if (SetProperty(ref _usageStatisticsEnabled, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public string LogsMaxTotalSizeMb
    {
        get => _logsMaxTotalSizeMb;
        set
        {
            if (SetProperty(ref _logsMaxTotalSizeMb, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public string ErrorLogsMaxFiles
    {
        get => _errorLogsMaxFiles;
        set
        {
            if (SetProperty(ref _errorLogsMaxFiles, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public bool SwitchProject
    {
        get => _switchProject;
        set
        {
            if (SetProperty(ref _switchProject, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public bool SwitchPreviewModel
    {
        get => _switchPreviewModel;
        set
        {
            if (SetProperty(ref _switchPreviewModel, value))
            {
                RecalculateDirtyState();
            }
        }
    }

    public string AntigravityCreditsDisplay
    {
        get => _antigravityCreditsDisplay;
        private set => SetProperty(ref _antigravityCreditsDisplay, value);
    }

    public string ServerYaml
    {
        get => _serverYaml;
        private set
        {
            if (SetProperty(ref _serverYaml, value))
            {
                OnPropertyChanged(nameof(YamlSearchResultText));
            }
        }
    }

    public string AdvancedYamlDraft
    {
        get => _advancedYamlDraft;
        set
        {
            if (SetProperty(ref _advancedYamlDraft, value))
            {
                OnPropertyChanged(nameof(HasYamlChanges));
                OnPropertyChanged(nameof(HasAnyChanges));
                OnPropertyChanged(nameof(CanSaveFields));
                OnPropertyChanged(nameof(CanSaveYaml));
                OnPropertyChanged(nameof(AdvancedYamlNotice));
                OnPropertyChanged(nameof(YamlSearchResultText));
                UpdateUnsavedChangesGuard();
            }
        }
    }

    public string YamlSearchQuery
    {
        get => _yamlSearchQuery;
        set
        {
            if (SetProperty(ref _yamlSearchQuery, value))
            {
                OnPropertyChanged(nameof(YamlSearchResultText));
            }
        }
    }

    public bool HasFieldChanges => _changedFields.Count > 0;

    public bool HasYamlChanges => !string.Equals(ServerYaml, AdvancedYamlDraft, StringComparison.Ordinal);

    public bool HasAnyChanges => HasFieldChanges || HasYamlChanges;

    public int ChangedFieldCount => _changedFields.Count;

    public string ChangedFieldSummary => HasFieldChanges
        ? string.Create(
            CultureInfo.CurrentCulture,
            $"已修改 {ChangedFieldCount.ToString(CultureInfo.CurrentCulture)} 项字段")
        : "字段草稿已同步";

    public string YamlSearchResultText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(YamlSearchQuery))
            {
                return "输入关键字后显示匹配次数";
            }

            var count = ManagementPageSupport.CountOccurrences(AdvancedYamlDraft, YamlSearchQuery);
            return string.Create(
                CultureInfo.CurrentCulture,
                $"匹配次数：{count.ToString(CultureInfo.CurrentCulture)}");
        }
    }

    public string AdvancedYamlNotice
    {
        get
        {
            if (HasFieldChanges)
            {
                return "字段草稿未保存时无法应用高级 YAML，请先保存或放弃字段修改。";
            }

            return "高级 YAML 仅作为兜底能力保留。应用前会显示差异确认。";
        }
    }

    public bool CanSaveFields => HasFieldChanges && !HasYamlChanges;

    public bool CanSaveYaml => HasYamlChanges && !HasFieldChanges;

    public IReadOnlyCollection<string> ChangedFields => new ReadOnlyCollection<string>(_changedFields.ToArray());

    public void LoadFromSnapshot(ManagementConfigSnapshot snapshot, string serverYaml)
    {
        var nextBaseline = ConfigFieldSnapshot.FromSnapshot(snapshot);

        _suspendTracking = true;
        try
        {
            _baseline = nextBaseline;
            Debug = nextBaseline.Debug;
            ProxyUrl = nextBaseline.ProxyUrl;
            WebSocketAuth = nextBaseline.WebSocketAuth;
            RequestRetry = nextBaseline.RequestRetry;
            MaxRetryInterval = nextBaseline.MaxRetryInterval;
            ForceModelPrefix = nextBaseline.ForceModelPrefix;
            RoutingStrategy = nextBaseline.RoutingStrategy;
            RequestLog = nextBaseline.RequestLog;
            LoggingToFile = nextBaseline.LoggingToFile;
            UsageStatisticsEnabled = nextBaseline.UsageStatisticsEnabled;
            LogsMaxTotalSizeMb = nextBaseline.LogsMaxTotalSizeMb;
            ErrorLogsMaxFiles = nextBaseline.ErrorLogsMaxFiles;
            SwitchProject = nextBaseline.SwitchProject;
            SwitchPreviewModel = nextBaseline.SwitchPreviewModel;
            AntigravityCreditsDisplay = nextBaseline.AntigravityCreditsDisplay;
            ServerYaml = serverYaml ?? string.Empty;
            AdvancedYamlDraft = ServerYaml;
        }
        finally
        {
            _suspendTracking = false;
        }

        RecalculateDirtyState();
    }

    public void RestoreFromServer()
    {
        _suspendTracking = true;
        try
        {
            Debug = _baseline.Debug;
            ProxyUrl = _baseline.ProxyUrl;
            WebSocketAuth = _baseline.WebSocketAuth;
            RequestRetry = _baseline.RequestRetry;
            MaxRetryInterval = _baseline.MaxRetryInterval;
            ForceModelPrefix = _baseline.ForceModelPrefix;
            RoutingStrategy = _baseline.RoutingStrategy;
            RequestLog = _baseline.RequestLog;
            LoggingToFile = _baseline.LoggingToFile;
            UsageStatisticsEnabled = _baseline.UsageStatisticsEnabled;
            LogsMaxTotalSizeMb = _baseline.LogsMaxTotalSizeMb;
            ErrorLogsMaxFiles = _baseline.ErrorLogsMaxFiles;
            SwitchProject = _baseline.SwitchProject;
            SwitchPreviewModel = _baseline.SwitchPreviewModel;
            AntigravityCreditsDisplay = _baseline.AntigravityCreditsDisplay;
            AdvancedYamlDraft = ServerYaml;
        }
        finally
        {
            _suspendTracking = false;
        }

        RecalculateDirtyState();
    }

    public bool TryCreateSavePayload(out ConfigPageSavePayload payload, out string? validationError)
    {
        payload = default;
        validationError = null;

        if (!HasFieldChanges)
        {
            return true;
        }

        if (HasYamlChanges)
        {
            validationError = "高级 YAML 仍有未保存修改，请先保存或放弃高级 YAML 草稿。";
            return false;
        }

        if (!TryParseOptionalInteger(RequestRetry, "请求重试次数", out var requestRetry, out validationError) ||
            !TryParseOptionalInteger(MaxRetryInterval, "最大重试间隔", out var maxRetryInterval, out validationError) ||
            !TryParseOptionalInteger(LogsMaxTotalSizeMb, "日志总大小上限", out var logsMaxTotalSizeMb, out validationError) ||
            !TryParseOptionalInteger(ErrorLogsMaxFiles, "错误日志文件上限", out var errorLogsMaxFiles, out validationError))
        {
            return false;
        }

        payload = new ConfigPageSavePayload(
            Debug,
            NormalizeText(ProxyUrl),
            WebSocketAuth,
            requestRetry,
            maxRetryInterval,
            ForceModelPrefix,
            NormalizeText(RoutingStrategy),
            RequestLog,
            LoggingToFile,
            UsageStatisticsEnabled,
            logsMaxTotalSizeMb,
            errorLogsMaxFiles,
            SwitchProject,
            SwitchPreviewModel,
            _changedFields.ToArray());

        return true;
    }

    private void RecalculateDirtyState()
    {
        if (_suspendTracking)
        {
            return;
        }

        _changedFields.Clear();

        Track("debug", Debug, _baseline.Debug);
        Track("proxy-url", NormalizeText(ProxyUrl), _baseline.ProxyUrl);
        Track("ws-auth", WebSocketAuth, _baseline.WebSocketAuth);
        Track("request-retry", NormalizeText(RequestRetry), _baseline.RequestRetry);
        Track("max-retry-interval", NormalizeText(MaxRetryInterval), _baseline.MaxRetryInterval);
        Track("force-model-prefix", ForceModelPrefix, _baseline.ForceModelPrefix);
        Track("routing/strategy", NormalizeText(RoutingStrategy), _baseline.RoutingStrategy);
        Track("request-log", RequestLog, _baseline.RequestLog);
        Track("logging-to-file", LoggingToFile, _baseline.LoggingToFile);
        Track("usage-statistics-enabled", UsageStatisticsEnabled, _baseline.UsageStatisticsEnabled);
        Track("logs-max-total-size-mb", NormalizeText(LogsMaxTotalSizeMb), _baseline.LogsMaxTotalSizeMb);
        Track("error-logs-max-files", NormalizeText(ErrorLogsMaxFiles), _baseline.ErrorLogsMaxFiles);
        Track("quota-exceeded/switch-project", SwitchProject, _baseline.SwitchProject);
        Track("quota-exceeded/switch-preview-model", SwitchPreviewModel, _baseline.SwitchPreviewModel);

        OnPropertyChanged(nameof(ChangedFieldCount));
        OnPropertyChanged(nameof(ChangedFieldSummary));
        OnPropertyChanged(nameof(HasFieldChanges));
        OnPropertyChanged(nameof(HasAnyChanges));
        OnPropertyChanged(nameof(CanSaveFields));
        OnPropertyChanged(nameof(CanSaveYaml));
        OnPropertyChanged(nameof(AdvancedYamlNotice));
        UpdateUnsavedChangesGuard();
    }

    private void Track<T>(string path, T current, T original)
    {
        if (!EqualityComparer<T>.Default.Equals(current, original))
        {
            _changedFields.Add(path);
        }
    }

    private void UpdateUnsavedChangesGuard()
    {
        _unsavedChangesGuard.SetDirty(ScopeName, HasAnyChanges);
    }

    private static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static bool TryParseOptionalInteger(string value, string label, out int? result, out string? validationError)
    {
        var trimmed = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            result = null;
            validationError = null;
            return true;
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            result = null;
            validationError = $"{label}必须是整数。";
            return false;
        }

        result = parsed;
        validationError = null;
        return true;
    }

    private sealed record ConfigFieldSnapshot(
        bool Debug,
        string ProxyUrl,
        bool WebSocketAuth,
        string RequestRetry,
        string MaxRetryInterval,
        bool ForceModelPrefix,
        string RoutingStrategy,
        bool RequestLog,
        bool LoggingToFile,
        bool UsageStatisticsEnabled,
        string LogsMaxTotalSizeMb,
        string ErrorLogsMaxFiles,
        bool SwitchProject,
        bool SwitchPreviewModel,
        string AntigravityCreditsDisplay)
    {
        public static readonly ConfigFieldSnapshot Empty = new(
            Debug: false,
            ProxyUrl: string.Empty,
            WebSocketAuth: false,
            RequestRetry: string.Empty,
            MaxRetryInterval: string.Empty,
            ForceModelPrefix: false,
            RoutingStrategy: string.Empty,
            RequestLog: false,
            LoggingToFile: false,
            UsageStatisticsEnabled: false,
            LogsMaxTotalSizeMb: string.Empty,
            ErrorLogsMaxFiles: string.Empty,
            SwitchProject: false,
            SwitchPreviewModel: false,
            AntigravityCreditsDisplay: "未设置");

        public static ConfigFieldSnapshot FromSnapshot(ManagementConfigSnapshot snapshot)
        {
            return new ConfigFieldSnapshot(
                snapshot.Debug ?? false,
                NormalizeText(snapshot.ProxyUrl),
                snapshot.WebSocketAuth ?? false,
                snapshot.RequestRetry?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                snapshot.MaxRetryInterval?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                snapshot.ForceModelPrefix ?? false,
                NormalizeText(snapshot.RoutingStrategy),
                snapshot.RequestLog ?? false,
                snapshot.LoggingToFile ?? false,
                snapshot.UsageStatisticsEnabled ?? false,
                snapshot.LogsMaxTotalSizeMb?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                snapshot.ErrorLogsMaxFiles?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                snapshot.QuotaExceeded?.SwitchProject ?? false,
                snapshot.QuotaExceeded?.SwitchPreviewModel ?? false,
                ManagementPageSupport.FormatBoolean(snapshot.QuotaExceeded?.AntigravityCredits));
        }
    }
}

public readonly record struct ConfigPageSavePayload(
    bool Debug,
    string ProxyUrl,
    bool WebSocketAuth,
    int? RequestRetry,
    int? MaxRetryInterval,
    bool ForceModelPrefix,
    string RoutingStrategy,
    bool RequestLog,
    bool LoggingToFile,
    bool UsageStatisticsEnabled,
    int? LogsMaxTotalSizeMb,
    int? ErrorLogsMaxFiles,
    bool SwitchProject,
    bool SwitchPreviewModel,
    IReadOnlyList<string> ChangedFields);
