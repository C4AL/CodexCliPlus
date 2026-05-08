using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Infrastructure.Updates;

internal static class UpdateAssetSelector
{
    public static UpdateReleaseAsset? FindInstallableSelfUpdatePackage(
        IEnumerable<UpdateReleaseAsset> assets
    )
    {
        return assets
            .Where(IsInstallableSelfUpdatePackage)
            .OrderByDescending(asset => asset.Size)
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool IsInstallableSelfUpdatePackage(UpdateReleaseAsset? asset)
    {
        return asset is not null
            && asset.Name.StartsWith("CodexCliPlus.Update.", StringComparison.OrdinalIgnoreCase)
            && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(asset.DownloadUrl);
    }
}
