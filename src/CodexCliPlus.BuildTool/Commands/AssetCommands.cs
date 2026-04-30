using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.BuildTool;

public static class AssetCommands
{
    private const string BackendSourceRepositoryUrl =
        "https://github.com/router-for-me/CLIProxyAPI.git";
    private const string BackendBuildDate = "2026-04-30";

    private sealed record BackendAssetFileMapping(string SourceFileName, string TargetFileName);

    private static readonly BackendAssetFileMapping[] RequiredBackendFiles =
    [
        new(BackendExecutableNames.ManagedExecutableFileName, BackendExecutableNames.ManagedExecutableFileName),
        new("LICENSE", "LICENSE"),
        new("README.md", "README.md"),
        new("README_CN.md", "README_CN.md"),
        new("config.example.yaml", "config.example.yaml"),
    ];

    private static readonly string[] RequiredBackendFileNames = RequiredBackendFiles
        .Select(file => file.TargetFileName)
        .ToArray();

    private static readonly string[] BackendCompatibilityTestPackages =
    [
        "./internal/api/handlers/management",
        "./internal/api/modules/amp",
        "./internal/translator/codex/claude",
        "./internal/translator/codex/openai/chat-completions",
        "./internal/translator/codex/openai/responses",
        "./internal/usage",
        "./internal/watcher/diff",
    ];

    public static async Task<int> FetchAssetsAsync(BuildContext context)
    {
        SafeFileSystem.CleanDirectory(context.AssetsRoot, context.Options.OutputRoot);
        var backendTarget = Path.Combine(context.AssetsRoot, "backend", "windows-x64");

        await BuildBackendFromRepositorySourceAsync(context, backendTarget);

        var manifest = await BuildAssetManifest.CreateAsync(
            context.Options.Version,
            context.Options.Runtime,
            context.BackendSourceRoot,
            context.AssetsRoot,
            cancellationToken: default
        );
        Directory.CreateDirectory(context.AssetsRoot);
        await manifest.WriteAsync(context.AssetManifestPath);
        context.Logger.Info($"asset manifest: {context.AssetManifestPath}");
        return 0;
    }

    public static async Task<int> VerifyAssetsAsync(BuildContext context)
    {
        if (!File.Exists(context.AssetManifestPath))
        {
            context.Logger.Info(
                $"asset manifest not found; fetching assets: {context.AssetManifestPath}"
            );
            var fetchCode = await FetchAssetsAsync(context);
            if (fetchCode != 0)
            {
                return fetchCode;
            }
        }

        var manifest = await BuildAssetManifest.ReadAsync(context.AssetManifestPath);
        var failures = manifest.Verify(context.AssetsRoot);
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                context.Logger.Error(failure);
            }

