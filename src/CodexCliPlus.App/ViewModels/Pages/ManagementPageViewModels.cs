using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Exceptions;
using CodexCliPlus.Core.Models.Management;

namespace CodexCliPlus.ViewModels.Pages;

public abstract class ManagementPageViewModel : ObservableObject
{
    private bool _isBusy;
    private string _status = "等待刷新";
    private string _error = string.Empty;

    protected ManagementPageViewModel(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }

    public string Subtitle { get; }

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    public string Status
    {
        get => _status;
        protected set => SetProperty(ref _status, value);
    }

    public string Error
    {
        get => _error;
        protected set => SetProperty(ref _error, value);
    }

    protected async Task RunAsync(string busyStatus, Func<Task> action)
    {
        IsBusy = true;
        Status = busyStatus;
        Error = string.Empty;

        try
        {
            await action();
            Status = string.Create(
                CultureInfo.CurrentCulture,
                $"已刷新：{DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}");
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            Status = "操作失败";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class DashboardPageViewModel : ManagementPageViewModel
{
    private readonly IManagementOverviewService _overviewService;

    public DashboardPageViewModel(IManagementOverviewService overviewService)
        : base("仪表盘", "连接状态、密钥、认证文件、模型和用量概览")
    {
        _overviewService = overviewService;
    }

    public ManagementOverviewSnapshot? Snapshot { get; private set; }

    public Task RefreshAsync()
    {
        return RunAsync("正在刷新仪表盘", async () =>
        {
            Snapshot = (await _overviewService.GetOverviewAsync()).Value;
            OnPropertyChanged(nameof(Snapshot));
        });
    }
}

public sealed class ConfigPageViewModel : ManagementPageViewModel
{
    private readonly IManagementConfigurationService _configurationService;

    public ConfigPageViewModel(
        IManagementConfigurationService configurationService,
        ConfigPageState state)
        : base("配置", "运行时、重试、日志和配额行为的原生配置页。高级 YAML 保留为折叠式兜底能力。")
    {
        _configurationService = configurationService;
        State = state;
    }

    public ConfigPageState State { get; }

    public ManagementConfigSnapshot? Snapshot { get; private set; }

    public string ServerYaml { get; private set; } = string.Empty;

    public Task RefreshAsync()
    {
        return RunAsync("正在读取配置", ReloadFromServiceAsync);
    }

    public Task SaveAsync()
    {
        if (!State.TryCreateSavePayload(out var payload, out var validationError))
        {
            Error = validationError ?? string.Empty;
            Status = "请修正配置字段";
            return Task.CompletedTask;
        }

        if (!State.HasFieldChanges)
        {
            Error = string.Empty;
            Status = "当前没有需要保存的字段修改";
            return Task.CompletedTask;
        }

        return RunAsync("正在保存配置", async () =>
        {
            var changedFields = payload.ChangedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);

            await SaveBooleanSettingAsync(changedFields, "debug", payload.Debug);
            await SaveBooleanSettingAsync(changedFields, "usage-statistics-enabled", payload.UsageStatisticsEnabled);
            await SaveBooleanSettingAsync(changedFields, "request-log", payload.RequestLog);
            await SaveBooleanSettingAsync(changedFields, "logging-to-file", payload.LoggingToFile);
            await SaveBooleanSettingAsync(changedFields, "ws-auth", payload.WebSocketAuth);
            await SaveBooleanSettingAsync(changedFields, "force-model-prefix", payload.ForceModelPrefix);
            await SaveBooleanSettingAsync(changedFields, "quota-exceeded/switch-project", payload.SwitchProject);
            await SaveBooleanSettingAsync(changedFields, "quota-exceeded/switch-preview-model", payload.SwitchPreviewModel);

            await SaveIntegerSettingAsync(changedFields, "request-retry", payload.RequestRetry);
            await SaveIntegerSettingAsync(changedFields, "max-retry-interval", payload.MaxRetryInterval);
            await SaveIntegerSettingAsync(changedFields, "logs-max-total-size-mb", payload.LogsMaxTotalSizeMb);
            await SaveIntegerSettingAsync(changedFields, "error-logs-max-files", payload.ErrorLogsMaxFiles);

            await SaveStringSettingAsync(changedFields, "routing/strategy", payload.RoutingStrategy);
            await SaveStringSettingAsync(changedFields, "proxy-url", payload.ProxyUrl, deleteWhenEmpty: true);

            await ReloadFromServiceAsync();
        });
    }

    public Task SaveAdvancedYamlAsync()
    {
        if (State.HasFieldChanges)
        {
            Error = "字段草稿仍有未保存修改，请先保存或放弃字段修改。";
            Status = "高级 YAML 未应用";
            return Task.CompletedTask;
        }

        if (!State.HasYamlChanges)
        {
            Error = string.Empty;
            Status = "高级 YAML 没有变化";
            return Task.CompletedTask;
        }

        return RunAsync("正在应用高级 YAML", async () =>
        {
            await _configurationService.PutConfigYamlAsync(State.AdvancedYamlDraft);
            await ReloadFromServiceAsync();
        });
    }

    private async Task ReloadFromServiceAsync()
    {
        var snapshotTask = _configurationService.GetConfigAsync();
        var yamlTask = _configurationService.GetConfigYamlAsync();
        await Task.WhenAll(snapshotTask, yamlTask);

        Snapshot = snapshotTask.Result.Value;
        ServerYaml = yamlTask.Result.Value;
        State.LoadFromSnapshot(Snapshot, ServerYaml);
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(ServerYaml));
    }

    private async Task SaveBooleanSettingAsync(HashSet<string> changedFields, string path, bool value)
    {
        if (!changedFields.Contains(path))
        {
            return;
        }

        await _configurationService.UpdateBooleanSettingAsync(path, value);
    }

    private async Task SaveIntegerSettingAsync(HashSet<string> changedFields, string path, int? value)
    {
        if (!changedFields.Contains(path))
        {
            return;
        }

        if (value is int parsed)
        {
            await _configurationService.UpdateIntegerSettingAsync(path, parsed);
            return;
        }

        await _configurationService.DeleteSettingAsync(path);
    }

    private async Task SaveStringSettingAsync(
        HashSet<string> changedFields,
        string path,
        string value,
        bool deleteWhenEmpty = true)
    {
        if (!changedFields.Contains(path))
        {
            return;
        }

        if (deleteWhenEmpty && string.IsNullOrWhiteSpace(value))
        {
            await _configurationService.DeleteSettingAsync(path);
            return;
        }

        await _configurationService.UpdateStringSettingAsync(path, value);
    }
}

public sealed class AiProvidersPageViewModel : ManagementPageViewModel
{
    private readonly IManagementProvidersService _providersService;

