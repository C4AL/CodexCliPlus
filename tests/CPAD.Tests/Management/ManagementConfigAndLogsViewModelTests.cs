using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using CPAD.Core.Abstractions.Management;
using CPAD.Core.Exceptions;
using CPAD.Core.Models.Management;
using CPAD.ViewModels.Pages;

namespace CPAD.Tests.Management;

public sealed class ManagementConfigAndLogsViewModelTests
{
    [Fact]
    public async Task ConfigSaveAsyncUsesAuditedSettingEndpointsAndDeletesClearedProxyUrl()
    {
        var service = new RecordingConfigurationService();
        var guard = new RecordingUnsavedChangesGuard();
        var state = new ConfigPageState(guard);
        var viewModel = new ConfigPageViewModel(service, state);

        await viewModel.RefreshAsync();

        state.Debug = true;
        state.UsageStatisticsEnabled = true;
        state.RequestLog = true;
        state.LoggingToFile = true;
        state.WebSocketAuth = true;
        state.ForceModelPrefix = true;
        state.SwitchProject = true;
        state.SwitchPreviewModel = true;
        state.RequestRetry = "7";
        state.MaxRetryInterval = "21";
        state.LogsMaxTotalSizeMb = "128";
        state.ErrorLogsMaxFiles = "9";
        state.RoutingStrategy = "fill-first";
        state.ProxyUrl = string.Empty;

        await viewModel.SaveAsync();

        Assert.Equal(
            [
                "debug",
                "usage-statistics-enabled",
                "request-log",
                "logging-to-file",
                "ws-auth",
                "force-model-prefix",
                "quota-exceeded/switch-project",
                "quota-exceeded/switch-preview-model"
            ],
            service.BooleanUpdates.Select(update => update.Path));

        Assert.Equal(
            [
                "request-retry",
                "max-retry-interval",
                "logs-max-total-size-mb",
                "error-logs-max-files"
            ],
            service.IntegerUpdates.Select(update => update.Path));

        Assert.Equal(
            ["routing/strategy"],
            service.StringUpdates.Select(update => update.Path));

        Assert.Equal(["proxy-url"], service.DeletedPaths);
        Assert.Empty(service.YamlPuts);
        Assert.False(state.HasAnyChanges);
        Assert.False(guard.HasUnsavedChanges);
    }

    [Fact]
    public async Task ConfigSaveAdvancedYamlAsyncUsesYamlEndpointWithoutFieldCalls()
    {
        var service = new RecordingConfigurationService();
        var guard = new RecordingUnsavedChangesGuard();
        var state = new ConfigPageState(guard);
        var viewModel = new ConfigPageViewModel(service, state);

        await viewModel.RefreshAsync();

        state.AdvancedYamlDraft = "request-retry: 11";

        await viewModel.SaveAdvancedYamlAsync();

        Assert.Equal(["request-retry: 11"], service.YamlPuts);
        Assert.Empty(service.BooleanUpdates);
        Assert.Empty(service.IntegerUpdates);
        Assert.Empty(service.StringUpdates);
        Assert.Empty(service.DeletedPaths);
        Assert.False(state.HasAnyChanges);
    }

    [Fact]
    public async Task LogsRefreshAndClearUseExistingLogsServiceEndpoints()
    {
        var service = new RecordingLogsService();
        var state = new LogsPageState();
        var viewModel = new LogsPageViewModel(service, state);

        await viewModel.RefreshAsync(after: 123);
        await viewModel.ClearAsync();

        Assert.Equal([(123L, 500), (0L, 500)], service.GetLogsCalls);
        Assert.Equal(2, service.ErrorLogCalls);
        Assert.Equal(1, service.ClearCalls);
        Assert.NotNull(viewModel.Snapshot);
        Assert.Single(viewModel.ErrorLogs);
    }

    [Fact]
    public async Task LogsLookupRequestLogAsyncReturnsInlineSafeNotFoundResultFor404()
    {
        var service = new RecordingLogsService();
        var state = new LogsPageState();
        var viewModel = new LogsPageViewModel(service, state);

        var result = await viewModel.LookupRequestLogAsync("missing-request");

        Assert.Equal(RequestLogLookupState.NotFound, result.State);
        Assert.Contains("missing-request", result.ErrorMessage, StringComparison.Ordinal);
    }

