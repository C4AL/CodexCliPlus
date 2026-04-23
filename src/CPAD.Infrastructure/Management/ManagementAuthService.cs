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

    public Task<ManagementApiResponse<IReadOnlyList<ManagementAuthFileItem>>> GetAuthFilesAsync(CancellationToken cancellationToken = default)
    {
        return GetListAsync("auth-files", ManagementMappers.MapAuthFiles, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementGeminiKeyConfiguration>>> GetGeminiKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetListAsync("gemini-api-key", root => ManagementMappers.MapConfig(root).GeminiApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetCodexKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetListAsync("codex-api-key", root => ManagementMappers.MapConfig(root).CodexApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetClaudeKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetListAsync("claude-api-key", root => ManagementMappers.MapConfig(root).ClaudeApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementProviderKeyConfiguration>>> GetVertexKeysAsync(CancellationToken cancellationToken = default)
    {
        return GetListAsync("vertex-api-key", root => ManagementMappers.MapConfig(root).VertexApiKeys, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementOpenAiCompatibilityEntry>>> GetOpenAiCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        return GetListAsync("openai-compatibility", root => ManagementMappers.MapConfig(root).OpenAiCompatibility, cancellationToken);
    }

    public async Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<string>>>> GetOAuthExcludedModelsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "oauth-excluded-models", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOAuthExcludedModels(document.RootElement));
    }

    public async Task<ManagementApiResponse<IReadOnlyDictionary<string, IReadOnlyList<ManagementOAuthModelAliasEntry>>>> GetOAuthModelAliasesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, "oauth-model-alias", cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOAuthModelAliases(document.RootElement));
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetAuthFileModelsAsync(string name, CancellationToken cancellationToken = default)
    {
        return GetListAsync($"auth-files/models?name={Uri.EscapeDataString(name)}", ManagementMappers.MapModelDescriptors, cancellationToken);
    }

    public Task<ManagementApiResponse<IReadOnlyList<ManagementModelDescriptor>>> GetModelDefinitionsAsync(string channel, CancellationToken cancellationToken = default)
    {
        return GetListAsync($"model-definitions/{Uri.EscapeDataString(channel)}", ManagementMappers.MapModelDescriptors, cancellationToken);
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

        var body = ManagementJson.Serialize(new Dictionary<string, object?>
        {
            ["provider"] = normalizedProvider,
            ["redirect_url"] = redirectUrl
        });

        var response = await _apiClient.SendManagementAsync(HttpMethod.Post, "oauth-callback", body, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, ManagementMappers.MapOperation(document.RootElement));
    }

    private async Task<ManagementApiResponse<T>> GetListAsync<T>(
        string path,
        Func<JsonElement, T> mapper,
        CancellationToken cancellationToken)
    {
        var response = await _apiClient.SendManagementAsync(HttpMethod.Get, path, cancellationToken: cancellationToken);
        using var document = ManagementJson.Parse(response.Value);
        return ManagementResponseFactory.Map(response, mapper(document.RootElement));
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
