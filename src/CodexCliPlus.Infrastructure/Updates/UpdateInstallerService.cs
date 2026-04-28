using System.Diagnostics;
using System.Security.Cryptography;

using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Infrastructure.Updates;

public sealed class UpdateInstallerService : IUpdateInstallerService
{
    private readonly IPathService _pathService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;

    public UpdateInstallerService(
        IPathService pathService,
        IHttpClientFactory httpClientFactory)
        : this(
            pathService,
            httpClientFactory,
            static startInfo => Process.Start(startInfo))
    {
    }

    public UpdateInstallerService(
        IPathService pathService,
        IHttpClientFactory httpClientFactory,
        Func<ProcessStartInfo, Process?> processStarter)
    {
        _pathService = pathService;
        _httpClientFactory = httpClientFactory;
        _processStarter = processStarter;
    }

    public bool CanPrepareInstaller(UpdateCheckResult updateCheckResult)
    {
        return _pathService.Directories.DataMode == AppDataMode.Installed &&
            updateCheckResult is not null &&
            updateCheckResult.Channel == UpdateChannel.Stable &&
            !updateCheckResult.IsChannelReserved &&
            updateCheckResult.IsCheckSuccessful &&
            updateCheckResult.IsUpdateAvailable &&
            ResolveInstallableAssetOrNull(updateCheckResult) is not null;
    }

    public async Task<PreparedUpdateInstaller> DownloadInstallerAsync(
        UpdateCheckResult updateCheckResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateCheckResult);
        EnsureInstalledModeForInstallerHandoff();
        ValidateStableUpdateResult(updateCheckResult);

        var installerAsset = ResolveInstallableAsset(updateCheckResult);
        await _pathService.EnsureCreatedAsync(cancellationToken);

        var updatesCacheDirectory = Path.Combine(_pathService.Directories.CacheDirectory, "updates");
        Directory.CreateDirectory(updatesCacheDirectory);

