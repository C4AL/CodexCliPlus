using System.IO.Compression;
using System.Security.Cryptography;

using CPAD.Core.Abstractions.Logging;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Models;

namespace CPAD.Infrastructure.Backend;

public sealed class BackendAssetService
{
    private const string BackendArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.34/CLIProxyAPI_6.9.34_windows_amd64.zip";

    private const string BackendArchiveSha256 =
        "34ca9b7bf53a6dd89b874ed3e204371673b7eb1abf34792498af4e65bf204815";

    private readonly HttpClient _httpClient;
    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;

    public BackendAssetService(HttpClient httpClient, IPathService pathService, IAppLogger logger)
    {
        _httpClient = httpClient;
        _pathService = pathService;
        _logger = logger;
    }

    public async Task<BackendAssetLayout> EnsureAssetsAsync(CancellationToken cancellationToken = default)
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);

        var workingDirectory = _pathService.Directories.BackendDirectory;
        var executablePath = Path.Combine(workingDirectory, "cli-proxy-api.exe");

        Directory.CreateDirectory(workingDirectory);

        if (File.Exists(executablePath))
        {
            return CreateLayout(workingDirectory, executablePath);
        }

        if (TryCopyFromBundledAssets(workingDirectory, executablePath))
        {
            _logger.Info("Copied backend files from the application bundle.");
            return CreateLayout(workingDirectory, executablePath);
        }

        if (TryCopyFromRepositoryAssets(workingDirectory, executablePath))
        {
            _logger.Info("Copied backend files from repository resources.");
            return CreateLayout(workingDirectory, executablePath);
        }

        _logger.Info("Downloading CLIProxyAPI backend assets.");
        await DownloadBackendArchiveAsync(workingDirectory, executablePath, cancellationToken);

        return CreateLayout(workingDirectory, executablePath);
    }

    private static BackendAssetLayout CreateLayout(
        string workingDirectory,
        string executablePath)
    {
        return new BackendAssetLayout(workingDirectory, executablePath);
    }

    private static bool TryCopyFromBundledAssets(string workingDirectory, string executablePath)
    {
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
        var bundledBackendDirectory = Path.Combine(assetsRoot, "backend", "windows-x64");
        var bundledExecutable = Path.Combine(bundledBackendDirectory, "cli-proxy-api.exe");

        if (!File.Exists(bundledExecutable))
        {
            return false;
        }

        CopyAssetFile(bundledExecutable, executablePath);
        CopyDocumentationFiles(bundledBackendDirectory, workingDirectory);
        return true;
    }

    private static bool TryCopyFromRepositoryAssets(
        string workingDirectory,
        string executablePath)
    {
        var repositoryRoot = TryFindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return false;
        }

        var resourcesRoot = Path.Combine(repositoryRoot, AppConstants.ResourcesDirectoryName);
        var repositoryBackendDirectory = Path.Combine(resourcesRoot, "backend", "windows-x64");
        var repositoryExecutable = Path.Combine(repositoryBackendDirectory, "cli-proxy-api.exe");

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
        CancellationToken cancellationToken)
    {
        var archiveBytes = await DownloadAndValidateAsync(BackendArchiveUrl, BackendArchiveSha256, cancellationToken);

        using var archiveStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var executableEntry = archive.Entries.FirstOrDefault(
            entry => string.Equals(entry.Name, "cli-proxy-api.exe", StringComparison.OrdinalIgnoreCase));

        if (executableEntry is null)
        {
            throw new InvalidOperationException(
                "The downloaded CLIProxyAPI archive does not contain cli-proxy-api.exe.");
        }

        ExtractEntry(executableEntry, executablePath);

        foreach (var entry in archive.Entries.Where(item => item.Name is "LICENSE" or "README.md" or "README_CN.md"))
        {
            ExtractEntry(entry, Path.Combine(workingDirectory, entry.Name));
        }
    }

    private static void CopyDocumentationFiles(string sourceDirectory, string workingDirectory)
    {
        foreach (var fileName in new[] { "LICENSE", "README.md", "README_CN.md", "config.example.yaml" })
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
        CancellationToken cancellationToken)
    {
        var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Downloaded asset hash mismatch for '{url}'. Expected '{expectedSha256}', got '{actualSha256}'.");
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
            if (File.Exists(Path.Combine(currentDirectory.FullName, "CliProxyApiDesktop.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}
