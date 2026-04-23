using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using CPAD.Core.Abstractions.Updates;
using CPAD.Core.Constants;
using CPAD.Core.Enums;
using CPAD.Core.Models;

namespace CPAD.Infrastructure.Updates;

public sealed class GitHubReleaseUpdateService : IUpdateCheckService
{
    private const string RepositoryOwner = "Blackblock-inc";
    private const string RepositoryName = "Cli-Proxy-API-Desktop";
    private const string Repository = $"{RepositoryOwner}/{RepositoryName}";
    private const string StableReleaseApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    private const string ReleasePageUrl = $"https://github.com/{Repository}/releases";

    private readonly IHttpClientFactory _httpClientFactory;

    public GitHubReleaseUpdateService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        UpdateChannel channel = UpdateChannel.Stable,
        CancellationToken cancellationToken = default)
    {
        var normalizedCurrent = NormalizeVersionSource(currentVersion);
        if (channel == UpdateChannel.Beta)
        {
            return new UpdateCheckResult
            {
                Channel = UpdateChannel.Beta,
                Repository = Repository,
                ApiUrl = StableReleaseApiUrl,
                CurrentVersion = normalizedCurrent,
                IsCheckSuccessful = true,
                IsChannelReserved = true,
                Status = "Beta reserved",
                Detail = "The beta update channel is intentionally reserved. Stable GitHub Releases remain the only active desktop update source in this phase.",
                ReleasePageUrl = ReleasePageUrl
            };
        }

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, StableReleaseApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CPAD-Desktop", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult
            {
                Channel = UpdateChannel.Stable,
                Repository = Repository,
                ApiUrl = StableReleaseApiUrl,
                CurrentVersion = normalizedCurrent,
                IsCheckSuccessful = true,
                IsNoReleasePublished = true,
                Status = "No stable release",
                Detail = "GitHub returned 404 for the latest release endpoint, which means this repository does not currently publish a stable release.",
                ReleasePageUrl = ReleasePageUrl
            };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new UpdateCheckResult
            {
                Channel = UpdateChannel.Stable,
                Repository = Repository,
                ApiUrl = StableReleaseApiUrl,
                CurrentVersion = normalizedCurrent,
                IsCheckSuccessful = false,
                Status = "Check failed",
                Detail = $"GitHub latest release request failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.",
                ErrorMessage = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : TruncateSingleLine(body, 240),
                ReleasePageUrl = ReleasePageUrl
            };
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var tagName = GetString(root, "tag_name");
        var name = GetString(root, "name");
        var latestVersion = NormalizeVersionSource(string.IsNullOrWhiteSpace(tagName) ? name : tagName);
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return new UpdateCheckResult
            {
                Channel = UpdateChannel.Stable,
                Repository = Repository,
                ApiUrl = StableReleaseApiUrl,
                CurrentVersion = normalizedCurrent,
                IsCheckSuccessful = false,
                Status = "Check failed",
                Detail = "GitHub returned a release document without tag_name or name.",
                ErrorMessage = "Missing release version.",
                ReleasePageUrl = GetString(root, "html_url") ?? ReleasePageUrl
            };
        }

        var comparison = CompareVersions(latestVersion, normalizedCurrent);
        var status = comparison switch
        {
            > 0 => "Update available",
            <= 0 => "Up to date",
            _ => "Review latest version"
        };

        var assets = ParseAssets(root);
        var installableAsset = FindInstallableStableInstallerAsset(assets);

        return new UpdateCheckResult
        {
            Channel = UpdateChannel.Stable,
            Repository = Repository,
            ApiUrl = StableReleaseApiUrl,
            CurrentVersion = normalizedCurrent,
            LatestVersion = latestVersion,
            IsCheckSuccessful = true,
            IsUpdateAvailable = comparison > 0,
            Status = status,
            Detail = BuildDetail(comparison, latestVersion, normalizedCurrent),
            ReleasePageUrl = GetString(root, "html_url") ?? ReleasePageUrl,
            PublishedAt = GetDateTimeOffset(root, "published_at"),
            HasInstallableAsset = installableAsset is not null,
            InstallableAsset = installableAsset,
            Assets = assets
        };
    }

    private static string BuildDetail(int? comparison, string latestVersion, string currentVersion)
    {
        return comparison switch
        {
            > 0 => $"Stable release {latestVersion} is newer than the installed desktop version {currentVersion}.",
            <= 0 => $"Installed desktop version {currentVersion} is current for the latest stable release {latestVersion}.",
            _ => $"Latest stable release {latestVersion} was found, but the current version {currentVersion} could not be compared numerically."
        };
    }

    private static IReadOnlyList<UpdateReleaseAsset> ParseAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var assets = new List<UpdateReleaseAsset>();
        foreach (var asset in assetsElement.EnumerateArray())
        {
            assets.Add(new UpdateReleaseAsset
            {
                Name = GetString(asset, "name") ?? string.Empty,
                DownloadUrl = GetString(asset, "browser_download_url") ?? string.Empty,
                Size = GetInt64(asset, "size") ?? 0,
                Digest = GetString(asset, "digest")
            });
        }

        return assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
            .OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UpdateReleaseAsset? FindInstallableStableInstallerAsset(
        IReadOnlyList<UpdateReleaseAsset> assets)
    {
        return assets
            .Where(asset =>
                asset.Name.StartsWith($"{AppConstants.InstallerNamePrefix}.", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(asset.DownloadUrl))
            .OrderByDescending(asset => asset.Size)
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int? CompareVersions(string latestVersion, string currentVersion)
    {
        var latestParts = ParseVersionParts(latestVersion);
        var currentParts = ParseVersionParts(currentVersion);
        if (latestParts.Count == 0 || currentParts.Count == 0)
        {
            return null;
        }

        var length = Math.Max(latestParts.Count, currentParts.Count);
        for (var index = 0; index < length; index++)
        {
            var latest = index < latestParts.Count ? latestParts[index] : 0;
            var current = index < currentParts.Count ? currentParts[index] : 0;
            if (latest > current)
            {
                return 1;
            }

            if (latest < current)
            {
                return -1;
            }
        }

        return 0;
    }

    private static IReadOnlyList<int> ParseVersionParts(string version)
    {
        return version
            .Split([".", "-", "_", "+", " ", "v", "V"], StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var part) ? part : (int?)null)
            .Where(part => part.HasValue)
            .Select(part => part!.Value)
            .ToArray();
    }

    private static string NormalizeVersionSource(string? version)
    {
        var normalized = (version ?? string.Empty).Trim();
        return normalized.Length == 0 ? "unknown" : normalized.TrimStart('v', 'V');
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string TruncateSingleLine(string value, int maxLength)
    {
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }
}
