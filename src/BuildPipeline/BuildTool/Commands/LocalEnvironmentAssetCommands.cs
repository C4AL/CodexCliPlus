using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexCliPlus.BuildTool;

internal static class LocalEnvironmentAssetCommands
{
    private const int ManifestSchema = 1;
    private const string CodexPackageName = "@openai/codex";
    private const string NodeDistributionBaseUrl = "https://nodejs.org/dist/";
    private static readonly Uri NodeDistributionIndexUri = new(NodeDistributionBaseUrl + "index.json");
    private static readonly Uri CodexRegistryMetadataUri = new(
        "https://registry.npmjs.org/@openai%2Fcodex"
    );
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    public static async Task BuildAsync(BuildContext context)
    {
        var targetRoot = Path.Combine(context.AssetsRoot, "local-environment");
        Directory.CreateDirectory(targetRoot);

        if (await TryBuildFromOverridesAsync(context, targetRoot))
        {
            return;
        }

        if (ShouldBuildSyntheticAssets(context))
        {
            await BuildSyntheticAssetsAsync(context, targetRoot);
            return;
        }

        var nodeArchitecture = ResolveNodeArchitecture(context.Options.Runtime);
        var node = await ResolveNodeLtsAsync(nodeArchitecture);
        var nodeDirectory = Path.Combine(targetRoot, "node");
        Directory.CreateDirectory(nodeDirectory);
        var nodeTargetPath = Path.Combine(nodeDirectory, node.FileName);
        await DownloadFileAsync(node.DownloadUri, nodeTargetPath);
        var nodeSha256 = await ComputeSha256Async(nodeTargetPath);

        var codexVersion = await ResolveLatestCodexVersionAsync();
        var npmCachePath = Path.Combine(targetRoot, "npm-cache");
        Directory.CreateDirectory(npmCachePath);
        await WarmCodexNpmCacheAsync(context, codexVersion, npmCachePath);
        RequireNonEmptyDirectory(npmCachePath, "Codex npm cache");

        await WriteManifestAsync(
            context,
            targetRoot,
            node.Version,
            nodeArchitecture,
            ToManifestPath(targetRoot, nodeTargetPath),
            nodeSha256,
            codexVersion,
            ToManifestPath(targetRoot, npmCachePath)
        );
    }

    private static async Task<bool> TryBuildFromOverridesAsync(BuildContext context, string targetRoot)
    {
        var nodeMsiPath = Environment.GetEnvironmentVariable(
            "CODEXCLIPLUS_LOCAL_ENV_NODE_MSI_PATH"
        );
        var nodeVersion = Environment.GetEnvironmentVariable(
            "CODEXCLIPLUS_LOCAL_ENV_NODE_VERSION"
        );
        var codexVersion = Environment.GetEnvironmentVariable(
            "CODEXCLIPLUS_LOCAL_ENV_CODEX_VERSION"
        );
        var npmCachePath = Environment.GetEnvironmentVariable(
            "CODEXCLIPLUS_LOCAL_ENV_NPM_CACHE_PATH"
        );

        if (
            string.IsNullOrWhiteSpace(nodeMsiPath)
            || string.IsNullOrWhiteSpace(nodeVersion)
            || string.IsNullOrWhiteSpace(codexVersion)
            || string.IsNullOrWhiteSpace(npmCachePath)
        )
        {
            return false;
        }

        if (!File.Exists(nodeMsiPath))
        {
            throw new FileNotFoundException("Configured local environment Node MSI not found.", nodeMsiPath);
        }

        if (!Directory.Exists(npmCachePath))
        {
            throw new DirectoryNotFoundException(
                $"Configured local environment npm cache not found: {npmCachePath}"
            );
        }

        var nodeArchitecture = ResolveNodeArchitecture(context.Options.Runtime);
        var nodeDirectory = Path.Combine(targetRoot, "node");
        Directory.CreateDirectory(nodeDirectory);
        var nodeTargetPath = Path.Combine(nodeDirectory, Path.GetFileName(nodeMsiPath));
        File.Copy(nodeMsiPath, nodeTargetPath, overwrite: true);

        var cacheTargetPath = Path.Combine(targetRoot, "npm-cache");
        ResetDirectory(cacheTargetPath);
        CopyDirectory(npmCachePath, cacheTargetPath);
        RequireNonEmptyDirectory(cacheTargetPath, "Codex npm cache");

        await WriteManifestAsync(
            context,
            targetRoot,
            nodeVersion,
            nodeArchitecture,
            ToManifestPath(targetRoot, nodeTargetPath),
            await ComputeSha256Async(nodeTargetPath),
            codexVersion,
            ToManifestPath(targetRoot, cacheTargetPath)
        );
        context.Logger.Info("local environment assets generated from configured overrides");
        return true;
    }

