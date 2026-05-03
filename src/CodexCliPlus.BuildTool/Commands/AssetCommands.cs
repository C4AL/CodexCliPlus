using System.Security.Cryptography;
using System.Text;
using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.BuildTool;

public static class AssetCommands
{
    private const string BackendBuildDate = "2026-05-02";

    private sealed record BackendAssetFileMapping(string SourceFileName, string TargetFileName);

    private static readonly BackendAssetFileMapping[] RequiredBackendFiles =
    [
        new(
            BackendExecutableNames.ManagedExecutableFileName,
            BackendExecutableNames.ManagedExecutableFileName
        ),
        new("LICENSE", "LICENSE"),
        new("README.md", "README.md"),
        new("README_CN.md", "README_CN.md"),
        new("config.example.yaml", "config.example.yaml"),
    ];

    private static readonly string[] RequiredBackendFileNames = RequiredBackendFiles
        .Select(file => file.TargetFileName)
        .ToArray();

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

            File.Copy(
                sourcePath,
                Path.Combine(backendTarget, file.TargetFileName),
                overwrite: true
            );
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

        var shortCommit = BackendReleaseMetadata.SourceCommit[..7];
        var ldflags =
            $"-s -w -X main.Version={BackendReleaseMetadata.Version} "
            + $"-X main.Commit={shortCommit}-source "
            + $"-X main.BuildDate={BackendBuildDate}";
        await RunRequiredAsync(
            context,
            "go",
            [
                "build",
                "-mod=readonly",
                "-trimpath",
                "-ldflags",
                ldflags,
                "-o",
                executablePath,
                "./cmd/server/",
            ],
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
        var goSumPath = Path.Combine(sourceRoot, "go.sum");
        var entrypointPath = Path.Combine(sourceRoot, "cmd", "server", "main.go");
        if (!File.Exists(goModPath) || !File.Exists(goSumPath) || !File.Exists(entrypointPath))
        {
            throw new DirectoryNotFoundException(
                $"Backend source is incomplete. Expected go.mod, go.sum, and cmd/server/main.go under '{sourceRoot}'."
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
}