    public AiProvidersPageViewModel(IManagementProvidersService providersService)
        : base("账号配置", "Gemini、Codex、Claude、Vertex、Ampcode 和 OpenAI 兼容配置")
    {
        _providersService = providersService;
    }

    public IReadOnlyList<ManagementGeminiKeyConfiguration> Gemini { get; private set; } = [];

    public IReadOnlyList<ManagementProviderKeyConfiguration> Codex { get; private set; } = [];

    public IReadOnlyList<ManagementProviderKeyConfiguration> Claude { get; private set; } = [];

    public IReadOnlyList<ManagementProviderKeyConfiguration> Vertex { get; private set; } = [];

    public IReadOnlyList<ManagementOpenAiCompatibilityEntry> OpenAi { get; private set; } = [];

    public ManagementAmpCodeConfiguration? AmpCode { get; private set; }

    public Task RefreshAsync()
    {
        return RunAsync("正在刷新提供商", async () =>
        {
            var geminiTask = _providersService.GetGeminiKeysAsync();
            var codexTask = _providersService.GetCodexKeysAsync();
            var claudeTask = _providersService.GetClaudeKeysAsync();
            var vertexTask = _providersService.GetVertexKeysAsync();
            var openAiTask = _providersService.GetOpenAiCompatibilityAsync();
            var ampCodeTask = _providersService.GetAmpCodeAsync();
            await Task.WhenAll(geminiTask, codexTask, claudeTask, vertexTask, openAiTask, ampCodeTask);

            Gemini = geminiTask.Result.Value;
            Codex = codexTask.Result.Value;
            Claude = claudeTask.Result.Value;
            Vertex = vertexTask.Result.Value;
            OpenAi = openAiTask.Result.Value;
            AmpCode = ampCodeTask.Result.Value;
            OnPropertyChanged(string.Empty);
        });
    }
}

public sealed class AuthFilesPageViewModel : ManagementPageViewModel
{
    private readonly IManagementAuthFilesService _authFilesService;
    private readonly IManagementOAuthService _oauthService;

    public AuthFilesPageViewModel(
        IManagementAuthFilesService authFilesService,
        IManagementOAuthService oauthService)
        : base("认证文件", "批量上传、删除、启停、筛选、模型查看、OAuth 排除和别名")
    {
        _authFilesService = authFilesService;
        _oauthService = oauthService;
    }

