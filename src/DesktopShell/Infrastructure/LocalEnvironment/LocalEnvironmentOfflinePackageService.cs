using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;

namespace CodexCliPlus.Infrastructure.LocalEnvironment;

public sealed class LocalEnvironmentOfflinePackageService
{
    public const string PendingUpgradeFileName = "local-environment-offline-upgrade.json";

    private static readonly Uri NodeDistributionIndexUri = new(
        "https://nodejs.org/dist/index.json"
    );
    private static readonly Uri CodexRegistryMetadataUri = new(
        "https://registry.npmjs.org/@openai%2Fcodex"
    );
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly Func<string> _assetRootResolver;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<Uri, CancellationToken, Task> _networkProbeAsync;

    public LocalEnvironmentOfflinePackageService(
        IPathService pathService,
        IAppLogger logger,
        Func<string>? assetRootResolver = null,
        Func<DateTimeOffset>? clock = null,
        Func<Uri, CancellationToken, Task>? networkProbeAsync = null
    )
    {
        _pathService = pathService;
        _logger = logger;
        _assetRootResolver =
            assetRootResolver
            ?? (() => Path.Combine(AppContext.BaseDirectory, "assets", "local-environment"));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _networkProbeAsync = networkProbeAsync ?? ProbeNetworkAsync;
    }

    public string PendingUpgradePath =>
        Path.Combine(_pathService.Directories.RuntimeDirectory, PendingUpgradeFileName);

    public async Task<LocalEnvironmentBundledInstallPlan> PrepareInstallAsync(
        CancellationToken cancellationToken = default
    )
    {
        var root = Path.GetFullPath(_assetRootResolver());
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("未找到内置离线环境包 manifest。", manifestPath);
        }

        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest =
            await JsonSerializer.DeserializeAsync<LocalEnvironmentBundleManifest>(
                manifestStream,
                JsonOptions,
                cancellationToken
            ) ?? throw new InvalidDataException("内置离线环境包 manifest 无法读取。");

        ValidateManifest(manifest);

        var nodeInstallerPath = ResolveBundledPath(root, manifest.Node.FileName);
        if (!File.Exists(nodeInstallerPath))
        {
            throw new FileNotFoundException("内置 Node.js 安装包不存在。", nodeInstallerPath);
        }

