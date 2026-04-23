using System.Globalization;
using System.Text.Json;

using CPAD.Core.Models.Management;

namespace CPAD.Infrastructure.Management;

internal static class ManagementMappers
{
    public static ManagementOperationResult MapOperation(JsonElement root)
    {
        return new ManagementOperationResult
        {
            Status = ManagementJson.GetString(root, "status"),
            Message = ManagementJson.GetString(root, "message"),
            Success = ManagementJson.GetBoolean(root, "success"),
            Ok = ManagementJson.GetBoolean(root, "ok"),
            Added = ManagementJson.GetInt32(root, "added"),
            Skipped = ManagementJson.GetInt32(root, "skipped"),
            Uploaded = ManagementJson.GetInt32(root, "uploaded"),
            Deleted = ManagementJson.GetInt32(root, "deleted"),
            Removed = ManagementJson.GetInt32(root, "removed"),
            Changed = ManagementJson.GetStringList(root, "changed"),
            Files = ManagementJson.GetStringList(root, "files"),
            Failed = MapBatchFailures(root)
        };
    }

    public static IReadOnlyList<string> MapApiKeys(JsonElement root)
    {
        return ManagementJson.GetStringList(root, "api-keys", "apiKeys");
    }

    public static ManagementConfigSnapshot MapConfig(JsonElement root)
    {
        var quotaExceeded = ManagementJson.GetObject(root, "quota-exceeded", "quotaExceeded");
        var routing = ManagementJson.GetObject(root, "routing");
        var oauthExcluded = MapOAuthExcludedModels(root);
        var ampCodeObject = ManagementJson.GetObject(root, "ampcode");

        return new ManagementConfigSnapshot
        {
            Debug = ManagementJson.GetBoolean(root, "debug"),
            ProxyUrl = ManagementJson.GetString(root, "proxy-url", "proxyUrl"),
            RequestRetry = ManagementJson.GetInt32(root, "request-retry", "requestRetry"),
            MaxRetryInterval = ManagementJson.GetInt32(root, "max-retry-interval", "maxRetryInterval"),
            QuotaExceeded = quotaExceeded is null ? null : new ManagementQuotaExceededSettings
            {
                SwitchProject = ManagementJson.GetBoolean(quotaExceeded.Value, "switch-project", "switchProject"),
                SwitchPreviewModel = ManagementJson.GetBoolean(quotaExceeded.Value, "switch-preview-model", "switchPreviewModel"),
                AntigravityCredits = ManagementJson.GetBoolean(quotaExceeded.Value, "antigravity-credits", "antigravityCredits")
            },
            UsageStatisticsEnabled = ManagementJson.GetBoolean(root, "usage-statistics-enabled", "usageStatisticsEnabled"),
            RequestLog = ManagementJson.GetBoolean(root, "request-log", "requestLog"),
            LoggingToFile = ManagementJson.GetBoolean(root, "logging-to-file", "loggingToFile"),
            LogsMaxTotalSizeMb = ManagementJson.GetInt32(root, "logs-max-total-size-mb", "logsMaxTotalSizeMb"),
            ErrorLogsMaxFiles = ManagementJson.GetInt32(root, "error-logs-max-files", "errorLogsMaxFiles"),
            WebSocketAuth = ManagementJson.GetBoolean(root, "ws-auth", "wsAuth"),
            ForceModelPrefix = ManagementJson.GetBoolean(root, "force-model-prefix", "forceModelPrefix"),
            RoutingStrategy = routing is not null
                ? ManagementJson.GetString(routing.Value, "strategy")
                : ManagementJson.GetString(root, "routing-strategy", "routingStrategy"),
            ApiKeys = ManagementJson.GetStringList(root, "api-keys", "apiKeys"),
            GeminiApiKeys = MapProviderList<ManagementGeminiKeyConfiguration>(root, "gemini-api-key", MapGeminiKeyConfiguration),
            CodexApiKeys = MapProviderList<ManagementProviderKeyConfiguration>(root, "codex-api-key", MapProviderKeyConfiguration),
            ClaudeApiKeys = MapProviderList<ManagementProviderKeyConfiguration>(root, "claude-api-key", MapProviderKeyConfiguration),
            VertexApiKeys = MapProviderList<ManagementProviderKeyConfiguration>(root, "vertex-api-key", MapProviderKeyConfiguration),
            OpenAiCompatibility = MapProviderList(root, "openai-compatibility", MapOpenAiCompatibilityEntry),
            OAuthExcludedModels = oauthExcluded,
            AmpCode = ampCodeObject is null ? null : MapAmpCodeConfiguration(ampCodeObject.Value),
            RawJson = root.GetRawText()
        };
    }