    public IReadOnlyList<ManagementAuthFileItem> Files { get; private set; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ExcludedModels { get; private set; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>> ModelAliases { get; private set; } =
        new Dictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>(StringComparer.OrdinalIgnoreCase);

    public Task RefreshAsync()
    {
        return RunAsync("正在刷新认证文件", async () =>
        {
            var filesTask = _authFilesService.GetAuthFilesAsync();
            var excludedTask = _oauthService.GetOAuthExcludedModelsAsync();
            var aliasesTask = _oauthService.GetOAuthModelAliasesAsync();
            await Task.WhenAll(filesTask, excludedTask, aliasesTask);

            Files = filesTask.Result.Value;
            ExcludedModels = excludedTask.Result.Value;
            ModelAliases = aliasesTask.Result.Value;
            OnPropertyChanged(string.Empty);
        });
    }

    public Task UploadAsync(IReadOnlyList<ManagementAuthFileUpload> files)
    {
        return RunAsync("正在上传认证文件", async () =>
        {
            await _authFilesService.UploadAuthFilesAsync(files);
            Files = (await _authFilesService.GetAuthFilesAsync()).Value;
            OnPropertyChanged(nameof(Files));
        });
    }

    public Task DeleteAsync(IReadOnlyList<string> names)
    {
        return RunAsync("正在删除认证文件", async () =>
        {
            await _authFilesService.DeleteAuthFilesAsync(names);
            Files = (await _authFilesService.GetAuthFilesAsync()).Value;
            OnPropertyChanged(nameof(Files));
        });
    }

    public Task SetDisabledAsync(string name, bool disabled)
    {
        return RunAsync("正在更新认证状态", async () =>
        {
            await _authFilesService.SetAuthFileDisabledAsync(name, disabled);
            Files = (await _authFilesService.GetAuthFilesAsync()).Value;
            OnPropertyChanged(nameof(Files));
        });
    }

    public Task<ManagementApiResponse<string>> DownloadAsync(string name)
    {
        return _authFilesService.DownloadAuthFileAsync(name);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetModelsAsync(string name)
    {
        return _authFilesService.GetAuthFileModelsAsync(name);
    }
}

public sealed class OAuthPageViewModel : ManagementPageViewModel
{
    private readonly IManagementOAuthService _oauthService;

    public OAuthPageViewModel(IManagementOAuthService oauthService)
        : base("OAuth", "启动授权、轮询状态、提交回调，并支持 Gemini 项目参数")
    {
        _oauthService = oauthService;
    }

    public Task<ManagementApiResponse<ManagementOAuthStartResponse>> StartAsync(string provider, string? projectId = null)
    {
        return _oauthService.GetOAuthStartAsync(provider, projectId);
    }

    public Task<ManagementApiResponse<ManagementOAuthStatus>> GetStatusAsync(string state)
    {
        return _oauthService.GetOAuthStatusAsync(state);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> SubmitCallbackAsync(string provider, string redirectUrl)
    {
        return _oauthService.SubmitOAuthCallbackAsync(provider, redirectUrl);
    }
}

public sealed class QuotaPageViewModel : ManagementPageViewModel
{
    private readonly IManagementQuotaService _quotaService;

    public QuotaPageViewModel(IManagementQuotaService quotaService)
        : base("配额", "配额耗尽后的项目切换、预览模型切换和当前用量摘要")
    {
        _quotaService = quotaService;
    }

    public ManagementConfigSnapshot? Config { get; private set; }

    public Task RefreshAsync()
    {
        return RunAsync("正在刷新配额", async () =>
        {
            Config = (await _quotaService.GetQuotaSettingsAsync()).Value;
            OnPropertyChanged(nameof(Config));
        });
    }

    public Task SetSwitchProjectAsync(bool enabled)
    {
        return RunAsync("正在保存配额设置", async () =>
        {
            await _quotaService.SetSwitchProjectAsync(enabled);
            Config = (await _quotaService.GetQuotaSettingsAsync()).Value;
            OnPropertyChanged(nameof(Config));
        });
    }

    public Task SetSwitchPreviewModelAsync(bool enabled)
    {
        return RunAsync("正在保存配额设置", async () =>
        {
            await _quotaService.SetSwitchPreviewModelAsync(enabled);
            Config = (await _quotaService.GetQuotaSettingsAsync()).Value;
            OnPropertyChanged(nameof(Config));
        });
    }
}

public sealed class UsagePageViewModel : ManagementPageViewModel
{
    private readonly IManagementUsageService _usageService;

    public UsagePageViewModel(IManagementUsageService usageService)
        : base("用量", "统计总览、趋势、导入导出和模型明细")
    {
        _usageService = usageService;
    }

    public ManagementUsageSnapshot? Snapshot { get; private set; }

    public Task RefreshAsync()
    {
        return RunAsync("正在刷新用量", async () =>
        {
            Snapshot = (await _usageService.GetUsageAsync()).Value;
            OnPropertyChanged(nameof(Snapshot));
        });
    }

    public Task<ManagementApiResponse<ManagementUsageExportPayload>> ExportAsync()
    {
        return _usageService.ExportUsageAsync();
    }

    public Task ImportAsync(ManagementUsageExportPayload payload)
    {
        return RunAsync("正在导入用量", async () =>
        {
            await _usageService.ImportUsageAsync(payload);
            Snapshot = (await _usageService.GetUsageAsync()).Value;
            OnPropertyChanged(nameof(Snapshot));
        });
    }
}

public sealed class LogsPageViewModel : ManagementPageViewModel
{
    private readonly IManagementLogsService _logsService;

    public LogsPageViewModel(IManagementLogsService logsService, LogsPageState state)
        : base("日志", "增量刷新、错误日志清单和页内请求编号检查。")
    {
        _logsService = logsService;
        State = state;
    }

    public LogsPageState State { get; }

    public ManagementLogsSnapshot? Snapshot { get; private set; }

    public IReadOnlyList<ManagementErrorLogFile> ErrorLogs { get; private set; } = [];

    public Task RefreshAsync(long after = 0)
    {
        return RunAsync("正在刷新日志", async () =>
        {
            await ReloadLogsAsync(after);
        });
    }

    public Task ClearAsync()
    {
        return RunAsync("正在清空日志", async () =>
        {
            await _logsService.ClearLogsAsync();
            await ReloadLogsAsync();
        });
    }

    public async Task<RequestLogLookupResult> LookupRequestLogAsync(string id)
    {
        try
        {
            var payload = await _logsService.GetRequestLogByIdAsync(id);
            return RequestLogLookupResult.Found(payload.Value);
        }
        catch (ManagementApiException exception) when (exception.StatusCode == 404)
        {
            return RequestLogLookupResult.NotFound($"未找到请求编号为 {id} 的日志。");
        }
        catch (Exception exception)
        {
            return RequestLogLookupResult.Failed(exception.Message);
        }
    }

    private async Task ReloadLogsAsync(long after = 0)
    {
        var logsTask = _logsService.GetLogsAsync(after, limit: 500);
        var errorTask = _logsService.GetRequestErrorLogsAsync();
        await Task.WhenAll(logsTask, errorTask);

        Snapshot = logsTask.Result.Value;
        ErrorLogs = errorTask.Result.Value;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class SystemPageViewModel : ManagementPageViewModel
{
    private readonly IManagementSystemService _systemService;
    private readonly IManagementSessionService _sessionService;
    private readonly IManagementAuthService _authService;
    private readonly IManagementConfigurationService _configurationService;

    public SystemPageViewModel(
        IManagementSystemService systemService,
        IManagementSessionService sessionService,
        IManagementAuthService authService,
        IManagementConfigurationService configurationService)
        : base("系统", "版本、模型列表、请求日志开关、官方链接和连接信息")
    {
        _systemService = systemService;
        _sessionService = sessionService;
        _authService = authService;
        _configurationService = configurationService;
    }

    public ManagementConnectionInfo? Connection { get; private set; }

    public ManagementLatestVersionInfo? LatestVersion { get; private set; }

    public IReadOnlyList<ManagementModelDescriptor> Models { get; private set; } = [];

    public ManagementConfigSnapshot? Config { get; private set; }

    public Task RefreshAsync()
    {
        return RunAsync("正在刷新系统", async () =>
        {
            Connection = await _sessionService.GetConnectionAsync();
            Config = (await _configurationService.GetConfigAsync()).Value;
            LatestVersion = await ReadLatestVersionAsync();
            Models = await ReadModelsAsync();
            OnPropertyChanged(string.Empty);
        });
    }

    public Task SetRequestLogAsync(bool enabled)
    {
        return RunAsync("正在保存请求日志开关", async () =>
        {
            await _configurationService.UpdateBooleanSettingAsync("request-log", enabled);
            Config = (await _configurationService.GetConfigAsync()).Value;
            OnPropertyChanged(nameof(Config));
        });
    }

    private async Task<ManagementLatestVersionInfo?> ReadLatestVersionAsync()
    {
        try
        {
            return (await _systemService.GetLatestVersionAsync()).Value;
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<ManagementModelDescriptor>> ReadModelsAsync()
    {
        try
        {
            var keys = (await _authService.GetApiKeysAsync()).Value;
            return (await _systemService.GetAvailableModelsAsync(keys.Count > 0 ? keys[0] : null)).Value;
        }
        catch
        {
            return [];
        }
    }
}
