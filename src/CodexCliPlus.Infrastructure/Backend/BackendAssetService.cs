using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Infrastructure.Backend;

public sealed class BackendAssetService
{
    private readonly HttpClient _httpClient;
    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;

    public BackendAssetService(HttpClient httpClient, IPathService pathService, IAppLogger logger)
    {
        _httpClient = httpClient;
        _pathService = pathService;
        _logger = logger;
    }

    public async Task<BackendAssetLayout> EnsureAssetsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);

        var workingDirectory = _pathService.Directories.BackendDirectory;
        var executablePath = GetManagedExecutablePath(workingDirectory);

        Directory.CreateDirectory(workingDirectory);

        if (
            File.Exists(executablePath)
            && await IsExecutableVersionCurrentAsync(executablePath, cancellationToken)
        )
        {
            CleanupLegacyManagedExecutable(workingDirectory);
            return CreateLayout(workingDirectory, executablePath);
        }

        return await RepairAssetsAsync(cancellationToken);
    }

    public async Task<BackendAssetLayout> RepairAssetsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);

        var workingDirectory = _pathService.Directories.BackendDirectory;
        var executablePath = GetManagedExecutablePath(workingDirectory);
        Directory.CreateDirectory(workingDirectory);

        if (TryCopyFromBundledAssets(workingDirectory, executablePath))
        {
            if (await IsExecutableVersionCurrentAsync(executablePath, cancellationToken))
            {
                _logger.Info("Copied backend files from the application bundle.");
                CleanupLegacyManagedExecutable(workingDirectory);
                return CreateLayout(workingDirectory, executablePath);
            }

            _logger.Warn(
                "Bundled backend assets are not the pinned CLIProxyAPI version; continuing repair."
            );
        }

        if (TryCopyFromRepositoryAssets(workingDirectory, executablePath))
        {
            if (await IsExecutableVersionCurrentAsync(executablePath, cancellationToken))
            {
                _logger.Info("Copied backend files from repository resources.");
                CleanupLegacyManagedExecutable(workingDirectory);
                return CreateLayout(workingDirectory, executablePath);
            }

            _logger.Warn(
                "Repository backend assets are not the pinned CLIProxyAPI version; continuing repair."
            );
        }

        _logger.Info("Downloading CLIProxyAPI backend assets.");
        await DownloadBackendArchiveAsync(workingDirectory, executablePath, cancellationToken);
        if (!await IsExecutableVersionCurrentAsync(executablePath, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Downloaded CLIProxyAPI backend did not match the pinned version {BackendReleaseMetadata.Version}."
            );
        }

        CleanupLegacyManagedExecutable(workingDirectory);

        return CreateLayout(workingDirectory, executablePath);
    }

    private async Task<bool> IsExecutableVersionCurrentAsync(
        string executablePath,
        CancellationToken cancellationToken
    )
    {
        var version = await TryReadExecutableVersionAsync(executablePath, cancellationToken);
        if (IsExpectedBackendVersion(version))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            _logger.Warn($"Could not determine CLIProxyAPI backend version at '{executablePath}'.");
        }
        else
        {
            _logger.Warn(
                $"CLIProxyAPI backend at '{executablePath}' is version {version}; expected {BackendReleaseMetadata.Version}."
            );
        }

        return false;
    }

    private async Task<string?> TryReadExecutableVersionAsync(
        string executablePath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            var result = await ProcessCapture.RunAsync(
                executablePath,
                "--version",
                Path.GetDirectoryName(executablePath),
                timeout.Token
            );

            if (result.ExitCode != 0)
            {
                _logger.Warn(
                    $"CLIProxyAPI version probe exited with code {result.ExitCode}: {result.StandardError}"
                );
            }

            var output = string.Join(
                Environment.NewLine,
                new[] { result.StandardOutput, result.StandardError }.Where(value =>
                    !string.IsNullOrWhiteSpace(value)
                )
            );
            return TryParseCliProxyApiVersion(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn($"CLIProxyAPI version probe timed out for '{executablePath}'.");
            return null;
        }
        catch (Exception exception)
        {
            _logger.Warn(
                $"CLIProxyAPI version probe failed for '{executablePath}': {exception.Message}"
            );
            return null;
        }
    }

    internal static string? TryParseCliProxyApiVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var labeledMatch = Regex.Match(
            output,
            @"(?im)\b(?:CLIProxyAPI\s+Version|Version)\s*:\s*v?(?<version>\d+(?:\.\d+){1,3})(?:\b|$)",
            RegexOptions.CultureInvariant
        );
        if (labeledMatch.Success)
        {
            return labeledMatch.Groups["version"].Value;
        }

        var fallbackMatch = Regex.Match(
            output,
            @"(?im)\bv?(?<version>\d+\.\d+\.\d+(?:\.\d+)?)\b",
            RegexOptions.CultureInvariant
        );
        return fallbackMatch.Success ? fallbackMatch.Groups["version"].Value : null;
    }

    internal static bool IsExpectedBackendVersion(string? version)
    {
        return string.Equals(
            version?.Trim().TrimStart('v', 'V'),
            BackendReleaseMetadata.Version,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static BackendAssetLayout CreateLayout(string workingDirectory, string executablePath)
    {
        return new BackendAssetLayout(workingDirectory, executablePath);
    }

    private static string GetManagedExecutablePath(string workingDirectory)
    {
        return Path.Combine(workingDirectory, BackendExecutableNames.ManagedExecutableFileName);
    }

    private static bool TryCopyFromBundledAssets(string workingDirectory, string executablePath)
    {
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
        var bundledBackendDirectory = Path.Combine(assetsRoot, "backend", "windows-x64");
        var bundledExecutable = Path.Combine(
            bundledBackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName
        );

        if (!File.Exists(bundledExecutable))
        {
            return false;
        }

        CopyAssetFile(bundledExecutable, executablePath);
        CopyDocumentationFiles(bundledBackendDirectory, workingDirectory);
        return true;
    }

    private static bool TryCopyFromRepositoryAssets(string workingDirectory, string executablePath)
    {
        var repositoryRoot = TryFindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return false;
        }

        var resourcesRoot = Path.Combine(repositoryRoot, AppConstants.ResourcesDirectoryName);
        var repositoryBackendDirectory = Path.Combine(resourcesRoot, "backend", "windows-x64");
        var repositoryExecutable = Path.Combine(
            repositoryBackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName
        );

        if (!File.Exists(repositoryExecutable))
        {
            return false;
        }

        CopyAssetFile(repositoryExecutable, executablePath);
        CopyDocumentationFiles(repositoryBackendDirectory, workingDirectory);

        return true;
    }

    private async Task DownloadBackendArchiveAsync(
        string workingDirectory,
        string executablePath,
        CancellationToken cancellationToken
    )
    {
        var archiveBytes = await DownloadAndValidateAsync(
            BackendReleaseMetadata.ArchiveUrl,
            BackendReleaseMetadata.ArchiveSha256,
            cancellationToken
        );

        using var archiveStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var executableEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                entry.Name,
                BackendExecutableNames.UpstreamExecutableFileName,
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (executableEntry is null)
        {
            throw new InvalidOperationException(
                $"The downloaded CLIProxyAPI archive does not contain {BackendExecutableNames.UpstreamExecutableFileName}."
            );
        }

        ExtractEntry(executableEntry, executablePath);

        foreach (
            var entry in archive.Entries.Where(item =>
                item.Name is "LICENSE" or "README.md" or "README_CN.md" or "config.example.yaml"
            )
        )
        {
            ExtractEntry(entry, Path.Combine(workingDirectory, entry.Name));
        }
    }

    private void CleanupLegacyManagedExecutable(string workingDirectory)
    {
        var legacyExecutablePath = Path.Combine(
            workingDirectory,
            BackendExecutableNames.UpstreamExecutableFileName
        );
        if (!File.Exists(legacyExecutablePath))
        {
            return;
        }

        try
        {
            File.Delete(legacyExecutablePath);
            _logger.Info($"Removed legacy managed backend executable '{legacyExecutablePath}'.");
        }
        catch (Exception exception)
        {
            _logger.Warn(
                $"Failed to remove legacy managed backend executable '{legacyExecutablePath}': {exception.Message}"
            );
        }
    }

    private static void CopyDocumentationFiles(string sourceDirectory, string workingDirectory)
    {
        foreach (
            var fileName in new[] { "LICENSE", "README.md", "README_CN.md", "config.example.yaml" }
        )
        {
            var sourcePath = Path.Combine(sourceDirectory, fileName);
            if (File.Exists(sourcePath))
            {
                CopyAssetFile(sourcePath, Path.Combine(workingDirectory, fileName));
            }
        }
    }

    private async Task<byte[]> DownloadAndValidateAsync(
        string url,
        string expectedSha256,
        CancellationToken cancellationToken
    )
    {
        var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Downloaded asset hash mismatch for '{url}'. Expected '{expectedSha256}', got '{actualSha256}'."
            );
        }

        return bytes;
    }

    private static void CopyAssetFile(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    private static string? TryFindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "CodexCliPlus.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}