        var expectedSha = NormalizeSha256(manifest.Node.Sha256);
        var actualSha = await ComputeSha256Async(nodeInstallerPath, cancellationToken);
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("内置 Node.js 安装包 SHA256 校验失败。");
        }

        var bundledNpmCachePath = ResolveBundledPath(root, manifest.Codex.NpmCachePath);
        if (!Directory.Exists(bundledNpmCachePath))
        {
            throw new DirectoryNotFoundException("内置 Codex npm 离线缓存不存在。");
        }

        if (!Directory.EnumerateFiles(bundledNpmCachePath, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidDataException("内置 Codex npm 离线缓存不完整。");
        }

        var writableCachePath = Path.Combine(
            _pathService.Directories.CacheDirectory,
            "local-environment",
            "npm-cache",
            SanitizePathSegment(manifest.Codex.Version)
        );
        EnsurePathUnderDirectory(writableCachePath, _pathService.Directories.CacheDirectory);
        ResetDirectory(writableCachePath);
        CopyDirectory(bundledNpmCachePath, writableCachePath);

        return new LocalEnvironmentBundledInstallPlan(
            manifest,
            nodeInstallerPath,
            writableCachePath
        );
    }

    public async Task WritePendingUpgradeAsync(
        LocalEnvironmentBundleManifest manifest,
        CancellationToken cancellationToken = default
    )
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);
        var now = _clock();
        var state = new LocalEnvironmentOfflineUpgradeState
        {
            Schema = 1,
            OfflineNodeVersion = manifest.Node.Version,
            OfflineCodexVersion = manifest.Codex.Version,
            CreatedAt = now,
            NextAllowedCheckAt = now,
        };
        await WritePendingUpgradeAsync(state, cancellationToken);
    }

    public async Task<LocalEnvironmentOfflineUpgradeState?> TryReadPendingUpgradeAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);
        var path = PendingUpgradePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LocalEnvironmentOfflineUpgradeState>(
                stream,
                JsonOptions,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warn($"离线环境待升级标记读取失败：{exception.Message}");
            return null;
        }
    }

    public async Task ClearPendingUpgradeAsync(CancellationToken cancellationToken = default)
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(PendingUpgradePath))
        {
            File.Delete(PendingUpgradePath);
        }
    }

    public async Task PostponePendingUpgradeAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default
    )
    {
        var state = await TryReadPendingUpgradeAsync(cancellationToken);
        if (state is null)
        {
            return;
        }

        var postponed = state with { NextAllowedCheckAt = _clock().Add(delay) };
        await WritePendingUpgradeAsync(postponed, cancellationToken);
    }

    public async Task<bool> IsUpgradeNetworkReadyAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await Task.WhenAll(
                _networkProbeAsync(NodeDistributionIndexUri, timeout.Token),
                _networkProbeAsync(CodexRegistryMetadataUri, timeout.Token)
            );
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Info($"离线环境升级网络检查未通过：{exception.Message}");
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Info("离线环境升级网络检查超时。");
            return false;
        }
    }

    private async Task WritePendingUpgradeAsync(
        LocalEnvironmentOfflineUpgradeState state,
        CancellationToken cancellationToken
    )
    {
        var path = PendingUpgradePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(state, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void ValidateManifest(LocalEnvironmentBundleManifest manifest)
    {
        if (manifest.Schema <= 0)
        {
            throw new InvalidDataException("内置离线环境包 manifest schema 无效。");
        }

        RequireValue(manifest.Runtime, "runtime");
        RequireValue(manifest.Node.Version, "node.version");
        RequireValue(manifest.Node.Architecture, "node.architecture");
        RequireValue(manifest.Node.FileName, "node.fileName");
        RequireValue(manifest.Node.Sha256, "node.sha256");
        RequireValue(manifest.Codex.Version, "codex.version");
        RequireValue(manifest.Codex.NpmCachePath, "codex.npmCachePath");
        _ = NormalizeSha256(manifest.Node.Sha256);
    }

    private static void RequireValue(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"内置离线环境包 manifest 缺少 {propertyName}。");
        }
    }

    private static string ResolveBundledPath(string root, string manifestPath)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.IsPathRooted(manifestPath)
            ? Path.GetFullPath(manifestPath)
            : Path.GetFullPath(Path.Combine(fullRoot, manifestPath.Replace('/', Path.DirectorySeparatorChar)));
        EnsurePathUnderDirectory(candidate, fullRoot);
        return candidate;
    }

    private static void EnsurePathUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径不在允许的目录内。");
        }
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var targetPath = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (
            normalized.Length != 64
            || normalized.Any(character => !Uri.IsHexDigit(character))
        )
        {
            throw new InvalidDataException("内置 Node.js 安装包 SHA256 格式无效。");
        }

        return normalized;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task ProbeNetworkAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await SharedHttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
    }
}

public sealed record LocalEnvironmentBundledInstallPlan(
    LocalEnvironmentBundleManifest Manifest,
    string NodeInstallerPath,
    string WritableNpmCachePath
);

public sealed class LocalEnvironmentBundleManifest
{
    public int Schema { get; init; }

    public string Runtime { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; }

    public LocalEnvironmentBundleNodeManifest Node { get; init; } = new();

    public LocalEnvironmentBundleCodexManifest Codex { get; init; } = new();
}

public sealed class LocalEnvironmentBundleNodeManifest
{
    public string Version { get; init; } = string.Empty;

    public string Architecture { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;
}

public sealed class LocalEnvironmentBundleCodexManifest
{
    public string Version { get; init; } = string.Empty;

    public string NpmCachePath { get; init; } = string.Empty;
}

public sealed record LocalEnvironmentOfflineUpgradeState
{
    public int Schema { get; init; }

    public string OfflineNodeVersion { get; init; } = string.Empty;

    public string OfflineCodexVersion { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset NextAllowedCheckAt { get; init; }
}
