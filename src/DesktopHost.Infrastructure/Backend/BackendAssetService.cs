using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

using DesktopHost.Core.Abstractions.Logging;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Models;

namespace DesktopHost.Infrastructure.Backend;

public sealed class BackendAssetService
{
    private const string BackendArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.34/CLIProxyAPI_6.9.34_windows_amd64.zip";

    private const string BackendArchiveSha256 =
        "34ca9b7bf53a6dd89b874ed3e204371673b7eb1abf34792498af4e65bf204815";

    private const string ManagementHtmlUrl =
        "https://github.com/router-for-me/Cli-Proxy-API-Management-Center/releases/download/v1.7.41/management.html";

    private const string ManagementHtmlSha256 =
        "5df3cf888afab7678ee94f6041fb6584796c203092f3e270c00db6a43dfcaa99";

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
        var staticDirectory = Path.Combine(workingDirectory, "static");
        var managementHtmlPath = Path.Combine(staticDirectory, "management.html");

        Directory.CreateDirectory(staticDirectory);

        if (File.Exists(executablePath) && File.Exists(managementHtmlPath))
        {
            return new BackendAssetLayout(workingDirectory, executablePath, staticDirectory, managementHtmlPath);
        }

        if (TryCopyFromBundledAssets(executablePath, managementHtmlPath))
        {
            _logger.Info("已从安装目录复制 bundled 后端资源。");
            return new BackendAssetLayout(workingDirectory, executablePath, staticDirectory, managementHtmlPath);
        }

        if (TryCopyFromRepositoryAssets(executablePath, managementHtmlPath))
        {
            _logger.Info("已从仓库资源目录复制后端资产。");
            return new BackendAssetLayout(workingDirectory, executablePath, staticDirectory, managementHtmlPath);
        }

        _logger.Info("开始下载官方 CLIProxyAPI 与 management.html 资产。");
        await DownloadBackendArchiveAsync(workingDirectory, executablePath, cancellationToken);
        await DownloadManagementHtmlAsync(managementHtmlPath, cancellationToken);

        return new BackendAssetLayout(workingDirectory, executablePath, staticDirectory, managementHtmlPath);
    }

    private static bool TryCopyFromBundledAssets(string executablePath, string managementHtmlPath)
    {
        var bundledExecutable = Path.Combine(AppContext.BaseDirectory, "assets", "backend", "windows-x64", "cli-proxy-api.exe");
        var bundledManagementHtml = Path.Combine(AppContext.BaseDirectory, "assets", "webview2", "management.html");

        if (!File.Exists(bundledExecutable) || !File.Exists(bundledManagementHtml))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(managementHtmlPath)!);
        File.Copy(bundledExecutable, executablePath, overwrite: true);
        File.Copy(bundledManagementHtml, managementHtmlPath, overwrite: true);
        return true;
    }

    private bool TryCopyFromRepositoryAssets(string executablePath, string managementHtmlPath)
    {
        var repoRoot = TryFindRepositoryRoot();
        if (repoRoot is null)
        {
            return false;
        }

        var repoExecutable = Path.Combine(repoRoot, "resources", "backend", "windows-x64", "cli-proxy-api.exe");
        var repoManagementHtml = Path.Combine(repoRoot, "resources", "webview2", "management.html");

        if (!File.Exists(repoExecutable) || !File.Exists(repoManagementHtml))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(managementHtmlPath)!);
        File.Copy(repoExecutable, executablePath, overwrite: true);
        File.Copy(repoManagementHtml, managementHtmlPath, overwrite: true);
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
            throw new InvalidOperationException("下载的 CLIProxyAPI 压缩包中缺少 cli-proxy-api.exe。");
        }

        executableEntry.ExtractToFile(executablePath, overwrite: true);

        foreach (var entry in archive.Entries.Where(
                     item => item.Name is "LICENSE" or "README.md" or "README_CN.md"))
        {
            var targetPath = Path.Combine(workingDirectory, entry.Name);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private async Task DownloadManagementHtmlAsync(string managementHtmlPath, CancellationToken cancellationToken)
    {
        var managementHtmlBytes = await DownloadAndValidateAsync(
            ManagementHtmlUrl,
            ManagementHtmlSha256,
            cancellationToken);

        await File.WriteAllBytesAsync(managementHtmlPath, managementHtmlBytes, cancellationToken);
    }

    private async Task<byte[]> DownloadAndValidateAsync(
        string url,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"下载文件校验失败：{url}");
        }

        return bytes;
    }

    private static string? TryFindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CliProxyApiDesktop.sln"))
                || Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
        }

        return null;
    }
}