    private sealed class RecordingConfigurationService : IManagementConfigurationService
    {
        private bool? _debug;
        private string? _proxyUrl = "http://127.0.0.1:8888";
        private int? _requestRetry = 3;
        private int? _maxRetryInterval = 5;
        private bool? _usageStatisticsEnabled;
        private bool? _requestLog;
        private bool? _loggingToFile;
        private int? _logsMaxTotalSizeMb = 64;
        private int? _errorLogsMaxFiles = 4;
        private bool? _webSocketAuth;
        private bool? _forceModelPrefix;
        private string? _routingStrategy = "random";
        private bool? _switchProject;
        private bool? _switchPreviewModel;
        private string _yaml = "request-retry: 3";

        public List<(string Path, bool Value)> BooleanUpdates { get; } = [];

        public List<(string Path, int Value)> IntegerUpdates { get; } = [];

        public List<(string Path, string Value)> StringUpdates { get; } = [];

        public List<string> DeletedPaths { get; } = [];

        public List<string> YamlPuts { get; } = [];

        public Task<ManagementApiResponse<ManagementConfigSnapshot>> GetConfigAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Response(BuildSnapshot()));
        }

        public Task<ManagementApiResponse<string>> GetConfigYamlAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Response(_yaml));
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> PutConfigYamlAsync(string yamlContent, CancellationToken cancellationToken = default)
        {
            YamlPuts.Add(yamlContent);
            _yaml = yamlContent;

            var requestRetryMatch = Regex.Match(yamlContent, @"request-retry:\s*(\d+)", RegexOptions.CultureInvariant);
            if (requestRetryMatch.Success)
            {
                _requestRetry = int.Parse(requestRetryMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            return Task.FromResult(Response(new ManagementOperationResult { Success = true, Status = "ok" }));
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateBooleanSettingAsync(string path, bool value, CancellationToken cancellationToken = default)
        {
            BooleanUpdates.Add((path, value));

            switch (path)
            {
                case "debug":
                    _debug = value;
                    break;
                case "usage-statistics-enabled":
                    _usageStatisticsEnabled = value;
                    break;
                case "request-log":
                    _requestLog = value;
                    break;
                case "logging-to-file":
                    _loggingToFile = value;
                    break;
                case "ws-auth":
                    _webSocketAuth = value;
                    break;
                case "force-model-prefix":
                    _forceModelPrefix = value;
                    break;
                case "quota-exceeded/switch-project":
                    _switchProject = value;
                    break;
                case "quota-exceeded/switch-preview-model":
                    _switchPreviewModel = value;
                    break;
            }

            return Task.FromResult(Response(new ManagementOperationResult { Success = true, Status = "ok" }));
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateIntegerSettingAsync(string path, int value, CancellationToken cancellationToken = default)
        {
            IntegerUpdates.Add((path, value));

            switch (path)
            {
                case "request-retry":
                    _requestRetry = value;
                    break;
                case "max-retry-interval":
                    _maxRetryInterval = value;
                    break;
                case "logs-max-total-size-mb":
                    _logsMaxTotalSizeMb = value;
                    break;
                case "error-logs-max-files":
                    _errorLogsMaxFiles = value;
                    break;
            }

            return Task.FromResult(Response(new ManagementOperationResult { Success = true, Status = "ok" }));
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> UpdateStringSettingAsync(string path, string value, CancellationToken cancellationToken = default)
        {
            StringUpdates.Add((path, value));

            switch (path)
            {
                case "proxy-url":
                    _proxyUrl = value;
                    break;
                case "routing/strategy":
                    _routingStrategy = value;
                    break;
            }

            return Task.FromResult(Response(new ManagementOperationResult { Success = true, Status = "ok" }));
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> DeleteSettingAsync(string path, CancellationToken cancellationToken = default)
        {
            DeletedPaths.Add(path);

            switch (path)
            {
                case "proxy-url":
                    _proxyUrl = null;
                    break;
                case "routing/strategy":
                    _routingStrategy = null;
                    break;
                case "request-retry":
                    _requestRetry = null;
                    break;
                case "max-retry-interval":
                    _maxRetryInterval = null;
                    break;
                case "logs-max-total-size-mb":
                    _logsMaxTotalSizeMb = null;
                    break;
                case "error-logs-max-files":
                    _errorLogsMaxFiles = null;
                    break;
            }

            return Task.FromResult(Response(new ManagementOperationResult { Success = true, Status = "ok" }));
        }

        private ManagementConfigSnapshot BuildSnapshot()
        {
            return new ManagementConfigSnapshot
            {
                Debug = _debug,
                ProxyUrl = _proxyUrl,
                RequestRetry = _requestRetry,
                MaxRetryInterval = _maxRetryInterval,
                UsageStatisticsEnabled = _usageStatisticsEnabled,
                RequestLog = _requestLog,
                LoggingToFile = _loggingToFile,
                LogsMaxTotalSizeMb = _logsMaxTotalSizeMb,
                ErrorLogsMaxFiles = _errorLogsMaxFiles,
                WebSocketAuth = _webSocketAuth,
                ForceModelPrefix = _forceModelPrefix,
                RoutingStrategy = _routingStrategy,
                QuotaExceeded = new ManagementQuotaExceededSettings
                {
                    SwitchProject = _switchProject,
                    SwitchPreviewModel = _switchPreviewModel,
                    AntigravityCredits = true
                }
            };
        }
    }

    private sealed class RecordingLogsService : IManagementLogsService
    {
        public List<(long After, int Limit)> GetLogsCalls { get; } = [];

        public int ErrorLogCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public Task<ManagementApiResponse<ManagementLogsSnapshot>> GetLogsAsync(long after = 0, int limit = 0, CancellationToken cancellationToken = default)
        {
            GetLogsCalls.Add((after, limit));

            return Task.FromResult(Response(new ManagementLogsSnapshot
            {
                Lines = ["line-a", "line-b"],
                LineCount = 2,
                LatestTimestamp = after > 0 ? after + 1 : 1700000000
            }));
        }

        public Task<ManagementApiResponse<ManagementOperationResult>> ClearLogsAsync(CancellationToken cancellationToken = default)
        {
            ClearCalls++;
            return Task.FromResult(Response(new ManagementOperationResult { Success = true, Status = "ok" }));
        }

        public Task<ManagementApiResponse<IReadOnlyList<ManagementErrorLogFile>>> GetRequestErrorLogsAsync(CancellationToken cancellationToken = default)
        {
            ErrorLogCalls++;
            IReadOnlyList<ManagementErrorLogFile> payload =
            [
                new ManagementErrorLogFile
                {
                    Name = "request-error.log",
                    Size = 1024,
                    Modified = 1700000000
                }
            ];

            return Task.FromResult(Response(payload));
        }

        public Task<ManagementApiResponse<string>> GetRequestLogByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.Equals(id, "missing-request", StringComparison.Ordinal))
            {
                throw new ManagementApiException("missing", 404);
            }

            return Task.FromResult(Response($"request-log::{id}"));
        }
    }

    private sealed class RecordingUnsavedChangesGuard : IUnsavedChangesGuard
    {
        private readonly HashSet<string> _dirtyScopes = new(StringComparer.OrdinalIgnoreCase);

        public bool HasUnsavedChanges => _dirtyScopes.Count > 0;

        public string? DirtyScope => _dirtyScopes.FirstOrDefault();

        public void SetDirty(string scope, bool hasUnsavedChanges)
        {
            if (hasUnsavedChanges)
            {
                _dirtyScopes.Add(scope);
            }
            else
            {
                _dirtyScopes.Remove(scope);
            }
        }

        public void Clear()
        {
            _dirtyScopes.Clear();
        }

        public bool ConfirmLeave(string targetDescription)
        {
            _dirtyScopes.Clear();
            return true;
        }
    }

    private static ManagementApiResponse<T> Response<T>(T value)
    {
        return new ManagementApiResponse<T>
        {
            Value = value,
            Metadata = new ManagementServerMetadata
            {
                Version = "test"
            },
            StatusCode = HttpStatusCode.OK
        };
    }
}
