using CPAD.Core.Enums;

namespace CPAD.Core.Models;

public sealed class UpdateCheckResult
{
    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;

    public string Repository { get; init; } = string.Empty;

    public string ApiUrl { get; init; } = string.Empty;

    public string CurrentVersion { get; init; } = string.Empty;

    public string? LatestVersion { get; init; }

    public bool IsCheckSuccessful { get; init; }

    public bool IsUpdateAvailable { get; init; }

    public bool IsNoReleasePublished { get; init; }

    public bool IsChannelReserved { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public string? ReleasePageUrl { get; init; }

    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset? PublishedAt { get; init; }

    public bool HasInstallableAsset { get; init; }

    public UpdateReleaseAsset? InstallableAsset { get; init; }

    public IReadOnlyList<UpdateReleaseAsset> Assets { get; init; } = [];
}

public sealed class UpdateReleaseAsset
{
    public string Name { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    public long Size { get; init; }

    public string? Digest { get; init; }
}