    public static IReadOnlyList<ManagementAuthFileItem> MapAuthFiles(JsonElement root)
    {
        var files = new List<ManagementAuthFileItem>();
        var array = ManagementJson.GetArray(root, "files");
        if (array is null)
        {
            return files;
        }

        foreach (var item in array.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ManagementJson.GetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            files.Add(new ManagementAuthFileItem
            {
                Name = name,
                Id = ManagementJson.GetString(item, "id"),
                Type = ManagementJson.GetString(item, "type"),
                Provider = ManagementJson.GetString(item, "provider"),
                Label = ManagementJson.GetString(item, "label"),
                Email = ManagementJson.GetString(item, "email"),
                AccountType = ManagementJson.GetString(item, "account_type", "accountType"),
                Account = ManagementJson.GetString(item, "account"),
                AuthIndex = ManagementJson.GetString(item, "auth_index", "authIndex"),
                Size = ManagementJson.GetInt64(item, "size"),
                Disabled = ManagementJson.GetBoolean(item, "disabled") ?? false,
                Unavailable = ManagementJson.GetBoolean(item, "unavailable") ?? false,
                RuntimeOnly = ManagementJson.GetBoolean(item, "runtime_only", "runtimeOnly") ?? false,
                Status = ManagementJson.GetString(item, "status"),
                StatusMessage = ManagementJson.GetString(item, "status_message", "statusMessage"),
                Source = ManagementJson.GetString(item, "source"),
                Path = ManagementJson.GetString(item, "path"),
                CreatedAt = ManagementJson.GetDateTimeOffset(item, "created_at", "createdAt"),
                UpdatedAt = ManagementJson.GetDateTimeOffset(item, "updated_at", "modtime", "modified"),
                LastRefresh = ManagementJson.GetDateTimeOffset(item, "last_refresh", "lastRefresh"),
                NextRetryAfter = ManagementJson.GetDateTimeOffset(item, "next_retry_after", "nextRetryAfter")
            });
        }

        return files;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> MapOAuthExcludedModels(JsonElement root)
    {
        JsonElement source;
        if (ManagementJson.TryGetProperty(root, out var wrapped, "oauth-excluded-models", "oauthExcludedModels"))
        {
            source = wrapped;
        }
        else
        {
            source = root;
        }

        if (source.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in source.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var items = property.Value.EnumerateArray()
                .Select(ManagementJson.AsString)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            result[property.Name] = items;
        }

        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>> MapOAuthModelAliases(JsonElement root)
    {
        JsonElement source;
        if (ManagementJson.TryGetProperty(root, out var wrapped, "oauth-model-alias", "oauthModelAlias"))
        {
            source = wrapped;
        }
        else
        {
            source = root;
        }

        if (source.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in source.EnumerateObject())
        {
            if (channel.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var entries = new List<ManagementOAuthModelAliasEntry>();
            foreach (var item in channel.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ManagementJson.GetString(item, "name");
                var alias = ManagementJson.GetString(item, "alias");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                entries.Add(new ManagementOAuthModelAliasEntry
                {
                    Name = name,
                    Alias = alias,
                    Fork = ManagementJson.GetBoolean(item, "fork") ?? false
                });
            }

            result[channel.Name] = entries;
        }

        return result;
    }

    public static IReadOnlyList<ManagementModelDescriptor> MapModelDescriptors(JsonElement root)
    {
        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (ManagementJson.TryGetProperty(root, out var wrapped, "data", "models", "items") && wrapped.ValueKind == JsonValueKind.Array)
        {
            array = wrapped;
        }
        else
        {
            return [];
        }

        var models = new List<ManagementModelDescriptor>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ManagementJson.GetString(item, "id", "name");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            models.Add(new ManagementModelDescriptor
            {
                Id = id,
                DisplayName = ManagementJson.GetString(item, "display_name", "displayName"),
                Type = ManagementJson.GetString(item, "type", "object"),
                OwnedBy = ManagementJson.GetString(item, "owned_by", "ownedBy"),
                Alias = ManagementJson.GetString(item, "alias"),
                Description = ManagementJson.GetString(item, "description")
            });
        }

        return models;
    }

    public static ManagementOAuthStartResponse MapOAuthStart(JsonElement root)
    {
        return new ManagementOAuthStartResponse
        {
            Url = ManagementJson.GetString(root, "url") ?? string.Empty,
            State = ManagementJson.GetString(root, "state")
        };
    }

    public static ManagementOAuthStatus MapOAuthStatus(JsonElement root)
    {
        return new ManagementOAuthStatus
        {
            Status = ManagementJson.GetString(root, "status") ?? string.Empty,
            Error = ManagementJson.GetString(root, "error")
        };
    }

    public static ManagementUsageSnapshot MapUsage(JsonElement root)
    {
        var usageRoot = ManagementJson.GetObject(root, "usage") ?? root;
        var apis = new Dictionary<string, ManagementUsageApiSnapshot>(StringComparer.OrdinalIgnoreCase);
        var apiObject = ManagementJson.GetObject(usageRoot, "apis");
        if (apiObject is not null)
        {
            foreach (var apiEntry in apiObject.Value.EnumerateObject())
            {
                var models = new Dictionary<string, ManagementUsageModelSnapshot>(StringComparer.OrdinalIgnoreCase);
                var modelsObject = ManagementJson.GetObject(apiEntry.Value, "models");
                if (modelsObject is not null)
                {
                    foreach (var modelEntry in modelsObject.Value.EnumerateObject())
                    {
                        var details = new List<ManagementUsageRequestDetail>();
                        var detailArray = ManagementJson.GetArray(modelEntry.Value, "details");
                        if (detailArray is not null)
                        {
                            foreach (var detail in detailArray.Value.EnumerateArray())
                            {
                                var tokens = ManagementJson.GetObject(detail, "tokens");
                                details.Add(new ManagementUsageRequestDetail
                                {
                                    Timestamp = ManagementJson.GetDateTimeOffset(detail, "timestamp"),
                                    Source = ManagementJson.GetString(detail, "source"),
                                    AuthIndex = ManagementJson.GetString(detail, "auth_index", "authIndex"),
                                    LatencyMs = ManagementJson.GetInt64(detail, "latency_ms", "latencyMs"),
                                    Failed = ManagementJson.GetBoolean(detail, "failed") ?? false,
                                    Tokens = tokens is null ? new ManagementUsageTokenStats() : new ManagementUsageTokenStats
                                    {
                                        InputTokens = ManagementJson.GetInt64(tokens.Value, "input_tokens", "inputTokens") ?? 0,
                                        OutputTokens = ManagementJson.GetInt64(tokens.Value, "output_tokens", "outputTokens") ?? 0,
                                        ReasoningTokens = ManagementJson.GetInt64(tokens.Value, "reasoning_tokens", "reasoningTokens") ?? 0,
                                        CachedTokens = ManagementJson.GetInt64(tokens.Value, "cached_tokens", "cachedTokens") ?? 0,
                                        TotalTokens = ManagementJson.GetInt64(tokens.Value, "total_tokens", "totalTokens") ?? 0
                                    }
                                });
                            }
                        }

                        models[modelEntry.Name] = new ManagementUsageModelSnapshot
                        {
                            TotalRequests = ManagementJson.GetInt64(modelEntry.Value, "total_requests", "totalRequests") ?? 0,
                            TotalTokens = ManagementJson.GetInt64(modelEntry.Value, "total_tokens", "totalTokens") ?? 0,
                            Details = details
                        };
                    }
                }

                apis[apiEntry.Name] = new ManagementUsageApiSnapshot
                {
                    TotalRequests = ManagementJson.GetInt64(apiEntry.Value, "total_requests", "totalRequests") ?? 0,
                    TotalTokens = ManagementJson.GetInt64(apiEntry.Value, "total_tokens", "totalTokens") ?? 0,
                    Models = models
                };
            }
        }

        return new ManagementUsageSnapshot
        {
            TotalRequests = ManagementJson.GetInt64(usageRoot, "total_requests", "totalRequests") ?? 0,
            SuccessCount = ManagementJson.GetInt64(usageRoot, "success_count", "successCount") ?? 0,
            FailureCount = ManagementJson.GetInt64(usageRoot, "failure_count", "failureCount") ?? 0,
            TotalTokens = ManagementJson.GetInt64(usageRoot, "total_tokens", "totalTokens") ?? 0,
            Apis = apis,
            RequestsByDay = ManagementJson.GetObject(usageRoot, "requests_by_day", "requestsByDay") is { } requestsByDay
                ? ManagementJson.GetLongDictionary(requestsByDay)
                : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            RequestsByHour = ManagementJson.GetObject(usageRoot, "requests_by_hour", "requestsByHour") is { } requestsByHour
                ? ManagementJson.GetLongDictionary(requestsByHour)
                : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            TokensByDay = ManagementJson.GetObject(usageRoot, "tokens_by_day", "tokensByDay") is { } tokensByDay
                ? ManagementJson.GetLongDictionary(tokensByDay)
                : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            TokensByHour = ManagementJson.GetObject(usageRoot, "tokens_by_hour", "tokensByHour") is { } tokensByHour
                ? ManagementJson.GetLongDictionary(tokensByHour)
                : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        };
    }

    public static ManagementUsageExportPayload MapUsageExport(JsonElement root)
    {
        return new ManagementUsageExportPayload
        {
            Version = ManagementJson.GetInt32(root, "version") ?? 0,
            ExportedAt = ManagementJson.GetDateTimeOffset(root, "exported_at", "exportedAt"),
            Usage = MapUsage(root)
        };
    }

    public static ManagementUsageImportResult MapUsageImportResult(JsonElement root)
    {
        return new ManagementUsageImportResult
        {
            Added = ManagementJson.GetInt32(root, "added"),
            Skipped = ManagementJson.GetInt32(root, "skipped"),
            TotalRequests = ManagementJson.GetInt64(root, "total_requests", "totalRequests"),
            FailedRequests = ManagementJson.GetInt64(root, "failed_requests", "failedRequests")
        };
    }

    public static ManagementLogsSnapshot MapLogs(JsonElement root)
    {
        return new ManagementLogsSnapshot
        {
            Lines = ManagementJson.GetStringList(root, "lines"),
            LineCount = ManagementJson.GetInt32(root, "line-count", "lineCount") ?? 0,
            LatestTimestamp = ManagementJson.GetInt64(root, "latest-timestamp", "latestTimestamp") ?? 0
        };
    }

    public static IReadOnlyList<ManagementErrorLogFile> MapErrorLogs(JsonElement root)
    {
        var array = ManagementJson.GetArray(root, "files");
        if (array is null)
        {
            return [];
        }

        return array.Value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ManagementErrorLogFile
            {
                Name = ManagementJson.GetString(item, "name") ?? string.Empty,
                Size = ManagementJson.GetInt64(item, "size"),
                Modified = ManagementJson.GetInt64(item, "modified")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();
    }

    public static ManagementLatestVersionInfo MapLatestVersion(JsonElement root)
    {
        return new ManagementLatestVersionInfo
        {
            LatestVersion = ManagementJson.GetString(root, "latest-version", "latestVersion", "latest") ?? string.Empty
        };
    }

    public static ManagementApiCallResult MapApiCallResult(JsonElement root)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (ManagementJson.GetObject(root, "header", "headers") is { } headerObject)
        {
            foreach (var property in headerObject.EnumerateObject())
            {
                headers[property.Name] = property.Value.ValueKind == JsonValueKind.Array
                    ? property.Value.EnumerateArray()
                        .Select(ManagementJson.AsString)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!)
                        .ToArray()
                    : [];
            }
        }

        var bodyText = string.Empty;
        if (ManagementJson.TryGetProperty(root, out var body, "body"))
        {
            bodyText = body.ValueKind switch
            {
                JsonValueKind.String => body.GetString() ?? string.Empty,
                JsonValueKind.Undefined or JsonValueKind.Null => string.Empty,
                _ => body.GetRawText()
            };
        }

        return new ManagementApiCallResult
        {
            StatusCode = ManagementJson.GetInt32(root, "status_code", "statusCode") ?? 0,
            Headers = headers,
            BodyText = bodyText
        };
    }

    public static object ToUsageImportPayload(ManagementUsageExportPayload payload)
    {
        return new Dictionary<string, object?>
        {
            ["version"] = payload.Version,
            ["usage"] = ToUsageSnapshotPayload(payload.Usage)
        };
    }

    private static ManagementAmpCodeConfiguration MapAmpCodeConfiguration(JsonElement element)
    {
        var modelMappings = ManagementJson.GetArray(element, "model-mappings", "modelMappings");
        var upstreamApiKeys = ManagementJson.GetArray(element, "upstream-api-keys", "upstreamApiKeys");

        return new ManagementAmpCodeConfiguration
        {
            UpstreamUrl = ManagementJson.GetString(element, "upstream-url", "upstreamUrl"),
            UpstreamApiKey = ManagementJson.GetString(element, "upstream-api-key", "upstreamApiKey"),
            ForceModelMappings = ManagementJson.GetBoolean(element, "force-model-mappings", "forceModelMappings"),
            ModelMappings = modelMappings is null
                ? []
                : modelMappings.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => new ManagementAmpCodeModelMapping
                    {
                        From = ManagementJson.GetString(item, "from") ?? string.Empty,
                        To = ManagementJson.GetString(item, "to") ?? string.Empty
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.From) && !string.IsNullOrWhiteSpace(item.To))
                    .ToArray(),
            UpstreamApiKeys = upstreamApiKeys is null
                ? []
                : upstreamApiKeys.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => new ManagementAmpCodeUpstreamApiKeyMapping
                    {
                        UpstreamApiKey = ManagementJson.GetString(item, "upstream-api-key", "upstreamApiKey") ?? string.Empty,
                        ApiKeys = ManagementJson.GetStringList(item, "api-keys", "apiKeys")
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.UpstreamApiKey))
                    .ToArray()
        };
    }

    private static T MapProviderBase<T>(JsonElement item, Func<ManagementProviderKeyConfiguration, T> factory)
    {
        var apiKey = ManagementJson.GetString(item, "api-key", "apiKey");
        var headers = ManagementJson.GetObject(item, "headers") is { } headerObject
            ? ManagementJson.GetStringDictionary(headerObject)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var configuration = new ManagementProviderKeyConfiguration
        {
            ApiKey = apiKey ?? string.Empty,
            Priority = ManagementJson.GetInt32(item, "priority"),
            Prefix = ManagementJson.GetString(item, "prefix"),
            BaseUrl = ManagementJson.GetString(item, "base-url", "baseUrl"),
            WebSockets = ManagementJson.GetBoolean(item, "websockets", "webSockets"),
            ProxyUrl = ManagementJson.GetString(item, "proxy-url", "proxyUrl"),
            Headers = headers,
            Models = MapModelAliases(item),
            ExcludedModels = ManagementJson.GetStringList(item, "excluded-models", "excludedModels"),
            Cloak = ManagementJson.GetObject(item, "cloak") is { } cloakObject
                ? new ManagementCloakConfiguration
                {
                    Mode = ManagementJson.GetString(cloakObject, "mode"),
                    StrictMode = ManagementJson.GetBoolean(cloakObject, "strict-mode", "strictMode"),
                    SensitiveWords = ManagementJson.GetStringList(cloakObject, "sensitive-words", "sensitiveWords")
                }
                : null,
            AuthIndex = ManagementJson.GetString(item, "auth-index", "authIndex")
        };

        return factory(configuration);
    }

    private static ManagementGeminiKeyConfiguration MapGeminiKeyConfiguration(JsonElement item)
    {
        return MapProviderBase(item, baseConfig => new ManagementGeminiKeyConfiguration
        {
            ApiKey = baseConfig.ApiKey,
            Priority = baseConfig.Priority,
            Prefix = baseConfig.Prefix,
            BaseUrl = baseConfig.BaseUrl,
            WebSockets = baseConfig.WebSockets,
            ProxyUrl = baseConfig.ProxyUrl,
            Headers = baseConfig.Headers,
            Models = baseConfig.Models,
            ExcludedModels = baseConfig.ExcludedModels,
            Cloak = baseConfig.Cloak,
            AuthIndex = baseConfig.AuthIndex
        });
    }

    private static ManagementProviderKeyConfiguration MapProviderKeyConfiguration(JsonElement item)
    {
        return MapProviderBase(item, baseConfig => baseConfig);
    }

    private static ManagementOpenAiCompatibilityEntry MapOpenAiCompatibilityEntry(JsonElement item)
    {
        var headers = ManagementJson.GetObject(item, "headers") is { } headerObject
            ? ManagementJson.GetStringDictionary(headerObject)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var apiKeyEntries = new List<ManagementApiKeyEntry>();
        if (ManagementJson.GetArray(item, "api-key-entries", "apiKeyEntries") is { } apiEntryArray)
        {
            foreach (var apiEntry in apiEntryArray.EnumerateArray())
            {
                if (apiEntry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var apiKey = ManagementJson.GetString(apiEntry, "api-key", "apiKey");
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    continue;
                }

                apiKeyEntries.Add(new ManagementApiKeyEntry
                {
                    ApiKey = apiKey,
                    ProxyUrl = ManagementJson.GetString(apiEntry, "proxy-url", "proxyUrl"),
                    Headers = ManagementJson.GetObject(apiEntry, "headers") is { } apiHeaders
                        ? ManagementJson.GetStringDictionary(apiHeaders)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    AuthIndex = ManagementJson.GetString(apiEntry, "auth-index", "authIndex")
                });
            }
        }

        return new ManagementOpenAiCompatibilityEntry
        {
            Name = ManagementJson.GetString(item, "name") ?? string.Empty,
            Prefix = ManagementJson.GetString(item, "prefix"),
            BaseUrl = ManagementJson.GetString(item, "base-url", "baseUrl") ?? string.Empty,
            ApiKeyEntries = apiKeyEntries,
            Headers = headers,
            Models = MapModelAliases(item),
            Priority = ManagementJson.GetInt32(item, "priority"),
            TestModel = ManagementJson.GetString(item, "test-model", "testModel"),
            AuthIndex = ManagementJson.GetString(item, "auth-index", "authIndex")
        };
    }

    private static IReadOnlyList<ManagementModelAlias> MapModelAliases(JsonElement item)
    {
        if (ManagementJson.GetArray(item, "models") is not { } modelArray)
        {
            return [];
        }

        return modelArray.EnumerateArray()
            .Select(model =>
            {
                var name = ManagementJson.GetString(model, "name", "id", "model");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                return new ManagementModelAlias
                {
                    Name = name,
                    Alias = ManagementJson.GetString(model, "alias", "display_name", "displayName"),
                    Priority = ManagementJson.GetInt32(model, "priority"),
                    TestModel = ManagementJson.GetString(model, "test-model", "testModel")
                };
            })
            .Where(model => model is not null)
            .Select(model => model!)
            .ToArray();
    }

    private static IReadOnlyList<T> MapProviderList<T>(
        JsonElement root,
        string propertyName,
        Func<JsonElement, T> factory)
    {
        if (ManagementJson.GetArray(root, propertyName) is not { } array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(factory)
            .ToArray();
    }

    private static IReadOnlyList<ManagementBatchFailure> MapBatchFailures(JsonElement root)
    {
        if (ManagementJson.GetArray(root, "failed") is not { } array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ManagementBatchFailure
            {
                Name = ManagementJson.GetString(item, "name") ?? string.Empty,
                Error = ManagementJson.GetString(item, "error", "message") ?? string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Error))
            .ToArray();
    }

    private static object ToUsageSnapshotPayload(ManagementUsageSnapshot snapshot)
    {
        return new Dictionary<string, object?>
        {
            ["total_requests"] = snapshot.TotalRequests,
            ["success_count"] = snapshot.SuccessCount,
            ["failure_count"] = snapshot.FailureCount,
            ["total_tokens"] = snapshot.TotalTokens,
            ["apis"] = snapshot.Apis.ToDictionary(
                pair => pair.Key,
                pair => (object)ToUsageApiSnapshotPayload(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            ["requests_by_day"] = snapshot.RequestsByDay,
            ["requests_by_hour"] = snapshot.RequestsByHour,
            ["tokens_by_day"] = snapshot.TokensByDay,
            ["tokens_by_hour"] = snapshot.TokensByHour
        };
    }

    private static object ToUsageApiSnapshotPayload(ManagementUsageApiSnapshot snapshot)
    {
        return new Dictionary<string, object?>
        {
            ["total_requests"] = snapshot.TotalRequests,
            ["total_tokens"] = snapshot.TotalTokens,
            ["models"] = snapshot.Models.ToDictionary(
                pair => pair.Key,
                pair => (object)ToUsageModelSnapshotPayload(pair.Value),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static object ToUsageModelSnapshotPayload(ManagementUsageModelSnapshot snapshot)
    {
        return new Dictionary<string, object?>
        {
            ["total_requests"] = snapshot.TotalRequests,
            ["total_tokens"] = snapshot.TotalTokens,
            ["details"] = snapshot.Details.Select(detail => new Dictionary<string, object?>
            {
                ["timestamp"] = detail.Timestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                ["source"] = detail.Source,
                ["auth_index"] = detail.AuthIndex,
                ["latency_ms"] = detail.LatencyMs,
                ["failed"] = detail.Failed,
                ["tokens"] = new Dictionary<string, object?>
                {
                    ["input_tokens"] = detail.Tokens.InputTokens,
                    ["output_tokens"] = detail.Tokens.OutputTokens,
                    ["reasoning_tokens"] = detail.Tokens.ReasoningTokens,
                    ["cached_tokens"] = detail.Tokens.CachedTokens,
                    ["total_tokens"] = detail.Tokens.TotalTokens
                }
            }).ToArray()
        };
    }
}