            return 1;
        }

        context.Logger.Info($"asset verification passed: {manifest.Files.Count} file(s)");
        return 0;
    }

    public static async Task<int> SyncBackendSourceAsync(BuildContext context)
    {
        RequireBackendSource(context.BackendSourceRoot);

        var upstreamRoot = Path.Combine(context.Options.OutputRoot, "temp", "backend-upstream-source");
        SafeFileSystem.CleanDirectory(upstreamRoot, context.Options.OutputRoot);

        await RunRequiredAsync(
            context,
            "git",
            ["clone", "--filter=blob:none", "--no-checkout", BackendSourceRepositoryUrl, upstreamRoot],
            context.Options.RepositoryRoot,
            "clone CLIProxyAPI source"
        );
        await RunRequiredAsync(
            context,
            "git",
            ["checkout", BackendReleaseMetadata.SourceCommit],
            upstreamRoot,
            "checkout pinned CLIProxyAPI commit"
        );

        await RunRequiredAsync(
            context,
            "go",
            ["test", ..BackendCompatibilityTestPackages],
            context.BackendSourceRoot,
            "run CodexCliPlus backend compatibility tests"
        );

        var report = BackendSourceSyncReport.Create(
            BackendReleaseMetadata.Version,
            BackendReleaseMetadata.ReleaseTag,
            BackendReleaseMetadata.SourceCommit,
            upstreamRoot,
            context.BackendSourceRoot
        );
        var reportPath = Path.Combine(context.Options.OutputRoot, "backend-source-sync-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        context.Logger.Info($"backend source sync report: {reportPath}");
        context.Logger.Info(
            "Review the upstream comparison, update resources/backend/source intentionally, then update resources/backend/backend-source-manifest.json."
        );
        return 0;
    }

    private static async Task BuildBackendFromRepositorySourceAsync(
        BuildContext context,
        string backendTarget
    )
    {
        var sourceRoot = context.BackendSourceRoot;
        RequireBackendSource(sourceRoot);
        Directory.CreateDirectory(backendTarget);

        foreach (
            var file in RequiredBackendFiles.Where(file =>
                !string.Equals(
                    file.TargetFileName,
                    BackendExecutableNames.ManagedExecutableFileName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            var sourcePath = Path.Combine(sourceRoot, file.SourceFileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"Backend source is missing required file '{file.SourceFileName}'.",
                    sourcePath
                );
            }

            File.Copy(sourcePath, Path.Combine(backendTarget, file.TargetFileName), overwrite: true);
            context.Logger.Info($"asset copied: backend/windows-x64/{file.TargetFileName}");
        }

        var executablePath = Path.Combine(
            backendTarget,
            BackendExecutableNames.ManagedExecutableFileName
        );
        if (File.Exists(executablePath))
        {
            File.Delete(executablePath);
        }

        await RunRequiredAsync(
            context,
            "go",
            ["mod", "download"],
            sourceRoot,
            "download backend source modules"
        );

        var shortCommit = BackendReleaseMetadata.SourceCommit[..7];
        var ldflags =
            $"-s -w -X main.Version={BackendReleaseMetadata.Version} "
            + $"-X main.Commit={shortCommit}-source "
            + $"-X main.BuildDate={BackendBuildDate}";
        await RunRequiredAsync(
            context,
            "go",
            ["build", "-trimpath", "-ldflags", ldflags, "-o", executablePath, "./cmd/server/"],
            sourceRoot,
            "build backend executable from repository source",
            new Dictionary<string, string?>
            {
                ["CGO_ENABLED"] = "0",
                ["GOOS"] = "windows",
                ["GOARCH"] = "amd64",
            }
        );

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Backend source build did not create {BackendExecutableNames.ManagedExecutableFileName}.",
                executablePath
            );
        }

        var sha256 = await ComputeSha256Async(executablePath);
        context.Logger.Info(
            $"asset built from source: backend/windows-x64/{BackendExecutableNames.ManagedExecutableFileName} ({sha256})"
        );
    }

    private static void RequireBackendSource(string sourceRoot)
    {
        var goModPath = Path.Combine(sourceRoot, "go.mod");
        var entrypointPath = Path.Combine(sourceRoot, "cmd", "server", "main.go");
        if (!File.Exists(goModPath) || !File.Exists(entrypointPath))
        {
            throw new DirectoryNotFoundException(
                $"Backend source is incomplete. Expected go.mod and cmd/server/main.go under '{sourceRoot}'."
            );
        }
    }

    private static async Task RunRequiredAsync(
        BuildContext context,
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string description,
        IReadOnlyDictionary<string, string?>? environment = null
    )
    {
        var exitCode = await context.ProcessRunner.RunAsync(
            fileName,
            arguments,
            workingDirectory,
            context.Logger,
            environment: environment
        );
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{description} failed with exit code {exitCode}.");
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static IReadOnlyList<string> RequiredFiles => RequiredBackendFileNames;

    private sealed record BackendSourceSyncReport(
        string Component,
        string Version,
        string ReleaseTag,
        string SourceCommit,
        DateTimeOffset GeneratedAtUtc,
        IReadOnlyList<string> LocalOnlyFiles,
        IReadOnlyList<string> UpstreamOnlyFiles,
        IReadOnlyList<string> CommonFiles
    )
    {
        public static BackendSourceSyncReport Create(
            string version,
            string releaseTag,
            string sourceCommit,
            string upstreamRoot,
            string localRoot
        )
        {
            var upstreamFiles = EnumerateSourceFiles(upstreamRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var localFiles = EnumerateSourceFiles(localRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new BackendSourceSyncReport(
                "CLIProxyAPI",
                version,
                releaseTag,
                sourceCommit,
                DateTimeOffset.UtcNow,
                localFiles.Except(upstreamFiles, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                upstreamFiles.Except(localFiles, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                localFiles.Intersect(upstreamFiles, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            );
        }

        private static IEnumerable<string> EnumerateSourceFiles(string root)
        {
            return Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Where(path =>
                    !path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals(
                        BackendExecutableNames.ManagedExecutableFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !path.Equals(
                        BackendExecutableNames.UpstreamExecutableFileName,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
        }
    }
}
