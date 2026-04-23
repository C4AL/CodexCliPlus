using System.Net;
using System.Text.Json;

using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;

namespace CPAD.Infrastructure.Management;

public sealed class ManagementAuthService : IManagementAuthService
{
    private static readonly HashSet<string> WebUiSupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "codex",
        "anthropic",
        "antigravity",
        "gemini-cli"
    };

    private readonly IManagementApiClient _apiClient;

    public ManagementAuthService(IManagementApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<ManagementApiResponse<IReadOnlyList<string>>> GetApiKeysAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "api-keys", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapApiKeys(document.RootElement));
    }

    public async Task<ManagementApiResponse<ManagementOperationResult>> ReplaceApiKeysAsync(
        IReadOnlyList<string> apiKeys,
        CancellationToken cancellationToken = default)
    {
        var normalizedApiKeys = apiKeys
            .Select(key => key.Trim())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return await SendOperationAsync(
            HttpMethod.Put,
            "api-keys",
            normalizedApiKeys,
            cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateApiKeyAsync(
        int index,
        string value,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Patch,
            "api-keys",
            new Dictionary<string, object?>
            {
                ["index"] = index,
                ["value"] = value.Trim()
            },
            cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteApiKeyAsync(
        int index,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Delete,
            $"api-keys?index={index}",
            payload: null,
            cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementAuthFileItem>>> GetAuthFilesAsync(CancellationToken cancellationToken = default)
    {
        return GetMappedAsync("auth-files", ManagementMappers.MapAuthFiles, cancellationToken);
    }

    public async Task<ManagementApiResponse<ManagementOperationResult>> UploadAuthFileAsync(
        string fileName,
        string jsonContent,
        CancellationToken cancellationToken = default)
    {
        var normalizedFileName = NormalizeFileName(fileName);
        return await SendOperationAsync(
            HttpMethod.Post,
            $"auth-files?name={Uri.EscapeDataString(normalizedFileName)}",
            rawBody: jsonContent,
            cancellationToken: cancellationToken);
    }

    public async Task<ManagementApiResponse<ManagementOperationResult>> UploadAuthFilesAsync(
        IReadOnlyList<ManagementAuthFileUpload> files,
        CancellationToken cancellationToken = default)
    {
        var validFiles = files
            .Where(file => file.Content.Length > 0 && !string.IsNullOrWhiteSpace(file.FileName))
            .Select(file => new ManagementMultipartFile
            {
                FieldName = "file",
                FileName = NormalizeFileName(file.FileName),
                Content = file.Content,
                ContentType = file.ContentType
            })
            .ToArray();

        if (validFiles.Length == 0)
        {
            return CreateOperationResponse(new ManagementOperationResult
            {
                Status = "ok",
                Uploaded = 0
            });
        }

        var response = await _apiClient.SendManagementMultipartAsync(
            HttpMethod.Post,
            "auth-files",
            validFiles,
            cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOperation(document.RootElement));
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteAuthFileAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Delete,
            $"auth-files?name={Uri.EscapeDataString(name.Trim())}",
            payload: null,
            cancellationToken);
    }

    public async Task<ManagementApiResponse<ManagementOperationResult>> DeleteAuthFilesAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default)
    {
        var normalizedNames = names
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedNames.Length == 0)
        {
            return CreateOperationResponse(new ManagementOperationResult
            {
                Status = "ok",
                Deleted = 0
            });
        }

        var query = string.Join("&", normalizedNames.Select(name => $"name={Uri.EscapeDataString(name)}"));
        return await SendOperationAsync(HttpMethod.Delete, $"auth-files?{query}", payload: null, cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteAllAuthFilesAsync(CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Delete, "auth-files?all=true", payload: null, cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<string>> DownloadAuthFileAsync(string name, CancellationToken cancellationToken = default)
    {
        return _apiClient.SendManagementAsync(
            HttpMethod.Get,
            $"auth-files/download?name={Uri.EscapeDataString(name)}",
            accept: "*/*",
            cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> SetAuthFileDisabledAsync(
        string name,
        bool disabled,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Patch,
            "auth-files/status",
            new Dictionary<string, object?>
            {
                ["name"] = name.Trim(),
                ["disabled"] = disabled
            },
            cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> PatchAuthFileFieldsAsync(
        ManagementAuthFileFieldPatch patch,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = patch.Name.Trim()
        };

        if (patch.Prefix is not null)
        {
            payload["prefix"] = patch.Prefix;
        }

        if (patch.ProxyUrl is not null)
        {
            payload["proxy_url"] = patch.ProxyUrl;
        }

        if (patch.Headers.Count > 0)
        {
            payload["headers"] = patch.Headers;
        }

        return SendOperationAsync(HttpMethod.Patch, "auth-files/fields", payload, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementGeminiKeyConfiguration>>> GetGeminiKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetMappedAsync("gemini-api-key", root => ManagementMappers.MapConfig(root).GeminiApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetCodexKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetMappedAsync("codex-api-key", root => ManagementMappers.MapConfig(root).CodexApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetClaudeKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetMappedAsync("claude-api-key", root => ManagementMappers.MapConfig(root).ClaudeApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetVertexKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetMappedAsync("vertex-api-key", root => ManagementMappers.MapConfig(root).VertexApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementOpenAiCompatibilityEntry>>> GetOpenAiCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        return GetMappedAsync("openai-compatibility", root => ManagementMappers.MapConfig(root).OpenAiCompatibility, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceGeminiKeysAsync(
        IReadOnlyList<ManagementGeminiKeyConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Put, "gemini-api-key", configurations, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateGeminiKeyAsync(
        int index,
        ManagementGeminiKeyConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Patch, "gemini-api-key", BuildIndexedPayload(index, configuration), cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteGeminiKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Delete, BuildProviderDeletePath("gemini-api-key", apiKey, baseUrl), payload: null, cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceCodexKeysAsync(
        IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Put, "codex-api-key", configurations, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateCodexKeyAsync(
        int index,
        ManagementProviderKeyConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Patch, "codex-api-key", BuildIndexedPayload(index, configuration), cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteCodexKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Delete, BuildProviderDeletePath("codex-api-key", apiKey, baseUrl), payload: null, cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceClaudeKeysAsync(
        IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Put, "claude-api-key", configurations, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateClaudeKeyAsync(
        int index,
        ManagementProviderKeyConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Patch, "claude-api-key", BuildIndexedPayload(index, configuration), cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteClaudeKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Delete, BuildProviderDeletePath("claude-api-key", apiKey, baseUrl), payload: null, cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceVertexKeysAsync(
        IReadOnlyList<ManagementProviderKeyConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Put, "vertex-api-key", configurations, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateVertexKeyAsync(
        int index,
        ManagementProviderKeyConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Patch, "vertex-api-key", BuildIndexedPayload(index, configuration), cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteVertexKeyAsync(
        string apiKey,
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Delete, BuildProviderDeletePath("vertex-api-key", apiKey, baseUrl), payload: null, cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceOpenAiCompatibilityAsync(
        IReadOnlyList<ManagementOpenAiCompatibilityEntry> providers,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Put, "openai-compatibility", providers, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateOpenAiCompatibilityAsync(
        int index,
        ManagementOpenAiCompatibilityEntry provider,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(HttpMethod.Patch, "openai-compatibility", BuildIndexedPayload(index, provider), cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteOpenAiCompatibilityAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Delete,
            $"openai-compatibility?name={Uri.EscapeDataString(name.Trim())}",
            payload: null,
            cancellationToken);
    }

    public async Task<ManagementApiResponse<ManagementAmpCodeConfiguration>> GetAmpCodeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "ampcode", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        var ampCode = ManagementMappers.MapConfig(document.RootElement).AmpCode ?? new ManagementAmpCodeConfiguration();
        return ManagementResponseFactory.Map(response, ampCode);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateAmpUpstreamUrlAsync(
        string? upstreamUrl,
        CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(upstreamUrl)
            ? SendOperationAsync(HttpMethod.Delete, "ampcode/upstream-url", payload: null, cancellationToken: cancellationToken)
            : SendOperationAsync(HttpMethod.Put, "ampcode/upstream-url", new Dictionary<string, object?> { ["value"] = upstreamUrl.Trim() }, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateAmpUpstreamApiKeyAsync(
        string? upstreamApiKey,
        CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(upstreamApiKey)
            ? SendOperationAsync(HttpMethod.Delete, "ampcode/upstream-api-key", payload: null, cancellationToken: cancellationToken)
            : SendOperationAsync(HttpMethod.Put, "ampcode/upstream-api-key", new Dictionary<string, object?> { ["value"] = upstreamApiKey.Trim() }, cancellationToken);
    }

    public async Task<ManagementApiResponse<IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping>>> GetAmpUpstreamApiKeysAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "ampcode/upstream-api-keys", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        var array = ManagementJson.GetArray(document.RootElement, "upstream-api-keys", "upstreamApiKeys");
        var mappings = array is null
            ? Array.Empty<ManagementAmpCodeUpstreamApiKeyMapping>()
            : array.Value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ManagementAmpCodeUpstreamApiKeyMapping
                {
                    UpstreamApiKey = ManagementJson.GetString(item, "upstream-api-key", "upstreamApiKey") ?? string.Empty,
                    ApiKeys = ManagementJson.GetStringList(item, "api-keys", "apiKeys")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.UpstreamApiKey))
                .ToArray();

        return ManagementResponseFactory.Map(response, (IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping>)mappings);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceAmpUpstreamApiKeysAsync(
        IReadOnlyList<ManagementAmpCodeUpstreamApiKeyMapping> mappings,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Put,
            "ampcode/upstream-api-keys",
            new Dictionary<string, object?> { ["value"] = mappings },
            cancellationToken);
    }

    public async Task<ManagementApiResponse<IReadOnlyList<ManagementAmpCodeModelMapping>>> GetAmpModelMappingsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "ampcode/model-mappings", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        var array = ManagementJson.GetArray(document.RootElement, "model-mappings", "modelMappings");
        var mappings = array is null
            ? Array.Empty<ManagementAmpCodeModelMapping>()
            : array.Value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ManagementAmpCodeModelMapping
                {
                    From = ManagementJson.GetString(item, "from") ?? string.Empty,
                    To = ManagementJson.GetString(item, "to") ?? string.Empty
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.From) && !string.IsNullOrWhiteSpace(item.To))
                .ToArray();

        return ManagementResponseFactory.Map(response, (IReadOnlyList<ManagementAmpCodeModelMapping>)mappings);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceAmpModelMappingsAsync(
        IReadOnlyList<ManagementAmpCodeModelMapping> mappings,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Put,
            "ampcode/model-mappings",
            new Dictionary<string, object?> { ["value"] = mappings },
            cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> SetAmpForceModelMappingsAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Put,
            "ampcode/force-model-mappings",
            new Dictionary<string, object?> { ["value"] = enabled },
            cancellationToken);
    }

    public async Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<string>>>> GetOAuthExcludedModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "oauth-excluded-models", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOAuthExcludedModels(document.RootElement));
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceOAuthExcludedModelsAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> excludedModels,
        CancellationToken cancellationToken = default)
    {
        var normalized = excludedModels.ToDictionary(
            pair => pair.Key.Trim().ToLowerInvariant(),
            pair => (IReadOnlyList<string>)pair.Value
                .Select(model => model.Trim())
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        return SendOperationAsync(HttpMethod.Put, "oauth-excluded-models", normalized, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateOAuthExcludedModelsAsync(
        string provider,
        IReadOnlyList<string> models,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Patch,
            "oauth-excluded-models",
            new Dictionary<string, object?>
            {
                ["provider"] = provider.Trim().ToLowerInvariant(),
                ["models"] = models
            },
            cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteOAuthExcludedModelsAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Delete,
            $"oauth-excluded-models?provider={Uri.EscapeDataString(provider.Trim().ToLowerInvariant())}",
            payload: null,
            cancellationToken: cancellationToken);
    }

    public async Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>>> GetOAuthModelAliasesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "oauth-model-alias", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOAuthModelAliases(document.RootElement));
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> ReplaceOAuthModelAliasesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>> aliases,
        CancellationToken cancellationToken = default)
    {
        var normalized = aliases.ToDictionary(
            pair => pair.Key.Trim().ToLowerInvariant(),
            pair => (IReadOnlyList<ManagementOAuthModelAliasEntry>)pair.Value
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Alias))
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        return SendOperationAsync(HttpMethod.Put, "oauth-model-alias", normalized, cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> UpdateOAuthModelAliasAsync(
        string channel,
        IReadOnlyList<ManagementOAuthModelAliasEntry> aliases,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Patch,
            "oauth-model-alias",
            new Dictionary<string, object?>
            {
                ["channel"] = channel.Trim().ToLowerInvariant(),
                ["aliases"] = aliases
            },
            cancellationToken);
    }

    public Task<ManagementApiResponse<ManagementOperationResult>> DeleteOAuthModelAliasAsync(
        string channel,
        CancellationToken cancellationToken = default)
    {
        return SendOperationAsync(
            HttpMethod.Delete,
            $"oauth-model-alias?channel={Uri.EscapeDataString(channel.Trim().ToLowerInvariant())}",
            payload: null,
            cancellationToken: cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetAuthFileModelsAsync(string name, CancellationToken cancellationToken = default)
    {
        return GetMappedAsync($"auth-files/models?name={Uri.EscapeDataString(name)}", ManagementMappers.MapModelDescriptors, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetModelDefinitionsAsync(string channel, CancellationToken cancellationToken = default)
    {
        return GetMappedAsync($"model-definitions/{Uri.EscapeDataString(channel)}", ManagementMappers.MapModelDescriptors, cancellationToken);
    }

    public async Task<ManagementApiResponse<ManagementOAuthStartResponse>> GetOAuthStartAsync(
        string provider,
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var path = BuildOAuthStartPath(provider, projectId);
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, path, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOAuthStart(document.RootElement));
    }

    public async Task<ManagementApiResponse<ManagementOAuthStatus>> GetOAuthStatusAsync(string state, CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(
            HttpMethod.Get,
            $"get-auth-status?state={Uri.EscapeDataString(state)}",
            cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOAuthStatus(document.RootElement));
    }

    public async Task<ManagementApiResponse<ManagementOperationResult>> SubmitOAuthCallbackAsync(
        string provider,
        string redirectUrl,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = string.Equals(provider, "gemini-cli", StringComparison.OrdinalIgnoreCase)
            ? "gemini"
            : provider.Trim().ToLowerInvariant();

        return await SendOperationAsync(
            HttpMethod.Post,
            "oauth-callback",
            new Dictionary<string, object?>
            {
                ["provider"] = normalizedProvider,
                ["redirect_url"] = redirectUrl
            },
            cancellationToken);
    }

    private async Task<ManagementApiResponse<T>> GetMappedAsync<T>(
        string path,
        Func<JsonElement, T> mapper,
        CancellationToken cancellationToken)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, path, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, mapper(document.RootElement));
    }

    private async Task<ManagementApiResponse<ManagementOperationResult>> SendOperationAsync(
        HttpMethod method,
        string path,
        object? payload = null,
        CancellationToken cancellationToken = default,
        string? rawBody = null)
    {
        var body = rawBody ?? (payload is null ? null : ManagementJson.Serialize(payload));
        var response = await _apiClient.SendManagementAsync(
            method,
            path,
            body,
            cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOperation(document.RootElement));
    }

    private static ManagementApiResponse<ManagementOperationResult> CreateOperationResponse(ManagementOperationResult result)
    {
        return new ManagementApiResponse<ManagementOperationResult>
        {
            Value = result,
            Metadata = new ManagementServerMetadata(),
            StatusCode = HttpStatusCode.OK
        };
    }

    private static string NormalizeFileName(string fileName)
    {
        var normalizedFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            throw new ArgumentException("Auth file name cannot be empty.", nameof(fileName));
        }

        if (!normalizedFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            normalizedFileName += ".json";
        }

        return normalizedFileName;
    }

    private static Dictionary<string, object?> BuildIndexedPayload<T>(int index, T value)
    {
        return new Dictionary<string, object?>
        {
            ["index"] = index,
            ["value"] = value
        };
    }

    private static string BuildProviderDeletePath(string route, string apiKey, string? baseUrl)
    {
        var query = $"api-key={Uri.EscapeDataString(apiKey.Trim())}";
        if (baseUrl is not null)
        {
            query += $"&base-url={Uri.EscapeDataString(baseUrl.Trim())}";
        }

        return $"{route}?{query}";
    }

    private static string BuildOAuthStartPath(string provider, string? projectId)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var parameters = new List<string>();

        if (WebUiSupportedProviders.Contains(normalizedProvider))
        {
            parameters.Add("is_webui=true");
        }

        if (string.Equals(normalizedProvider, "gemini-cli", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(projectId))
        {
            parameters.Add($"project_id={Uri.EscapeDataString(projectId)}");
        }

        var query = parameters.Count == 0 ? string.Empty : $"?{string.Join("&", parameters)}";
        return $"{normalizedProvider}-auth-url{query}";
    }
}