        var safeFileName = Path.GetFileName(installerAsset.Name);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new InvalidOperationException("Stable installer asset does not provide a valid file name.");
        }

        var installerPath = Path.Combine(updatesCacheDirectory, safeFileName);
        if (TryReuseCachedInstaller(installerPath, installerAsset, out var cachedDigestValidated))
        {
            return CreatePreparedInstaller(
                updateCheckResult,
                installerAsset,
                updatesCacheDirectory,
                installerPath,
                usedCachedFile: true,
                digestValidated: cachedDigestValidated);
        }

        if (string.IsNullOrWhiteSpace(installerAsset.DownloadUrl))
        {
            throw new InvalidOperationException("Stable installer asset does not provide a download URL.");
        }

        var temporaryPath = $"{installerPath}.download";
        DeleteFileIfPresent(temporaryPath);

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(
                installerAsset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var installerStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await installerStream.CopyToAsync(fileStream, cancellationToken);
            }

            var digestValidated = ValidateInstallerFile(temporaryPath, installerAsset, strictDigestValidation: true);

            DeleteFileIfPresent(installerPath);
            File.Move(temporaryPath, installerPath);

            return CreatePreparedInstaller(
                updateCheckResult,
                installerAsset,
                updatesCacheDirectory,
                installerPath,
                usedCachedFile: false,
                digestValidated: digestValidated);
        }
        catch
        {
            DeleteFileIfPresent(temporaryPath);
            throw;
        }
    }

    public Task LaunchInstallerAsync(
        PreparedUpdateInstaller installer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installer);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInstalledModeForInstallerHandoff();

        if (installer.DataMode != AppDataMode.Installed)
        {
            throw new InvalidOperationException(
                "Installer handoff is only supported for Installed mode. Portable and Development modes remain manual-only.");
        }

        if (string.IsNullOrWhiteSpace(installer.InstallerPath) || !File.Exists(installer.InstallerPath))
        {
            throw new FileNotFoundException("Prepared installer executable could not be found.", installer.InstallerPath);
        }

        var workingDirectory = Path.GetDirectoryName(installer.InstallerPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException("Prepared installer does not provide a valid working directory.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installer.InstallerPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        var process = _processStarter(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException(
                $"Failed to launch installer '{installer.InstallerPath}' via shell execute.");
        }

        return Task.CompletedTask;
    }

    private static PreparedUpdateInstaller CreatePreparedInstaller(
        UpdateCheckResult updateCheckResult,
        UpdateReleaseAsset installerAsset,
        string updatesCacheDirectory,
        string installerPath,
        bool usedCachedFile,
        bool digestValidated)
    {
        return new PreparedUpdateInstaller
        {
            Asset = installerAsset,
            Version = updateCheckResult.LatestVersion,
            CacheDirectory = updatesCacheDirectory,
            InstallerPath = installerPath,
            UsedCachedFile = usedCachedFile,
            DigestValidated = digestValidated,
            DataMode = AppDataMode.Installed
        };
    }

    private static UpdateReleaseAsset ResolveInstallableAsset(UpdateCheckResult updateCheckResult)
    {
        var installableAsset = ResolveInstallableAssetOrNull(updateCheckResult);

        if (installableAsset is null)
        {
            throw new InvalidOperationException(
                "The update result does not contain a directly installable stable installer asset.");
        }

        return installableAsset;
    }

    private static UpdateReleaseAsset? ResolveInstallableAssetOrNull(UpdateCheckResult updateCheckResult)
    {
        if (updateCheckResult.InstallableAsset is not null &&
            IsInstallableAsset(updateCheckResult.InstallableAsset))
        {
            return updateCheckResult.InstallableAsset;
        }

        return updateCheckResult.Assets.FirstOrDefault(IsInstallableAsset);
    }

    private void EnsureInstalledModeForInstallerHandoff()
    {
        if (_pathService.Directories.DataMode == AppDataMode.Installed)
        {
            return;
        }

        throw new InvalidOperationException(
            "Installer handoff is disabled outside Installed mode. Portable and Development modes do not download, launch, or apply installer updates automatically.");
    }

    private static void ValidateStableUpdateResult(UpdateCheckResult updateCheckResult)
    {
        if (updateCheckResult.Channel != UpdateChannel.Stable || updateCheckResult.IsChannelReserved)
        {
            throw new InvalidOperationException(
                "Installer handoff requires an active Stable update check result. Beta is reserved and cannot produce installer handoff.");
        }

        if (!updateCheckResult.IsCheckSuccessful)
        {
            throw new InvalidOperationException(
                "Installer handoff requires a successful Stable update check result.");
        }

        if (!updateCheckResult.IsUpdateAvailable)
        {
            throw new InvalidOperationException(
                "Installer handoff is only available when the Stable release is newer than the running desktop version.");
        }
    }

    private static bool TryReuseCachedInstaller(
        string installerPath,
        UpdateReleaseAsset installerAsset,
        out bool digestValidated)
    {
        digestValidated = false;

        if (!File.Exists(installerPath))
        {
            return false;
        }

        try
        {
            digestValidated = ValidateInstallerFile(
                installerPath,
                installerAsset,
                strictDigestValidation: false);
            return true;
        }
        catch
        {
            DeleteFileIfPresent(installerPath);
            return false;
        }
    }

    private static bool ValidateInstallerFile(
        string installerPath,
        UpdateReleaseAsset installerAsset,
        bool strictDigestValidation)
    {
        var fileInfo = new FileInfo(installerPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Installer executable could not be found for validation.", installerPath);
        }

        if (installerAsset.Size > 0 && fileInfo.Length != installerAsset.Size)
        {
            throw new InvalidOperationException(
                $"Installer size mismatch for '{installerAsset.Name}'. Expected {installerAsset.Size} bytes but found {fileInfo.Length}.");
        }

        if (!TryParseSha256Digest(installerAsset.Digest, out var expectedDigest))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(expectedDigest))
        {
            return false;
        }

        var actualDigest = ComputeSha256(installerPath);
        if (string.Equals(actualDigest, expectedDigest, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!strictDigestValidation)
        {
            throw new InvalidOperationException(
                $"Cached installer digest mismatch for '{installerAsset.Name}'.");
        }

        throw new InvalidOperationException(
            $"Downloaded installer digest mismatch for '{installerAsset.Name}'.");
    }

    private static bool TryParseSha256Digest(string? digest, out string? normalizedDigest)
    {
        normalizedDigest = null;
        if (string.IsNullOrWhiteSpace(digest))
        {
            return false;
        }

        var parts = digest.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !string.Equals(parts[0], "sha256", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported installer digest format '{digest}'. Expected 'sha256:<hex>'.");
        }

        if (string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new InvalidOperationException(
                $"Unsupported installer digest format '{digest}'. Expected 'sha256:<hex>'.");
        }

        normalizedDigest = parts[1].Trim().ToLowerInvariant();
        return true;
    }

    private static string ComputeSha256(string installerPath)
    {
        using var stream = File.OpenRead(installerPath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsInstallableAsset(UpdateReleaseAsset asset)
    {
        return asset.Name.StartsWith($"{AppConstants.InstallerNamePrefix}.", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(asset.DownloadUrl);
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