    private static async Task BuildSyntheticAssetsAsync(BuildContext context, string targetRoot)
    {
        var nodeArchitecture = ResolveNodeArchitecture(context.Options.Runtime);
        var nodeDirectory = Path.Combine(targetRoot, "node");
        Directory.CreateDirectory(nodeDirectory);
        var nodeTargetPath = Path.Combine(nodeDirectory, $"node-v0.0.0-{nodeArchitecture}.msi");
        await File.WriteAllBytesAsync(nodeTargetPath, Encoding.UTF8.GetBytes("synthetic node msi"));

        var cachePath = Path.Combine(targetRoot, "npm-cache", "_cacache", "content-v2", "sha512");
        Directory.CreateDirectory(cachePath);
        await File.WriteAllTextAsync(Path.Combine(cachePath, "synthetic"), "synthetic codex cache");

        await WriteManifestAsync(
            context,
            targetRoot,
            "v0.0.0",
            nodeArchitecture,
            ToManifestPath(targetRoot, nodeTargetPath),
            await ComputeSha256Async(nodeTargetPath),
            "0.0.0",
            ToManifestPath(targetRoot, Path.Combine(targetRoot, "npm-cache"))
        );
        context.Logger.Info("local environment synthetic assets generated for test runner");
    }

    private static async Task WarmCodexNpmCacheAsync(
        BuildContext context,
        string codexVersion,
        string npmCachePath
    )
    {
        var workingDirectory = Path.Combine(
            context.Options.OutputRoot,
            "temp",
            "local-environment-npm"
        );
        SafeFileSystem.CleanDirectory(workingDirectory, context.Options.OutputRoot);
        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "package.json"), "{}");

        var exitCode = await context.ProcessRunner.RunAsync(
            ProcessExecutableResolver.ResolveNpmExecutable(),
            [
                "install",
                $"{CodexPackageName}@{codexVersion}",
                "--cache",
                npmCachePath,
                "--ignore-scripts",
                "--no-audit",
                "--fund=false",
            ],
            workingDirectory,
            context.Logger
        );
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to warm Codex npm cache with exit code {exitCode}."
            );
        }

        SafeFileSystem.DeleteDirectory(workingDirectory, context.Options.OutputRoot);
    }

    private static async Task<NodeLtsAsset> ResolveNodeLtsAsync(string architecture)
    {
        using var response = await SharedHttpClient.GetAsync(
            NodeDistributionIndexUri,
            HttpCompletionOption.ResponseHeadersRead
        );
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var requiredFile = $"win-{architecture}-msi";

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (
                !release.TryGetProperty("lts", out var lts)
                || lts.ValueKind is JsonValueKind.False or JsonValueKind.Null
                || !release.TryGetProperty("version", out var versionElement)
                || versionElement.GetString() is not { Length: > 0 } version
                || !release.TryGetProperty("files", out var files)
                || !files.EnumerateArray()
                    .Any(file =>
                        string.Equals(file.GetString(), requiredFile, StringComparison.Ordinal)
                    )
            )
            {
                continue;
            }

            var fileName = $"node-{version}-{architecture}.msi";
            return new NodeLtsAsset(
                version,
                architecture,
                fileName,
                new Uri($"{NodeDistributionBaseUrl}{version}/{fileName}")
            );
        }

        throw new InvalidOperationException(
            $"Could not resolve Node.js LTS MSI for architecture '{architecture}'."
        );
    }

    private static async Task<string> ResolveLatestCodexVersionAsync()
    {
        using var response = await SharedHttpClient.GetAsync(
            CodexRegistryMetadataUri,
            HttpCompletionOption.ResponseHeadersRead
        );
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        if (
            document.RootElement.TryGetProperty("dist-tags", out var tags)
            && tags.TryGetProperty("latest", out var latest)
            && latest.GetString() is { Length: > 0 } version
        )
        {
            return version;
        }

        throw new InvalidOperationException("Could not resolve latest @openai/codex version.");
    }

    private static async Task DownloadFileAsync(Uri uri, string destinationPath)
    {
        using var response = await SharedHttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead
        );
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true
        );
        await input.CopyToAsync(output);
    }

    private static async Task WriteManifestAsync(
        BuildContext context,
        string targetRoot,
        string nodeVersion,
        string nodeArchitecture,
        string nodeFileName,
        string nodeSha256,
        string codexVersion,
        string npmCachePath
    )
    {
        var manifest = new LocalEnvironmentAssetManifest
        {
            Schema = ManifestSchema,
            Runtime = context.Options.Runtime,
            GeneratedAt = DateTimeOffset.UtcNow,
            Node = new LocalEnvironmentNodeAssetManifest
            {
                Version = nodeVersion,
                Architecture = nodeArchitecture,
                FileName = nodeFileName,
                Sha256 = nodeSha256,
            },
            Codex = new LocalEnvironmentCodexAssetManifest
            {
                Version = codexVersion,
                NpmCachePath = npmCachePath,
            },
        };

        var manifestPath = Path.Combine(targetRoot, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        context.Logger.Info($"local environment manifest: {manifestPath}");
    }

    private static string ResolveNodeArchitecture(string runtime)
    {
        return runtime.ToLowerInvariant() switch
        {
            "win-x64" => "x64",
            "win-arm64" => "arm64",
            _ => throw new NotSupportedException(
                $"Local environment assets are only supported for Windows runtimes: {runtime}"
            ),
        };
    }

    private static bool ShouldBuildSyntheticAssets(BuildContext context)
    {
        if (
            string.Equals(
                Environment.GetEnvironmentVariable("CODEXCLIPLUS_BUILD_SYNTHETIC_LOCAL_ENVIRONMENT_ASSETS"),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return true;
        }

        return string.Equals(
            context.ProcessRunner.GetType().Assembly.GetName().Name,
            "CodexCliPlus.Tests",
            StringComparison.Ordinal
        );
    }

    private static void RequireNonEmptyDirectory(string path, string description)
    {
        if (!Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException($"{description} is empty.");
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
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var targetPath = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string ToManifestPath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private sealed record NodeLtsAsset(
        string Version,
        string Architecture,
        string FileName,
        Uri DownloadUri
    );

    private sealed class LocalEnvironmentAssetManifest
    {
        public int Schema { get; init; }

        public string Runtime { get; init; } = string.Empty;

        public DateTimeOffset GeneratedAt { get; init; }

        public LocalEnvironmentNodeAssetManifest Node { get; init; } = new();

        public LocalEnvironmentCodexAssetManifest Codex { get; init; } = new();
    }

    private sealed class LocalEnvironmentNodeAssetManifest
    {
        public string Version { get; init; } = string.Empty;

        public string Architecture { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string Sha256 { get; init; } = string.Empty;
    }

    private sealed class LocalEnvironmentCodexAssetManifest
    {
        public string Version { get; init; } = string.Empty;

        public string NpmCachePath { get; init; } = string.Empty;
    }
}
