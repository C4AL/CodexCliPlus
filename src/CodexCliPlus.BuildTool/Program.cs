using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Constants;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CodexCliPlus.BuildTool;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await BuildToolApp.ExecuteAsync(args);
    }
}

public static class BuildToolApp
{
    private static readonly string[] CommandNames =
    [
        "fetch-assets",
        "verify-assets",
        "build-webui",
        "publish",
        "package-offline-installer",
        "package-update",
        "verify-package",
        "write-checksums",
    ];

    public static async Task<int> ExecuteAsync(
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        IProcessRunner? processRunner = null,
        ISigningService? signingService = null
    )
    {
        standardOutput ??= Console.Out;
        standardError ??= Console.Error;
        var logger = new BuildLogger(standardOutput, standardError);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp(logger);
            return 0;
        }

        var parseResult = BuildOptions.TryParse(args, out var options, out var parseError);
        if (!parseResult || options is null)
        {
            logger.Error(parseError ?? "Invalid BuildTool arguments.");
            WriteHelp(logger);
            return 2;
        }

        if (!CommandNames.Contains(options.Command, StringComparer.OrdinalIgnoreCase))
        {
            logger.Error($"Unknown command: {options.Command}");
            WriteHelp(logger);
            return 1;
        }

        try
        {
            processRunner ??= new ProcessRunner();
            signingService ??= SigningServiceFactory.CreateFromEnvironment();

            var context = new BuildContext(options, logger, processRunner, signingService);
            logger.Info($"CodexCliPlus.BuildTool {options.Command}");
            logger.Info($"Repository: {options.RepositoryRoot}");
            logger.Info($"Output: {options.OutputRoot}");
            logger.Info(
                $"Configuration: {options.Configuration}; Runtime: {options.Runtime}; Version: {options.Version}"
            );

            return options.Command.ToLowerInvariant() switch
            {
                "fetch-assets" => await AssetCommands.FetchAssetsAsync(context),
                "verify-assets" => await AssetCommands.VerifyAssetsAsync(context),
                "build-webui" => await WebUiCommands.BuildVendoredAsync(context),
                "publish" => await PublishCommands.PublishAsync(context),
                "package-offline-installer" => await PackageCommands.PackageInstallerAsync(
                    context,
                    InstallerPackageKind.Offline
                ),
                "package-update" => await PackageCommands.PackageUpdateAsync(context),
                "verify-package" => await PackageCommands.VerifyPackagesAsync(context),
                "write-checksums" => await ReleaseArtifactCommands.WriteChecksumsAsync(context),
                _ => 1,
            };
        }
        catch (Exception exception)
        {
            logger.Error(exception.Message);
            return 1;
        }
    }

    private static bool IsHelp(string arg)
    {
        return arg is "-h" or "--help" or "help";
    }

    private static void WriteHelp(BuildLogger logger)
    {
        logger.Info("CodexCliPlus.BuildTool");
        logger.Info("Commands:");
        foreach (var command in CommandNames)
        {
            logger.Info($"  {command}");
        }

        logger.Info("Options:");
        logger.Info("  --configuration <Debug|Release>  Default: Release");
        logger.Info("  --runtime <rid>                  Default: win-x64");
        logger.Info("  --version <version>              Default: 1.0.0");
        logger.Info("  --repo-root <path>               Default: auto-detected repository root");
        logger.Info("  --output <path>                  Default: <repo>/artifacts/buildtool");
    }
}

public sealed record BuildOptions(
    string Command,
    string RepositoryRoot,
    string OutputRoot,
    string Configuration,
    string Runtime,
    string Version
)
{
    public static bool TryParse(string[] args, out BuildOptions? options, out string? error)
    {
        options = null;
        error = null;

        if (args.Length == 0)
        {
            error = "Missing command.";
            return false;
        }

        var command = args[0];
        var repositoryRoot = FindRepositoryRoot();
        var outputRoot = Path.Combine(repositoryRoot, "artifacts", "buildtool");
        var configuration = "Release";
        var runtime = "win-x64";
        var version = "1.0.0";

        for (var index = 1; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument: {arg}";
                return false;
            }

            if (index + 1 >= args.Length)
            {
                error = $"Missing value for option {arg}.";
                return false;
            }

            var value = args[++index];
            switch (arg)
            {
                case "--configuration":
                    configuration = value;
                    break;
                case "--runtime":
                    runtime = value;
                    break;
                case "--version":
                    version = value;
                    break;
                case "--repo-root":
                    repositoryRoot = Path.GetFullPath(value);
                    break;
                case "--output":
                    outputRoot = Path.GetFullPath(value);
                    break;
                default:
                    error = $"Unknown option: {arg}";
                    return false;
            }
        }

        options = new BuildOptions(
            command,
            Path.GetFullPath(repositoryRoot),
            Path.GetFullPath(outputRoot),
            configuration,
            runtime,
            version
        );
        return true;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodexCliPlus.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

public sealed class BuildContext(
    BuildOptions options,
    BuildLogger logger,
    IProcessRunner processRunner,
    ISigningService signingService
)
{
    public BuildOptions Options { get; } = options;

    public BuildLogger Logger { get; } = logger;

    public IProcessRunner ProcessRunner { get; } = processRunner;

    public ISigningService SigningService { get; } = signingService;

    public string AssetsRoot => Path.Combine(Options.OutputRoot, "assets");

    public string AssetManifestPath => Path.Combine(AssetsRoot, "asset-manifest.json");

    public string WebUiAssetsRoot => Path.Combine(AssetsRoot, "webui");

    public string WebUiGeneratedRoot => Path.Combine(WebUiAssetsRoot, "upstream");

    public string WebUiGeneratedDistRoot => Path.Combine(WebUiGeneratedRoot, "dist");

    public string WebUiGeneratedSyncMetadataPath => Path.Combine(WebUiGeneratedRoot, "sync.json");

    public string PublishRoot => Path.Combine(Options.OutputRoot, "publish", Options.Runtime);

    public string PackageRoot => Path.Combine(Options.OutputRoot, "packages");

    public string ChecksumsPath => Path.Combine(Options.OutputRoot, "SHA256SUMS.txt");

    public string ReleaseManifestPath => Path.Combine(Options.OutputRoot, "release-manifest.json");

    public string InstallerRoot => Path.Combine(Options.OutputRoot, "installer", Options.Runtime);

    public string ToolsRoot => Path.Combine(Options.OutputRoot, "tools");

    public string WebUiRoot =>
        Path.Combine(Options.RepositoryRoot, "resources", "webui", "upstream");

    public string WebUiModulesRoot =>
        Path.Combine(Options.RepositoryRoot, "resources", "webui", "modules");

    public string WebUiOverlayModuleRoot => Path.Combine(WebUiModulesRoot, "cpa-uv-overlay");

    public string WebUiOverlayManifestPath => Path.Combine(WebUiOverlayModuleRoot, "module.json");

    public string WebUiOverlaySourceRoot => Path.Combine(WebUiOverlayModuleRoot, "source");

    public string WebUiSourceRoot => Path.Combine(WebUiRoot, "source");

    public string WebUiSourcePackageJsonPath => Path.Combine(WebUiSourceRoot, "package.json");

    public string WebUiSourcePackageLockPath => Path.Combine(WebUiSourceRoot, "package-lock.json");

    public string WebUiSourceNodeModulesRoot => Path.Combine(WebUiSourceRoot, "node_modules");

    public string WebUiSourceDistRoot => Path.Combine(WebUiSourceRoot, "dist");

    public string WebUiSyncMetadataPath => Path.Combine(WebUiRoot, "sync.json");

    public string WebUiBuildRoot => Path.Combine(Options.OutputRoot, "temp", "webui");

    public string WebUiBuildSourceRoot => Path.Combine(WebUiBuildRoot, "source");

    public string WebUiBuildDistRoot => Path.Combine(WebUiBuildRoot, "dist");
}

public sealed class BuildLogger(TextWriter standardOutput, TextWriter standardError)
{
    public void Info(string message)
    {
        standardOutput.WriteLine($"[info] {message}");
    }

    public void Warning(string message)
    {
        standardOutput.WriteLine($"[warn] {message}");
    }

    public void Error(string message)
    {
        standardError.WriteLine($"[error] {message}");
    }
}

public interface IProcessRunner
{
    Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        BuildLogger logger,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        BuildLogger logger,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        var standardErrorLines = new List<string>();
        var standardErrorLock = new object();

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                logger.Info(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                lock (standardErrorLock)
                {
                    standardErrorLines.Add(eventArgs.Data);
                }
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit();

        foreach (var line in standardErrorLines)
        {
            if (process.ExitCode == 0)
            {
                logger.Warning(line);
            }
            else
            {
                logger.Error(line);
            }
        }

        return process.ExitCode;
    }
}

public interface ISigningService
{
    Task SignAsync(
        string artifactPath,
        BuildContext context,
        CancellationToken cancellationToken = default
    );
}

public sealed class NoOpSigningService : ISigningService
{
    public async Task SignAsync(
        string artifactPath,
        BuildContext context,
        CancellationToken cancellationToken = default
    )
    {
        await ArtifactSignatureMetadata.WriteUnsignedAsync(
            artifactPath,
            "No signing certificate configured for this local build.",
            cancellationToken
        );
    }
}

public static class AssetCommands
{
    private const string BackendSourceRepositoryUrl =
        "https://github.com/router-for-me/CLIProxyAPI.git";

    private const string PatchedBackendBuildDate = "2026-04-29";

    private sealed record BackendAssetFileMapping(
        string RepositoryFileName,
        string ArchiveEntryName,
        string TargetFileName
    );

    private static readonly BackendAssetFileMapping[] RequiredBackendFiles =
    [
        new(
            BackendExecutableNames.ManagedExecutableFileName,
            BackendExecutableNames.UpstreamExecutableFileName,
            BackendExecutableNames.ManagedExecutableFileName
        ),
        new("LICENSE", "LICENSE", "LICENSE"),
        new("README.md", "README.md", "README.md"),
        new("README_CN.md", "README_CN.md", "README_CN.md"),
        new("config.example.yaml", "config.example.yaml", "config.example.yaml"),
    ];

    private static readonly string[] RequiredBackendFileNames = RequiredBackendFiles
        .Select(file => file.TargetFileName)
        .ToArray();

    public static async Task<int> FetchAssetsAsync(BuildContext context)
    {
        var sourceDirectory = Path.Combine(
            context.Options.RepositoryRoot,
            "resources",
            "backend",
            "windows-x64"
        );
        SafeFileSystem.CleanDirectory(context.AssetsRoot, context.Options.OutputRoot);
        var backendTarget = Path.Combine(context.AssetsRoot, "backend", "windows-x64");

        if (
            Directory.Exists(sourceDirectory)
            && RequiredBackendFiles.All(file =>
                File.Exists(Path.Combine(sourceDirectory, file.RepositoryFileName))
            )
        )
        {
            foreach (var file in RequiredBackendFiles)
            {
                var sourcePath = Path.Combine(sourceDirectory, file.RepositoryFileName);
                Directory.CreateDirectory(backendTarget);
                File.Copy(
                    sourcePath,
                    Path.Combine(backendTarget, file.TargetFileName),
                    overwrite: true
                );
                context.Logger.Info($"asset copied: backend/windows-x64/{file.TargetFileName}");
            }
        }
        else
        {
            if (!BackendReleaseMetadata.RemoteArchiveFallbackEnabled)
            {
                context.Logger.Info(
                    "local patched backend resources are incomplete; building patched backend from source."
                );
                await BuildPatchedBackendFromSourceAsync(context, backendTarget);
                sourceDirectory =
                    $"{BackendSourceRepositoryUrl}@{BackendReleaseMetadata.SourceCommit}";
            }
            else
            {
                context.Logger.Info(
                    $"local backend resources are incomplete; downloading {BackendReleaseMetadata.ArchiveUrl}"
                );
                await DownloadBackendArchiveAsync(backendTarget, context.Logger);
                sourceDirectory = BackendReleaseMetadata.ArchiveUrl;
            }
        }

        var manifest = await BuildAssetManifest.CreateAsync(
            context.Options.Version,
            context.Options.Runtime,
            sourceDirectory,
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

    private static async Task BuildPatchedBackendFromSourceAsync(
        BuildContext context,
        string backendTarget
    )
    {
        Directory.CreateDirectory(backendTarget);
        var sourceRoot = Path.Combine(context.Options.OutputRoot, "temp", "backend-source");
        SafeFileSystem.CleanDirectory(sourceRoot, context.Options.OutputRoot);

        await RunRequiredAsync(
            context,
            "git",
            ["clone", "--no-checkout", BackendSourceRepositoryUrl, sourceRoot],
            context.Options.RepositoryRoot,
            "clone CLIProxyAPI source"
        );
        await RunRequiredAsync(
            context,
            "git",
            ["checkout", BackendReleaseMetadata.SourceCommit],
            sourceRoot,
            "checkout pinned CLIProxyAPI commit"
        );

        ApplyPatchedBackendSourceChanges(sourceRoot);

        await RunRequiredAsync(
            context,
            "go",
            [
                "get",
                "github.com/jackc/pgx/v5@v5.9.2",
                "github.com/cloudflare/circl@v1.6.3",
                "github.com/go-git/go-git/v6@v6.0.0-alpha.2",
                "golang.org/x/crypto@v0.50.0",
                "golang.org/x/net@v0.53.0",
                "golang.org/x/sync@v0.20.0",
            ],
            sourceRoot,
            "apply patched backend dependency versions"
        );
        await RunRequiredAsync(
            context,
            "go",
            ["mod", "tidy"],
            sourceRoot,
            "tidy patched backend module"
        );
        await RunRequiredAsync(
            context,
            "gofmt",
            ["-w", Path.Combine("internal", "store", "gitstore.go")],
            sourceRoot,
            "format patched backend source"
        );

        var executablePath = Path.Combine(
            backendTarget,
            BackendExecutableNames.ManagedExecutableFileName
        );
        var shortCommit = BackendReleaseMetadata.SourceCommit[..7];
        var ldflags =
            $"-s -w -X 'main.Version={BackendReleaseMetadata.Version}' "
            + $"-X 'main.Commit={shortCommit}-deps' "
            + $"-X 'main.BuildDate={PatchedBackendBuildDate}'";
        await RunRequiredAsync(
            context,
            "go",
            ["build", "-trimpath", "-ldflags", ldflags, "-o", executablePath, "./cmd/server/"],
            sourceRoot,
            "build patched backend executable",
            new Dictionary<string, string?>
            {
                ["CGO_ENABLED"] = "0",
                ["GOOS"] = "windows",
                ["GOARCH"] = "amd64",
            }
        );

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
            File.Copy(
                Path.Combine(sourceRoot, file.ArchiveEntryName),
                Path.Combine(backendTarget, file.TargetFileName),
                overwrite: true
            );
            context.Logger.Info($"asset built: backend/windows-x64/{file.TargetFileName}");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Patched backend build did not create {BackendExecutableNames.ManagedExecutableFileName}.",
                executablePath
            );
        }

        context.Logger.Info(
            $"asset built: backend/windows-x64/{BackendExecutableNames.ManagedExecutableFileName}"
        );
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

    private static void ApplyPatchedBackendSourceChanges(string sourceRoot)
    {
        var gitStorePath = Path.Combine(sourceRoot, "internal", "store", "gitstore.go");
        var source = File.ReadAllText(gitStorePath, Encoding.UTF8).Replace("\r\n", "\n");
        source = ReplaceRequired(
            source,
            "\"github.com/go-git/go-git/v6/plumbing\"\n",
            "\"github.com/go-git/go-git/v6/plumbing\"\n\t\"github.com/go-git/go-git/v6/plumbing/client\"\n"
        );
        source = ReplaceRequired(source, "Auth: authMethod", "ClientOptions: authMethod");
        source = ReplaceRequired(source, "Auth: s.gitAuth()", "ClientOptions: s.gitAuth()");
        source = ReplaceRequired(source, "transport.AuthMethod", "[]client.Option");
        source = ReplaceRequired(
            source,
            "return &http.BasicAuth{Username: user, Password: s.password}",
            "return []client.Option{client.WithHTTPAuth(&http.BasicAuth{Username: user, Password: s.password})}"
        );
        File.WriteAllText(gitStorePath, source, new UTF8Encoding(false));
    }

    private static string ReplaceRequired(string source, string oldValue, string newValue)
    {
        if (!source.Contains(oldValue, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Pinned backend source no longer contains expected patch fragment: {oldValue}"
            );
        }

        return source.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static async Task DownloadBackendArchiveAsync(string backendTarget, BuildLogger logger)
    {
        Directory.CreateDirectory(backendTarget);

        using var httpClient = new HttpClient();
        byte[]? archiveBytes = null;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                logger.Info($"backend archive download attempt {attempt}/3");
                archiveBytes = await httpClient.GetByteArrayAsync(
                    BackendReleaseMetadata.ArchiveUrl
                );
                break;
            }
            catch (Exception exception)
            {
                lastError = exception;
                logger.Error(
                    $"backend archive download attempt {attempt}/3 failed: {exception.Message}"
                );
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                }
            }
        }

        if (archiveBytes is null)
        {
            throw new InvalidOperationException(
                $"Failed to download backend archive after 3 attempts: {lastError?.Message}",
                lastError
            );
        }

        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
        if (
            !string.Equals(
                actualSha256,
                BackendReleaseMetadata.ArchiveSha256,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidDataException(
                $"Downloaded backend archive hash mismatch. Expected {BackendReleaseMetadata.ArchiveSha256}, got {actualSha256}."
            );
        }

        using var archiveStream = new MemoryStream(archiveBytes);
        ExtractBackendArchive(archiveStream, backendTarget, logger);
    }

    private static void ExtractBackendArchive(
        Stream archiveStream,
        string backendTarget,
        BuildLogger logger
    )
    {
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        foreach (var requiredFile in RequiredBackendFiles)
        {
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(
                    item.Name,
                    requiredFile.ArchiveEntryName,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (entry is null)
            {
                throw new InvalidDataException(
                    $"Downloaded backend archive is missing {requiredFile.ArchiveEntryName}."
                );
            }

            entry.ExtractToFile(
                Path.Combine(backendTarget, requiredFile.TargetFileName),
                overwrite: true
            );
            logger.Info($"asset downloaded: backend/windows-x64/{requiredFile.TargetFileName}");
        }
    }

    public static IReadOnlyList<string> RequiredFiles => RequiredBackendFileNames;
}

public static class WebUiCommands
{
    public static async Task<int> BuildVendoredAsync(BuildContext context)
    {
        EnsureVendoredLayout(context);

        context.Logger.Info("preparing vendored WebUI temporary build worktree");
        SafeFileSystem.CleanDirectory(context.WebUiBuildRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(
            context.WebUiSourceRoot,
            context.WebUiBuildSourceRoot,
            excludedRootDirectoryNames: ["node_modules", "dist"]
        );
        SafeFileSystem.CopyDirectory(context.WebUiOverlaySourceRoot, context.WebUiBuildSourceRoot);

        context.Logger.Info("installing vendored WebUI dependencies in temporary worktree");
        var installExitCode = await context.ProcessRunner.RunAsync(
            ResolveNpmExecutable(),
            ["ci"],
            context.WebUiBuildSourceRoot,
            context.Logger
        );
        if (installExitCode != 0)
        {
            context.Logger.Error($"npm ci failed with exit code {installExitCode}.");
            return installExitCode;
        }

        context.Logger.Info("building vendored WebUI");
        var buildExitCode = await context.ProcessRunner.RunAsync(
            ResolveNpmExecutable(),
            ["run", "build"],
            context.WebUiBuildSourceRoot,
            context.Logger
        );
        if (buildExitCode != 0)
        {
            context.Logger.Error($"npm run build failed with exit code {buildExitCode}.");
            return buildExitCode;
        }

        var builtEntryPath = Path.Combine(context.WebUiBuildDistRoot, "index.html");
        if (!Directory.Exists(context.WebUiBuildDistRoot) || !File.Exists(builtEntryPath))
        {
            context.Logger.Error($"vendored WebUI build output is missing: {builtEntryPath}");
            return 1;
        }

        SafeFileSystem.CleanDirectory(context.WebUiGeneratedRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.WebUiBuildDistRoot, context.WebUiGeneratedDistRoot);
        Directory.CreateDirectory(context.WebUiGeneratedRoot);
        File.Copy(
            context.WebUiSyncMetadataPath,
            context.WebUiGeneratedSyncMetadataPath,
            overwrite: true
        );
        context.Logger.Info($"vendored WebUI dist generated: {context.WebUiGeneratedDistRoot}");
        return 0;
    }

    private static void EnsureVendoredLayout(BuildContext context)
    {
        if (!Directory.Exists(context.WebUiSourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI source directory not found: {context.WebUiSourceRoot}"
            );
        }

        if (!File.Exists(context.WebUiSourcePackageJsonPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI package.json not found: {context.WebUiSourcePackageJsonPath}",
                context.WebUiSourcePackageJsonPath
            );
        }

        if (!File.Exists(context.WebUiSourcePackageLockPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI package-lock.json not found: {context.WebUiSourcePackageLockPath}",
                context.WebUiSourcePackageLockPath
            );
        }

        if (!File.Exists(context.WebUiSyncMetadataPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI sync metadata not found: {context.WebUiSyncMetadataPath}",
                context.WebUiSyncMetadataPath
            );
        }

        if (!Directory.Exists(context.WebUiOverlayModuleRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI overlay directory not found: {context.WebUiOverlayModuleRoot}"
            );
        }

        if (!File.Exists(context.WebUiOverlayManifestPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI overlay manifest not found: {context.WebUiOverlayManifestPath}",
                context.WebUiOverlayManifestPath
            );
        }

        if (!Directory.Exists(context.WebUiOverlaySourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI overlay source directory not found: {context.WebUiOverlaySourceRoot}"
            );
        }
    }

    private static string ResolveNpmExecutable()
    {
        var candidateNames = OperatingSystem.IsWindows()
            ? new[] { "npm.cmd", "npm.exe", "npm" }
            : new[] { "npm" };

        foreach (var candidateName in candidateNames)
        {
            var resolvedPath = TryResolveFromPath(candidateName);
            if (resolvedPath is not null)
            {
                return resolvedPath;
            }
        }

        return candidateNames[0];
    }

    private static string? TryResolveFromPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (
            var segment in pathValue.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var candidatePath = Path.Combine(segment.Trim('"'), fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }
}

public static class PublishCommands
{
    public static async Task<int> PublishAsync(BuildContext context)
    {
        var verifyCode = await AssetCommands.VerifyAssetsAsync(context);
        if (verifyCode != 0)
        {
            return verifyCode;
        }

        var webUiBuildCode = await WebUiCommands.BuildVendoredAsync(context);
        if (webUiBuildCode != 0)
        {
            return webUiBuildCode;
        }

        SafeFileSystem.CleanDirectory(context.PublishRoot, context.Options.OutputRoot);

        var appProject = Path.Combine(
            context.Options.RepositoryRoot,
            "src",
            "CodexCliPlus.App",
            "CodexCliPlus.App.csproj"
        );
        var arguments = new[]
        {
            "publish",
            appProject,
            "--configuration",
            context.Options.Configuration,
            "--runtime",
            context.Options.Runtime,
            "--self-contained",
            "true",
            "--output",
            context.PublishRoot,
            "/p:PublishSingleFile=false",
        };
        var exitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            arguments,
            context.Options.RepositoryRoot,
            context.Logger
        );
        if (exitCode != 0)
        {
            context.Logger.Error($"dotnet publish failed with exit code {exitCode}.");
            return exitCode;
        }

        SafeFileSystem.CopyDirectory(
            Path.Combine(context.AssetsRoot, "backend"),
            Path.Combine(context.PublishRoot, "assets", "backend")
        );
        SafeFileSystem.CopyDirectory(
            context.WebUiAssetsRoot,
            Path.Combine(context.PublishRoot, "assets", "webui")
        );
        var updaterExitCode = await PublishUpdaterAsync(context);
        if (updaterExitCode != 0)
        {
            return updaterExitCode;
        }

        await context.SigningService.SignAsync(
            Path.Combine(context.PublishRoot, AppConstants.ExecutableName),
            context
        );
        CopyBackendLicenseDocument(context);
        await WritePublishManifestAsync(context);
        context.Logger.Info($"publish output: {context.PublishRoot}");
        return 0;
    }

    private static async Task<int> PublishUpdaterAsync(BuildContext context)
    {
        var updaterProject = Path.Combine(
            context.Options.RepositoryRoot,
            "src",
            "CodexCliPlus.Updater",
            "CodexCliPlus.Updater.csproj"
        );
        var updaterOutput = Path.Combine(context.PublishRoot, "updater");
        var exitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            [
                "publish",
                updaterProject,
                "--configuration",
                context.Options.Configuration,
                "--runtime",
                context.Options.Runtime,
                "--self-contained",
                "true",
                "--output",
                updaterOutput,
                "/p:PublishSingleFile=false",
            ],
            context.Options.RepositoryRoot,
            context.Logger
        );
        if (exitCode != 0)
        {
            context.Logger.Error($"dotnet publish updater failed with exit code {exitCode}.");
            return exitCode;
        }

        await context.SigningService.SignAsync(
            Path.Combine(updaterOutput, "CodexCliPlus.Updater.exe"),
            context
        );
        return 0;
    }

    private static void CopyBackendLicenseDocument(BuildContext context)
    {
        var source = Path.Combine(context.AssetsRoot, "backend", "windows-x64", "LICENSE");
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "Backend license missing from verified assets.",
                source
            );
        }

        var targetDirectory = Path.Combine(context.PublishRoot, "Licenses");
        Directory.CreateDirectory(targetDirectory);
        File.Copy(
            source,
            Path.Combine(targetDirectory, "CLIProxyAPI.LICENSE.txt"),
            overwrite: true
        );
    }

    private static async Task WritePublishManifestAsync(BuildContext context)
    {
        var manifest = new PublishManifest
        {
            Product = AppConstants.ProductName,
            Version = context.Options.Version,
            Runtime = context.Options.Runtime,
            Configuration = context.Options.Configuration,
            Application = AppConstants.ExecutableName,
            AssetsManifest = Path.GetRelativePath(context.PublishRoot, context.AssetManifestPath),
        };
        await File.WriteAllTextAsync(
            Path.Combine(context.PublishRoot, "publish-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }
}

public static class PackageCommands
{
    public static async Task<int> PackageInstallerAsync(
        BuildContext context,
        InstallerPackageKind packageKind
    )
    {
        if (packageKind != InstallerPackageKind.Offline)
        {
            throw new NotSupportedException("Only offline installer packaging is supported.");
        }

        SafeFileSystem.RequirePublishRoot(context.PublishRoot);
        var packageMoniker = "Offline";
        var packageType = "offline-installer";
        var stageRoot = Path.Combine(context.InstallerRoot, packageType, "stage");
        var appPackageRoot = Path.Combine(stageRoot, "app-package");
        var payloadArchivePath = Path.Combine(stageRoot, "publish.7z");
        var installerOutputPath = Path.Combine(
            context.PackageRoot,
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.exe"
        );

        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, appPackageRoot);

        var installerPlan = new InstallerPlan
        {
            ProductName = AppConstants.ProductName,
            InstallerName = Path.GetFileName(installerOutputPath),
            AppUserModelId = AppConstants.AppUserModelId,
            CurrentUserDefault = false,
            PayloadDirectory = "app-package",
            MicaSetupRoute = true,
            RequestExecutionLevel = "admin",
            InstallDirectoryHint = $"%ProgramFiles%\\{AppConstants.ProductKey}",
            LaunchAfterInstall = true,
            StableReleaseSource = "https://github.com/C4AL/CodexCliPlus/releases/latest",
            BetaChannelReserved = true,
        };
        await WriteJsonAsync(Path.Combine(stageRoot, "mica-setup.json"), installerPlan);
        await InstallerMetadata.WriteAsync(context, appPackageRoot, stageRoot);

        var toolchain = await MicaSetupToolchain.AcquireAsync(context);
        var archiveExitCode = await CreateMicaPayloadArchiveAsync(
            context,
            toolchain,
            appPackageRoot,
            payloadArchivePath
        );
        if (archiveExitCode != 0)
        {
            return archiveExitCode;
        }

        var micaConfigPath = Path.Combine(stageRoot, "micasetup.json");
        var micaConfig = MicaSetupConfig.Create(context, payloadArchivePath, installerOutputPath);
        await File.WriteAllTextAsync(
            micaConfigPath,
            JsonSerializer.Serialize(micaConfig, MicaSetupConfig.JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        var builder = new MicaSetupInstallerBuilder(toolchain);
        var buildExitCode = await builder.BuildAsync(
            context,
            micaConfigPath,
            payloadArchivePath,
            installerOutputPath
        );
        if (buildExitCode != 0)
        {
            return buildExitCode;
        }

        await context.SigningService.SignAsync(installerOutputPath, context);
        Directory.CreateDirectory(Path.Combine(stageRoot, "output"));
        File.Copy(
            installerOutputPath,
            Path.Combine(stageRoot, "output", Path.GetFileName(installerOutputPath)),
            overwrite: true
        );
        CopySigningMetadataIfExists(
            installerOutputPath,
            Path.Combine(stageRoot, "output", Path.GetFileName(installerOutputPath))
        );

        var stagingPackagePath = Path.Combine(
            context.PackageRoot,
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        await CreatePackageAsync(context, stageRoot, stagingPackagePath, packageType);
        context.Logger.Info($"installer executable: {installerOutputPath}");
        return 0;
    }

    public static Task<int> VerifyPackagesAsync(BuildContext context)
    {
        var verifier = new PackageVerifier(context);
        var failures = verifier.VerifyAll();
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                context.Logger.Error(failure);
            }

            return Task.FromResult(1);
        }

        context.Logger.Info("package verification passed");
        return Task.FromResult(0);
    }

    public static async Task<int> PackageUpdateAsync(BuildContext context)
    {
        SafeFileSystem.RequirePublishRoot(context.PublishRoot);

        var stageRoot = Path.Combine(context.InstallerRoot, "update-package", "stage");
        var payloadRoot = Path.Combine(stageRoot, "payload");
        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, payloadRoot);

        var files = Directory
            .EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderBy(
                file => Path.GetRelativePath(payloadRoot, file.FullName),
                StringComparer.OrdinalIgnoreCase
            )
            .ToArray();
        var entries = new List<object>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(payloadRoot, file.FullName).Replace('\\', '/');
            entries.Add(
                new
                {
                    path = relativePath,
                    size = file.Length,
                    sha256 = await ComputeSha256Async(file.FullName),
                }
            );
        }

        var manifest = new
        {
            product = AppConstants.ProductName,
            version = context.Options.Version,
            runtime = context.Options.Runtime,
            createdAtUtc = DateTimeOffset.UtcNow,
            updateKind = "file-manifest-diff",
            signing = SigningOptions.FromEnvironment().SigningRequired
                ? "required"
                : "optional-unsigned",
            files = entries,
        };

        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "update-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        Directory.CreateDirectory(context.PackageRoot);
        var packagePath = Path.Combine(
            context.PackageRoot,
            $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(
            stageRoot,
            packagePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false
        );
        await context.SigningService.SignAsync(packagePath, context);
        context.Logger.Info($"update package: {packagePath}");
        return 0;
    }

    private static async Task CreatePackageAsync(
        BuildContext context,
        string stageRoot,
        string packagePath,
        string packageType
    )
    {
        Directory.CreateDirectory(context.PackageRoot);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        var manifest = new PackageManifest
        {
            Product = AppConstants.ProductName,
            Version = context.Options.Version,
            Runtime = context.Options.Runtime,
            PackageType = packageType,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "package-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        ZipFile.CreateFromDirectory(
            stageRoot,
            packagePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false
        );
        await context.SigningService.SignAsync(packagePath, context);
        context.Logger.Info($"{packageType} package: {packagePath}");
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static void CopySigningMetadataIfExists(
        string sourceArtifactPath,
        string targetArtifactPath
    )
    {
        foreach (
            var metadataPath in new[]
            {
                ArtifactSignatureMetadata.GetSignaturePath(sourceArtifactPath),
                ArtifactSignatureMetadata.GetUnsignedPath(sourceArtifactPath),
            }
        )
        {
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            var targetMetadataPath = metadataPath.EndsWith(
                ".signature.json",
                StringComparison.OrdinalIgnoreCase
            )
                ? ArtifactSignatureMetadata.GetSignaturePath(targetArtifactPath)
                : ArtifactSignatureMetadata.GetUnsignedPath(targetArtifactPath);
            File.Copy(metadataPath, targetMetadataPath, overwrite: true);
        }
    }

    private static async Task<int> CreateMicaPayloadArchiveAsync(
        BuildContext context,
        MicaSetupToolchain toolchain,
        string appPackageRoot,
        string archivePath
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        var exitCode = await context.ProcessRunner.RunAsync(
            toolchain.SevenZipPath,
            ["a", archivePath, ".\\*", "-t7z", "-mx=5", "-mf=BCJ2", "-r", "-y"],
            appPackageRoot,
            context.Logger
        );
        if (exitCode == 0 && File.Exists(archivePath))
        {
            return 0;
        }

        context.Logger.Error(
            "7z did not create a MicaSetup payload archive; falling back to a zip container with the same payload."
        );
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(
            appPackageRoot,
            archivePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false
        );
        return File.Exists(archivePath) ? 0
            : exitCode == 0 ? 1
            : exitCode;
    }

    private static Task WriteJsonAsync(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }
}

public enum InstallerPackageKind
{
    Offline,
}

public static class ReleaseArtifactCommands
{
    public static async Task<int> WriteChecksumsAsync(BuildContext context)
    {
        var files = EnumerateReleaseFiles(context)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                path => ToRepositoryRelativePath(context, path),
                StringComparer.OrdinalIgnoreCase
            )
            .ToArray();

        if (files.Length == 0)
        {
            context.Logger.Error("No release artifacts found for checksum generation.");
            return 1;
        }

        var artifacts = new List<object>();
        var checksumLines = new List<string>();
        foreach (var file in files)
        {
            var sha256 = await ComputeSha256Async(file);
            var relativePath = ToRepositoryRelativePath(context, file);
            var signature = await ArtifactSignatureMetadata.ReadForArtifactAsync(file);
            var signatureMetadataPath = signature is null
                ? null
                : ToRepositoryRelativePath(context, signature.MetadataPath);
            checksumLines.Add($"{sha256}  {relativePath}");
            artifacts.Add(
                new
                {
                    path = relativePath,
                    size = new FileInfo(file).Length,
                    sha256,
                    signed = signature?.Metadata.HasSignature ?? false,
                    signatureKind = signature?.Metadata.SignatureKind ?? "none",
                    signatureMetadataPath,
                    attestationExpected = signature?.Metadata.AttestationExpected ?? true,
                }
            );
        }

        Directory.CreateDirectory(context.Options.OutputRoot);
        await File.WriteAllLinesAsync(
            context.ChecksumsPath,
            checksumLines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        var manifest = new
        {
            product = AppConstants.ProductName,
            version = context.Options.Version,
            runtime = context.Options.Runtime,
            configuration = context.Options.Configuration,
            generatedAtUtc = DateTimeOffset.UtcNow,
            signing = SigningOptions.FromEnvironment().SigningRequired
                ? "required"
                : "unsigned-or-optional",
            attestation = new { provider = "github-artifact-attestation", expected = true },
            artifacts,
        };
        await File.WriteAllTextAsync(
            context.ReleaseManifestPath,
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        context.Logger.Info($"checksums: {context.ChecksumsPath}");
        context.Logger.Info($"release manifest: {context.ReleaseManifestPath}");
        return 0;
    }

    private static IEnumerable<string> EnumerateReleaseFiles(BuildContext context)
    {
        foreach (var file in EnumerateFilesIfExists(context.PackageRoot))
        {
            yield return file;
        }

        foreach (
            var file in new[]
            {
                context.AssetManifestPath,
                Path.Combine(context.PublishRoot, "publish-manifest.json"),
            }
        )
        {
            if (File.Exists(file))
            {
                yield return file;
            }
        }

        var sbomRoot = Path.Combine(context.Options.RepositoryRoot, "artifacts", "sbom");
        foreach (var file in EnumerateFilesIfExists(sbomRoot))
        {
            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateFilesIfExists(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            : [];
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string ToRepositoryRelativePath(BuildContext context, string path)
    {
        return Path.GetRelativePath(context.Options.RepositoryRoot, path).Replace('\\', '/');
    }
}

public sealed class PackageVerifier
{
    private readonly BuildContext context;
    private readonly SigningOptions signingOptions;

    public PackageVerifier(BuildContext context, SigningOptions? signingOptions = null)
    {
        this.context = context;
        this.signingOptions = signingOptions ?? SigningOptions.FromEnvironment();
    }

    public IReadOnlyList<string> VerifyAll()
    {
        var failures = new List<string>();
        VerifyInstallerPackage("Offline", failures);
        VerifyUpdatePackage(failures);

        return failures;
    }

    private void VerifyInstallerPackage(string packageMoniker, List<string> failures)
    {
        var installerName =
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.exe";
        var stagingPackagePath = Path.Combine(
            context.PackageRoot,
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        var installerPath = Path.Combine(context.PackageRoot, installerName);

        VerifyExecutable(installerPath, failures);
        VerifyZip(stagingPackagePath, $"app-package/{AppConstants.ExecutableName}", failures);
        VerifyZip(
            stagingPackagePath,
            "app-package/assets/webui/upstream/dist/index.html",
            failures
        );
        VerifyZip(stagingPackagePath, "app-package/assets/webui/upstream/sync.json", failures);
        VerifyZip(stagingPackagePath, "mica-setup.json", failures);
        VerifyZip(stagingPackagePath, "micasetup.json", failures);
        VerifyZipExecutable(stagingPackagePath, $"output/{installerName}", failures);
        VerifyZip(stagingPackagePath, "app-package/packaging/uninstall-cleanup.json", failures);
        VerifyZip(stagingPackagePath, "app-package/packaging/dependency-precheck.json", failures);
        VerifyZip(stagingPackagePath, "app-package/packaging/update-policy.json", failures);

        if (!signingOptions.SigningRequired)
        {
            return;
        }

        VerifySignatureMetadata(installerPath, "authenticode", expectedSigned: true, failures);
        VerifySignatureMetadata(
            stagingPackagePath,
            "github-artifact-attestation",
            expectedSigned: false,
            failures
        );
        VerifyZipSignatureMetadata(
            stagingPackagePath,
            $"app-package/{AppConstants.ExecutableName}.signature.json",
            "authenticode",
            expectedSigned: true,
            failures
        );
        VerifyZipSignatureMetadata(
            stagingPackagePath,
            $"output/{installerName}.signature.json",
            "authenticode",
            expectedSigned: true,
            failures
        );
    }

    private static void VerifyZip(string path, string requiredEntry, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var found = archive.Entries.Any(entry =>
                string.Equals(
                    NormalizeEntryName(entry.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (!found)
            {
                failures.Add($"Package '{path}' is missing entry '{requiredEntry}'.");
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private void VerifyUpdatePackage(List<string> failures)
    {
        var updatePackagePath = Path.Combine(
            context.PackageRoot,
            $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        VerifyZip(updatePackagePath, "update-manifest.json", failures);
        VerifyZip(updatePackagePath, $"payload/{AppConstants.ExecutableName}", failures);
    }

    private static void VerifyZipExecutable(
        string path,
        string requiredEntry,
        List<string> failures
    )
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(
                    NormalizeEntryName(item.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (entry is null)
            {
                failures.Add($"Package '{path}' is missing entry '{requiredEntry}'.");
                return;
            }

            if (entry.Length < 64)
            {
                failures.Add(
                    $"Installer executable is too small to be valid: {path}!{requiredEntry}"
                );
                return;
            }

            using var stream = entry.Open();
            var validationFailure = WindowsExecutableValidation.ValidateStream(
                stream,
                $"{path}!{requiredEntry}"
            );
            if (validationFailure is not null)
            {
                failures.Add(validationFailure);
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private static void VerifyExecutable(string path, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Installer executable missing: {path}");
            return;
        }

        var validationFailure = WindowsExecutableValidation.ValidateFile(path);
        if (validationFailure is not null)
        {
            failures.Add(validationFailure);
        }
    }

    private static void VerifySignatureMetadata(
        string artifactPath,
        string expectedSignatureKind,
        bool expectedSigned,
        List<string> failures
    )
    {
        var metadataPath = ArtifactSignatureMetadata.GetSignaturePath(artifactPath);
        if (!File.Exists(metadataPath))
        {
            failures.Add($"Signature metadata missing: {metadataPath}");
            return;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ArtifactSignatureMetadata>(
                File.ReadAllText(metadataPath, Encoding.UTF8),
                JsonDefaults.Options
            );
            VerifySignatureMetadata(
                metadata,
                metadataPath,
                expectedSignatureKind,
                expectedSigned,
                failures
            );
        }
        catch (JsonException exception)
        {
            failures.Add(
                $"Signature metadata is not valid JSON: {metadataPath}: {exception.Message}"
            );
        }
    }

    private static void VerifyZipSignatureMetadata(
        string packagePath,
        string requiredEntry,
        string expectedSignatureKind,
        bool expectedSigned,
        List<string> failures
    )
    {
        if (!File.Exists(packagePath))
        {
            failures.Add($"Package missing: {packagePath}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(
                    NormalizeEntryName(item.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (entry is null)
            {
                failures.Add($"Package '{packagePath}' is missing entry '{requiredEntry}'.");
                return;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var metadata = JsonSerializer.Deserialize<ArtifactSignatureMetadata>(
                reader.ReadToEnd(),
                JsonDefaults.Options
            );
            VerifySignatureMetadata(
                metadata,
                $"{packagePath}!{requiredEntry}",
                expectedSignatureKind,
                expectedSigned,
                failures
            );
        }
        catch (InvalidDataException exception)
        {
            failures.Add(
                $"Package '{packagePath}' is not a readable zip archive: {exception.Message}"
            );
        }
        catch (JsonException exception)
        {
            failures.Add(
                $"Signature metadata in package is not valid JSON: {packagePath}!{requiredEntry}: {exception.Message}"
            );
        }
    }

    private static void VerifySignatureMetadata(
        ArtifactSignatureMetadata? metadata,
        string displayPath,
        string expectedSignatureKind,
        bool expectedSigned,
        List<string> failures
    )
    {
        if (metadata is null)
        {
            failures.Add($"Signature metadata is empty: {displayPath}");
            return;
        }

        if (!string.Equals(metadata.SignatureKind, expectedSignatureKind, StringComparison.Ordinal))
        {
            failures.Add(
                $"Signature metadata kind mismatch: {displayPath}; expected {expectedSignatureKind}, got {metadata.SignatureKind}."
            );
        }

        if (!metadata.AttestationExpected)
        {
            failures.Add(
                $"Signature metadata does not require artifact attestation: {displayPath}"
            );
        }

        if (
            expectedSigned
            && (!metadata.HasSignature || !metadata.Verified || metadata.Signing != "signed")
        )
        {
            failures.Add($"Artifact is not recorded as signed and verified: {displayPath}");
        }

        if (!expectedSigned && metadata.Signing != "not-applicable")
        {
            failures.Add(
                $"Artifact attestation metadata has unexpected signing state: {displayPath}"
            );
        }
    }

    private static string NormalizeEntryName(string name)
    {
        return name.Replace('\\', '/');
    }
}

public static class InstallerMetadata
{
    private static readonly string[] RequiredWebUiFiles =
    [
        "assets/webui/upstream/dist/index.html",
        "assets/webui/upstream/sync.json",
    ];

    public static async Task WriteAsync(
        BuildContext context,
        string appPackageRoot,
        string installerStageRoot
    )
    {
        var packagingRoot = Path.Combine(appPackageRoot, "packaging");
        Directory.CreateDirectory(packagingRoot);

        await WriteJsonAsync(
            Path.Combine(packagingRoot, "dependency-precheck.json"),
            new
            {
                webView2 = new
                {
                    required = true,
                    runtime = "Microsoft Edge WebView2 Runtime",
                    detection = "CoreWebView2Environment.GetAvailableBrowserVersionString",
                    bundledFirst = false,
                    downloadPage = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                    note = "The desktop shell is a WebView2 host and cannot render the vendored official WebUI without the runtime.",
                },
                runtime = new
                {
                    selfContained = true,
                    targetFramework = "net10.0-windows",
                    bundledFirst = true,
                    requiredExecutable = AppConstants.ExecutableName,
                    onlineFallback = "https://dotnet.microsoft.com/download/dotnet/10.0",
                    note = "The installed app is self-contained; online runtime repair is reserved for future framework-dependent payloads.",
                },
                backend = new
                {
                    bundledPath = "assets/backend/windows-x64",
                    requiredFiles = AssetCommands.RequiredFiles,
                    bundledFirst = true,
                    onlineFallback = "https://github.com/router-for-me/CLIProxyAPI/releases",
                    verifyWithManifest = "assets/backend/windows-x64 + asset-manifest.json",
                },
                webUi = new
                {
                    bundledPath = "assets/webui/upstream",
                    requiredFiles = RequiredWebUiFiles,
                    bundledFirst = true,
                    verifyWithFiles = "assets/webui/upstream/dist/index.html + assets/webui/upstream/sync.json",
                    note = "The desktop shell serves the vendored official WebUI from local packaged files instead of a remote URL.",
                },
                installer = new
                {
                    precheck = "Setup payload contains dependency-precheck.json so the installer chain can validate WebView2, bundled backend files, and vendored WebUI assets before launch.",
                    launchAfterInstall = true,
                },
            }
        );

        await WriteJsonAsync(
            Path.Combine(packagingRoot, "update-policy.json"),
            new
            {
                stable = new
                {
                    enabled = true,
                    source = "release-manifest.json + CodexCliPlus.Update.<version>.<runtime>.zip",
                    expectedInstallerAsset = $"{AppConstants.InstallerNamePrefix}.Offline.{context.Options.Version}.exe",
                    updateKind = "file-manifest-diff",
                    installedBuildCanLaunchUpdater = true,
                },
                beta = new { reserved = true, enabled = false },
            }
        );

        var cleanup = new InstallerCleanupManifest
        {
            ProductKey = AppConstants.ProductKey,
            KeepUserDataOption = true,
            KeepMyDataOptionName = "KeepMyData",
            KeepMyDataDefault = false,
            DefaultUninstallProfile = "full-clean",
            SafeDeleteRoots =
            [
                "%ProgramFiles%\\CodexCliPlus",
                "%AppData%\\CodexCliPlus",
                "%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs\\CodexCliPlus",
                "%AppData%\\Microsoft\\Windows\\Start Menu\\Programs\\CodexCliPlus",
                "%LocalAppData%\\CodexCliPlus",
            ],
            AlwaysDelete =
            [
                "%ProgramFiles%\\CodexCliPlus",
                "%AppData%\\Microsoft\\Windows\\Start Menu\\Programs\\CodexCliPlus",
                "%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs\\CodexCliPlus",
            ],
            DeleteByDefault =
            [
                "%ProgramFiles%\\CodexCliPlus\\config",
                "%ProgramFiles%\\CodexCliPlus\\config\\secrets\\*.bin",
                "%ProgramFiles%\\CodexCliPlus\\cache",
                "%ProgramFiles%\\CodexCliPlus\\cache\\updates",
                "%ProgramFiles%\\CodexCliPlus\\logs",
                "%ProgramFiles%\\CodexCliPlus\\backend",
                "%ProgramFiles%\\CodexCliPlus\\diagnostics",
                "%ProgramFiles%\\CodexCliPlus\\runtime",
                "%AppData%\\CodexCliPlus",
            ],
            PreserveWhenKeepMyData =
            [
                "%ProgramFiles%\\CodexCliPlus\\config",
                "%ProgramFiles%\\CodexCliPlus\\config\\secrets",
                "%ProgramFiles%\\CodexCliPlus\\logs",
                "%ProgramFiles%\\CodexCliPlus\\diagnostics",
            ],
            RegistryValues =
            [
                "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\CodexCliPlus",
                "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CodexCliPlus",
            ],
            FirewallRules = ["CodexCliPlus"],
            ScheduledTasks = ["CodexCliPlus"],
            SafetyRules =
            [
                "Only delete roots whose resolved final segment is CodexCliPlus.",
                "Never follow a cleanup item outside Program Files, AppData, LocalAppData legacy data, the selected install directory, Start Menu, CodexCliPlus firewall rules, or CodexCliPlus scheduled tasks.",
                "Default uninstall runs the full-clean profile and deletes CodexCliPlus user data.",
                "KeepMyData preserves config, credential references, logs, and diagnostics while removing installed binaries and integration points.",
            ],
        };
        await WriteJsonAsync(Path.Combine(packagingRoot, "uninstall-cleanup.json"), cleanup);
        await WriteJsonAsync(Path.Combine(installerStageRoot, "uninstall-cleanup.json"), cleanup);
    }

    private static Task WriteJsonAsync(string path, object value)
    {
        return File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }
}

public sealed class MicaSetupToolchain
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/lemutec/MicaSetup/releases/latest";

    private MicaSetupToolchain(
        string rootDirectory,
        string sevenZipPath,
        string makeMicaPath,
        string templatePath,
        string version
    )
    {
        RootDirectory = rootDirectory;
        SevenZipPath = sevenZipPath;
        MakeMicaPath = makeMicaPath;
        TemplatePath = templatePath;
        Version = version;
    }

    public string RootDirectory { get; }

    public string SevenZipPath { get; }

    public string MakeMicaPath { get; }

    public string TemplatePath { get; }

    public string Version { get; }

    public static async Task<MicaSetupToolchain> AcquireAsync(BuildContext context)
    {
        var repoOwnedRoot = Path.Combine(
            context.Options.RepositoryRoot,
            "build",
            "micasetup",
            "toolchain"
        );
        var repoOwnedToolchain = await TryCreateFromDirectoryAsync(
            repoOwnedRoot,
            "repo-owned",
            context.Logger
        );
        if (repoOwnedToolchain is not null)
        {
            context.Logger.Info($"MicaSetup tools repo-owned: {repoOwnedToolchain.Version}");
            return repoOwnedToolchain;
        }

        var root = Path.Combine(context.ToolsRoot, "micasetup");
        var cachedToolchain = await TryCreateFromDirectoryAsync(root, "cached", context.Logger);
        if (cachedToolchain is not null)
        {
            context.Logger.Info($"MicaSetup tools cached: {cachedToolchain.Version}");
            return cachedToolchain;
        }

        SafeFileSystem.CleanDirectory(root, context.Options.OutputRoot);
        var sevenZip = Path.Combine(root, "build", "bin", "7z.exe");
        var makeMica = Path.Combine(root, "build", "makemica.exe");
        var template = Path.Combine(root, "build", "template", "default.7z");
        var versionPath = Path.Combine(root, "micasetup-tools-version.txt");

        var release = await QueryLatestReleaseAsync();
        var asset =
            release.Assets.FirstOrDefault(item =>
                item.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                && item.Name.StartsWith("MicaSetup.Tools.", StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidDataException(
                "Latest MicaSetup release does not contain a MicaSetup.Tools nupkg asset."
            );

        var downloadPath = Path.Combine(root, "download", asset.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
        await DownloadWithRetryAsync(asset.DownloadUrl, downloadPath, context.Logger);
        ZipFile.ExtractToDirectory(downloadPath, root, overwriteFiles: true);

        if (!File.Exists(sevenZip) || !File.Exists(makeMica) || !File.Exists(template))
        {
            throw new FileNotFoundException(
                "Downloaded MicaSetup.Tools package is missing makemica.exe, 7z.exe, or template/default.7z."
            );
        }

        await File.WriteAllTextAsync(
            versionPath,
            release.TagName,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        context.Logger.Info($"MicaSetup tools downloaded: {release.TagName}");
        var compatibleMakeMica = MakeMicaVisualStudioCompatibility.TryCreateCompatibleMakeMica(
            makeMica,
            context.Logger
        );
        return new MicaSetupToolchain(
            root,
            sevenZip,
            compatibleMakeMica,
            template,
            release.TagName
        );
    }

    private static async Task<MicaSetupToolchain?> TryCreateFromDirectoryAsync(
        string root,
        string fallbackVersion,
        BuildLogger logger
    )
    {
        var sevenZip = Path.Combine(root, "build", "bin", "7z.exe");
        var makeMica = Path.Combine(root, "build", "makemica.exe");
        var template = Path.Combine(root, "build", "template", "default.7z");
        if (!File.Exists(sevenZip) || !File.Exists(makeMica) || !File.Exists(template))
        {
            return null;
        }

        var versionPath = Path.Combine(root, "micasetup-tools-version.txt");
        var version = File.Exists(versionPath)
            ? (await File.ReadAllTextAsync(versionPath)).Trim()
            : fallbackVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = fallbackVersion;
        }

        var compatibleMakeMica = MakeMicaVisualStudioCompatibility.TryCreateCompatibleMakeMica(
            makeMica,
            logger
        );
        return new MicaSetupToolchain(root, sevenZip, compatibleMakeMica, template, version);
    }

    private static async Task<MicaSetupRelease> QueryLatestReleaseAsync()
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CodexCliPlus-BuildTool", "1.0"));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"MicaSetup latest release query failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}"
            );
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var assets = new List<MicaSetupReleaseAsset>();
        if (
            root.TryGetProperty("assets", out var assetsElement)
            && assetsElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = GetString(asset, "name");
                var url = GetString(asset, "browser_download_url");
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                {
                    assets.Add(new MicaSetupReleaseAsset(name, url));
                }
            }
        }

        return new MicaSetupRelease(
            GetString(root, "tag_name") ?? GetString(root, "name") ?? "latest",
            assets
        );
    }

    private static async Task DownloadWithRetryAsync(
        string url,
        string targetPath,
        BuildLogger logger
    )
    {
        using var client = new HttpClient();
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                logger.Info($"MicaSetup tools download attempt {attempt}/3");
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(targetPath, bytes);
                return;
            }
            catch (Exception exception)
            {
                lastError = exception;
                logger.Error(
                    $"MicaSetup tools download attempt {attempt}/3 failed: {exception.Message}"
                );
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                }
            }
        }

        throw new InvalidOperationException(
            $"Failed to download MicaSetup tools: {lastError?.Message}",
            lastError
        );
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record MicaSetupRelease(
        string TagName,
        IReadOnlyList<MicaSetupReleaseAsset> Assets
    );

    private sealed record MicaSetupReleaseAsset(string Name, string DownloadUrl);
}

public static class MakeMicaVisualStudioCompatibility
{
    private const string OriginalVsWhereArguments = "-latest -property installationPath";
    private const string Vs2026AwareVsWhereArguments =
        "-latest -products * -property installationPath";
    private const string CompatibleExecutableSuffix = ".vs2026.exe";

    public static string TryCreateCompatibleMakeMica(string makeMicaPath, BuildLogger logger)
    {
        var directory = Path.GetDirectoryName(makeMicaPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return makeMicaPath;
        }

        var compatiblePath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(makeMicaPath)}{CompatibleExecutableSuffix}"
        );

        if (IsCompatibleCopyCurrent(makeMicaPath, compatiblePath))
        {
            return compatiblePath;
        }

        var temporaryPath = $"{compatiblePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(
                makeMicaPath,
                new ReaderParameters { InMemory = true, ReadingMode = ReadingMode.Immediate }
            );
            var replacementCount = ReplaceVsWhereArguments(assembly.MainModule);
            if (replacementCount == 0)
            {
                TryDeleteFile(temporaryPath);
                return makeMicaPath;
            }

            assembly.Write(temporaryPath);
            File.Move(temporaryPath, compatiblePath, overwrite: true);
            File.SetLastWriteTimeUtc(
                compatiblePath,
                File.GetLastWriteTimeUtc(makeMicaPath).AddSeconds(1)
            );
            logger.Info(
                $"MicaSetup makemica.exe compatibility copy created for VS 2026 Build Tools: {compatiblePath}"
            );
            return compatiblePath;
        }
        catch (BadImageFormatException)
        {
            TryDeleteFile(temporaryPath);
            return makeMicaPath;
        }
        catch (Exception exception)
        {
            TryDeleteFile(temporaryPath);
            logger.Warning(
                $"Could not create VS 2026-compatible makemica.exe copy; using original makemica.exe: {exception.Message}"
            );
            return makeMicaPath;
        }
    }

    private static bool IsCompatibleCopyCurrent(string makeMicaPath, string compatiblePath)
    {
        return File.Exists(compatiblePath)
            && File.GetLastWriteTimeUtc(compatiblePath) > File.GetLastWriteTimeUtc(makeMicaPath);
    }

    private static int ReplaceVsWhereArguments(ModuleDefinition module)
    {
        var replacementCount = 0;
        foreach (var type in EnumerateTypes(module.Types))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (
                        instruction.OpCode == OpCodes.Ldstr
                        && instruction.Operand is string value
                        && string.Equals(value, OriginalVsWhereArguments, StringComparison.Ordinal)
                    )
                    {
                        instruction.Operand = Vs2026AwareVsWhereArguments;
                        replacementCount++;
                    }
                }
            }
        }

        return replacementCount;
    }

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nestedType in EnumerateTypes(type.NestedTypes))
            {
                yield return nestedType;
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }
}

public sealed class MicaSetupInstallerBuilder(MicaSetupToolchain toolchain)
{
    private const string CleanupMethodMarker = "private static void CleanupCodexCliPlusUserData()";

    private const string UninstallCleanupSource = """
                private static void CleanupCodexCliPlusUserData()
                {
                    RegistyAutoRunHelper.Disable("CodexCliPlus");

                    if (Option.Current.KeepMyData)
                    {
                        return;
                    }

                    foreach (string path in EnumerateCodexCliPlusCleanupRoots())
                    {
                        TryDeleteSafePath(path);
                    }

                    TryDeleteFirewallRule("CodexCliPlus");
                    TryDeleteScheduledTask("CodexCliPlus");
                }

                private static IEnumerable<string> EnumerateCodexCliPlusCleanupRoots()
                {
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "CodexCliPlus");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "CodexCliPlus");
                }

                private static void TryDeleteSafePath(string path)
                {
                    if (string.IsNullOrWhiteSpace(path) || !IsSafeCodexCliPlusPath(path))
                    {
                        return;
                    }

                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        else if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }

                private static bool IsSafeCodexCliPlusPath(string path)
                {
                    string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string name = Path.GetFileName(fullPath);
                    if (!string.Equals(name, "CodexCliPlus", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
                }

                private static bool IsEqualOrUnder(string fullPath, string rootPath)
                {
                    if (string.IsNullOrWhiteSpace(rootPath))
                    {
                        return false;
                    }

                    string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                        || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                }

                private static void TryDeleteFirewallRule(string ruleName)
                {
                    TryStartAndWait("netsh.exe", $"advfirewall firewall delete rule name=\"{ruleName}\"");
                }

                private static void TryDeleteScheduledTask(string taskName)
                {
                    TryStartAndWait("schtasks.exe", $"/Delete /TN \"{taskName}\" /F");
                }

                private static void TryStartAndWait(string fileName, string arguments)
                {
                    try
                    {
                        using Process? process = Process.Start(new ProcessStartInfo
                        {
                            FileName = fileName,
                            Arguments = arguments,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        process?.WaitForExit(5000);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
        """;

    public async Task<int> BuildAsync(
        BuildContext context,
        string micaConfigPath,
        string payloadArchivePath,
        string installerOutputPath
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(installerOutputPath)!);
        if (File.Exists(installerOutputPath))
        {
            File.Delete(installerOutputPath);
        }

        if (!HasVisualStudioInstaller())
        {
            context.Logger.Info(
                "Visual Studio Installer not detected; using dotnet msbuild against the official MicaSetup template."
            );
            return await BuildWithDotnetMsbuildAsync(
                context,
                micaConfigPath,
                payloadArchivePath,
                installerOutputPath
            );
        }

        var makeMicaExitCode = await context.ProcessRunner.RunAsync(
            toolchain.MakeMicaPath,
            [micaConfigPath],
            Path.GetDirectoryName(micaConfigPath)!,
            context.Logger
        );
        if (makeMicaExitCode == 0 && File.Exists(installerOutputPath))
        {
            var validationFailure = WindowsExecutableValidation.ValidateFile(installerOutputPath);
            if (validationFailure is null)
            {
                context.Logger.Info("MicaSetup installer generated by makemica.exe");
                return 0;
            }

            context.Logger.Error(
                $"makemica.exe produced an invalid installer: {validationFailure}"
            );
            File.Delete(installerOutputPath);
        }

        context.Logger.Info(
            makeMicaExitCode == 0
                ? "makemica.exe completed without a valid installer executable; falling back to dotnet msbuild against the official MicaSetup template."
                : $"makemica.exe failed with exit code {makeMicaExitCode}; falling back to dotnet msbuild against the official MicaSetup template."
        );
        return await BuildWithDotnetMsbuildAsync(
            context,
            micaConfigPath,
            payloadArchivePath,
            installerOutputPath
        );
    }

    private static bool HasVisualStudioInstaller()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var programFilesRoot in GetProgramFilesRoots())
        {
            var installerRoot = Path.Combine(
                programFilesRoot,
                "Microsoft Visual Studio",
                "Installer"
            );
            if (
                File.Exists(Path.Combine(installerRoot, "setup.exe"))
                || File.Exists(Path.Combine(installerRoot, "vswhere.exe"))
            )
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetProgramFilesRoots()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return programFilesX86;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (
            !string.IsNullOrWhiteSpace(programFiles)
            && !string.Equals(programFiles, programFilesX86, StringComparison.OrdinalIgnoreCase)
        )
        {
            yield return programFiles;
        }
    }

    private async Task<int> BuildWithDotnetMsbuildAsync(
        BuildContext context,
        string micaConfigPath,
        string payloadArchivePath,
        string installerOutputPath
    )
    {
        var stageRoot = Path.GetDirectoryName(micaConfigPath)!;
        var distRoot = Path.Combine(stageRoot, ".dist");
        SafeFileSystem.CleanDirectory(distRoot, context.Options.OutputRoot);

        var extractExitCode = await context.ProcessRunner.RunAsync(
            toolchain.SevenZipPath,
            ["x", toolchain.TemplatePath, $"-o{distRoot}", "-y"],
            stageRoot,
            context.Logger
        );
        if (extractExitCode != 0)
        {
            context.Logger.Error(
                $"MicaSetup template extraction failed with exit code {extractExitCode}."
            );
            return extractExitCode;
        }

        ApplyTemplateConfig(context, distRoot, payloadArchivePath);

        var uninstExitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            [
                "msbuild",
                Path.Combine(distRoot, "MicaSetup.Uninst.csproj"),
                "/t:Rebuild",
                "/p:Configuration=Release",
                "/p:DeployOnBuild=true",
                "/p:PublishProfile=FolderProfile",
                "/p:ImportDirectoryBuildProps=false",
                "/p:RestoreUseStaticGraphEvaluation=false",
                "/restore",
            ],
            distRoot,
            context.Logger
        );
        if (uninstExitCode != 0)
        {
            context.Logger.Error(
                $"MicaSetup uninstaller build failed with exit code {uninstExitCode}."
            );
            return uninstExitCode;
        }

        var builtUninstaller = Path.Combine(distRoot, "bin", "Release", "MicaSetup.exe");
        var uninstallerResource = Path.Combine(distRoot, "Resources", "Setups", "Uninst.exe");
        if (!File.Exists(builtUninstaller))
        {
            context.Logger.Error($"MicaSetup uninstaller output missing: {builtUninstaller}");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(uninstallerResource)!);
        File.Copy(builtUninstaller, uninstallerResource, overwrite: true);

        var setupExitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            [
                "msbuild",
                Path.Combine(distRoot, "MicaSetup.csproj"),
                "/t:Rebuild",
                "/p:Configuration=Release",
                "/p:DeployOnBuild=true",
                "/p:PublishProfile=FolderProfile",
                "/p:ImportDirectoryBuildProps=false",
                "/p:RestoreUseStaticGraphEvaluation=false",
                "/restore",
            ],
            distRoot,
            context.Logger
        );
        if (setupExitCode != 0)
        {
            context.Logger.Error($"MicaSetup setup build failed with exit code {setupExitCode}.");
            return setupExitCode;
        }

        var builtInstaller = Path.Combine(distRoot, "bin", "Release", "MicaSetup.exe");
        if (!File.Exists(builtInstaller))
        {
            context.Logger.Error($"MicaSetup installer output missing: {builtInstaller}");
            return 1;
        }

        File.Copy(builtInstaller, installerOutputPath, overwrite: true);
        var validationFailure = WindowsExecutableValidation.ValidateFile(installerOutputPath);
        if (validationFailure is not null)
        {
            context.Logger.Error(validationFailure);
            return 1;
        }

        context.Logger.Info("MicaSetup installer generated by dotnet msbuild fallback");
        return 0;
    }

    private static void ApplyTemplateConfig(
        BuildContext context,
        string distRoot,
        string payloadArchivePath
    )
    {
        var setupResourcePath = Path.Combine(distRoot, "Resources", "Setups", "publish.7z");
        Directory.CreateDirectory(Path.GetDirectoryName(setupResourcePath)!);
        File.Copy(payloadArchivePath, setupResourcePath, overwrite: true);

        var cleanupManifestSource = Path.Combine(
            Path.GetDirectoryName(payloadArchivePath)!,
            "uninstall-cleanup.json"
        );
        if (File.Exists(cleanupManifestSource))
        {
            File.Copy(
                cleanupManifestSource,
                Path.Combine(distRoot, "Resources", "Setups", "uninstall-cleanup.json"),
                overwrite: true
            );
        }

        CopyLicenseDocuments(context, Path.Combine(distRoot, "Resources", "Licenses"));

        var setupProgram = Path.Combine(distRoot, "Program.cs");
        var uninstProgram = Path.Combine(distRoot, "Program.un.cs");
        PatchProgramSource(setupProgram, context.Options.Version, isUninstaller: false);
        PatchProgramSource(uninstProgram, context.Options.Version, isUninstaller: true);
        PatchForCurrentUserInstall(distRoot);
        PatchUninstallCleanup(distRoot);
    }

    private static void CopyLicenseDocuments(BuildContext context, string targetDirectory)
    {
        var repositoryRoot = context.Options.RepositoryRoot;
        var documents = new (string Source, string Target)[]
        {
            (Path.Combine(repositoryRoot, "LICENSE.txt"), "CodexCliPlus.LICENSE.txt"),
            (
                Path.Combine(context.AssetsRoot, "backend", "windows-x64", "LICENSE"),
                "CLIProxyAPI.LICENSE.txt"
            ),
            (
                Path.Combine(repositoryRoot, "resources", "licenses", "BetterGI.GPL-3.0.txt"),
                "BetterGI.GPL-3.0.txt"
            ),
            (Path.Combine(repositoryRoot, "resources", "licenses", "NOTICE.txt"), "NOTICE.txt"),
        };

        foreach (var (source, target) in documents)
        {
            if (!File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(targetDirectory);
            File.Copy(source, Path.Combine(targetDirectory, target), overwrite: true);
        }
    }

    private static void PatchProgramSource(string path, string version, bool isUninstaller)
    {
        var source = File.ReadAllText(path);
        if (!source.Contains(".UseElevated()", StringComparison.Ordinal))
        {
            source = source.Replace(
                "Hosting.CreateBuilder()",
                "Hosting.CreateBuilder().UseElevated()",
                StringComparison.Ordinal
            );
        }

        source = ReplaceBetween(
            source,
            ".UseSingleInstance(\"",
            "\")",
            isUninstaller
                ? "BlackblockInc.CodexCliPlus.Uninstall"
                : "BlackblockInc.CodexCliPlus.Setup"
        );
        source = ReplaceBetween(
            source,
            "[assembly: Guid(\"",
            "\")]",
            "6f8dd8b7-21ea-4c6b-9695-40a27874ce4d"
        );
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyTitle(\"",
            "\")]",
            isUninstaller ? "CodexCliPlus Uninstall" : "CodexCliPlus Setup"
        );
        source = ReplaceBetween(source, "[assembly: AssemblyProduct(\"", "\")]", "CodexCliPlus");
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyDescription(\"",
            "\")]",
            isUninstaller ? "CodexCliPlus Uninstall" : "CodexCliPlus Setup"
        );
        source = ReplaceBetween(source, "[assembly: AssemblyCompany(\"", "\")]", "Blackblock Inc.");
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyVersion(\"",
            "\")]",
            NormalizeAssemblyVersion(version)
        );
        source = ReplaceBetween(
            source,
            "[assembly: AssemblyFileVersion(\"",
            "\")]",
            NormalizeAssemblyVersion(version)
        );
        source = ReplaceBetween(source, "[assembly: RequestExecutionLevel(\"", "\")]", "admin");

        source = ReplaceAssignment(source, "option.IsCreateDesktopShortcut", "true");
        source = ReplaceAssignment(source, "option.IsCreateUninst", "true");
        source = ReplaceAssignment(source, "option.IsUninstLower", "false");
        source = ReplaceAssignment(source, "option.IsCreateStartMenu", "true");
        source = ReplaceAssignment(source, "option.IsPinToStartMenu", "false");
        source = ReplaceAssignment(source, "option.IsCreateQuickLaunch", "false");
        source = ReplaceAssignment(source, "option.IsCreateRegistryKeys", "true");
        source = ReplaceAssignment(source, "option.IsCreateAsAutoRun", "false");
        source = ReplaceAssignment(source, "option.IsCustomizeVisiableAutoRun", "true");
        source = ReplaceAssignment(source, "option.AutoRunLaunchCommand", "\"/autostart\"");
        source = ReplaceAssignment(source, "option.IsUseInstallPathPreferX86", "false");
        source = ReplaceAssignment(
            source,
            "option.IsUseInstallPathPreferAppDataLocalPrograms",
            "false"
        );
        source = ReplaceAssignment(source, "option.IsUseInstallPathPreferAppDataRoaming", "false");
        source = ReplaceAssignment(source, "option.IsAllowFullFolderSecurity", "true");
        source = ReplaceAssignment(source, "option.IsAllowFirewall", "false");
        source = ReplaceAssignment(source, "option.IsRefreshExplorer", "true");
        source = ReplaceAssignment(source, "option.IsInstallCertificate", "false");
        source = ReplaceAssignment(source, "option.IsEnableUninstallDelayUntilReboot", "true");
        source = ReplaceAssignment(source, "option.IsEnvironmentVariable", "false");
        source = ReplaceAssignment(source, "option.AppName", "\"CodexCliPlus\"");
        source = ReplaceAssignment(source, "option.KeyName", "\"CodexCliPlus\"");
        source = ReplaceAssignment(source, "option.ExeName", "\"CodexCliPlus.exe\"");
        source = ReplaceAssignment(source, "option.DisplayVersion", $"\"{version}\"");
        source = ReplaceAssignment(source, "option.Publisher", "\"Blackblock Inc.\"");
        source = ReplaceAssignment(source, "option.MessageOfPage1", "\"CodexCliPlus\"");
        source = ReplaceAssignment(
            source,
            "option.MessageOfPage2",
            isUninstaller ? "\"正在卸载 CodexCliPlus\"" : "\"正在安装 CodexCliPlus\""
        );
        source = ReplaceAssignment(
            source,
            "option.MessageOfPage3",
            isUninstaller ? "\"卸载完成\"" : "\"安装完成\""
        );
        if (isUninstaller)
        {
            source = EnsureOptionAssignment(source, "option.KeepMyData", "false", "option.ExeName");
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PatchForCurrentUserInstall(string distRoot)
    {
        var uninstMainViewModel = Path.Combine(
            distRoot,
            "ViewModels",
            "Uninst",
            "MainViewModel.cs"
        );
        if (File.Exists(uninstMainViewModel))
        {
            var source = File.ReadAllText(uninstMainViewModel)
                .Replace(
                    "private bool isElevated = RuntimeHelper.IsElevated;",
                    "private bool isElevated = true;",
                    StringComparison.Ordinal
                );
            File.WriteAllText(
                uninstMainViewModel,
                source,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
        }
    }

    private static void PatchUninstallCleanup(string distRoot)
    {
        var path = Path.Combine(distRoot, "Helper", "Setup", "UninstallHelper.cs");
        if (!File.Exists(path))
        {
            return;
        }

        var source = File.ReadAllText(path);
        source = source.Replace(
            "using System.IO;",
            "using System.Diagnostics;\r\nusing System.IO;",
            StringComparison.Ordinal
        );
        source = source.Replace(
            "else { // For security reason, uninst should always keep data because of unundering admin. Option.Current.KeepMyData = true; uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(); if (string.IsNullOrWhiteSpace(uinfo.UninstallData)) { MessageBox.Info(ApplicationDispatcherHelper.MainWindow, \"InstallationInfoLostHint\".Tr()); } }",
            "else { uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(); if (string.IsNullOrWhiteSpace(uinfo.UninstallData)) { MessageBox.Info(ApplicationDispatcherHelper.MainWindow, \"InstallationInfoLostHint\".Tr()); } }",
            StringComparison.Ordinal
        );
        source = source.Replace(
            """
                    else
                    {
                        // For security reason, uninst should always keep data because of unundering admin.
                        Option.Current.KeepMyData = true;

                        uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath();

                        if (string.IsNullOrWhiteSpace(uinfo.UninstallData))
                        {
                            MessageBox.Info(ApplicationDispatcherHelper.MainWindow, "InstallationInfoLostHint".Tr());
                        }
                    }
            """,
            """
                    else
                    {
                        uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath();

                        if (string.IsNullOrWhiteSpace(uinfo.UninstallData))
                        {
                            MessageBox.Info(ApplicationDispatcherHelper.MainWindow, "InstallationInfoLostHint".Tr());
                        }
                    }
            """,
            StringComparison.Ordinal
        );
        source = source.Replace(
            "try { RegistyUninstallHelper.Delete(Option.Current.KeyName); }",
            "CleanupCodexCliPlusUserData(); try { RegistyUninstallHelper.Delete(Option.Current.KeyName); }",
            StringComparison.Ordinal
        );
        source = source.Replace(
            """
                    try
                    {
                        RegistyUninstallHelper.Delete(Option.Current.KeyName);
                    }
            """,
            """
                    CleanupCodexCliPlusUserData();

                    try
                    {
                        RegistyUninstallHelper.Delete(Option.Current.KeyName);
                    }
            """,
            StringComparison.Ordinal
        );

        if (!source.Contains(CleanupMethodMarker, StringComparison.Ordinal))
        {
            source = source.Replace(
                "public static void DeleteUninst()",
                UninstallCleanupSource
                    + "\r\n\r\n                public static void DeleteUninst()",
                StringComparison.Ordinal
            );
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string EnsureOptionAssignment(
        string source,
        string assignmentTarget,
        string value,
        string insertAfterTarget
    )
    {
        if (source.Contains(assignmentTarget, StringComparison.Ordinal))
        {
            return ReplaceAssignment(source, assignmentTarget, value);
        }

        var insertAfter = source.IndexOf(insertAfterTarget, StringComparison.Ordinal);
        if (insertAfter < 0)
        {
            return source;
        }

        var semicolon = source.IndexOf(';', insertAfter);
        return semicolon < 0
            ? source
            : source.Insert(semicolon + 1, $" {assignmentTarget} = {value};");
    }

    private static string ReplaceBetween(string source, string prefix, string suffix, string value)
    {
        var start = source.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return source;
        }

        start += prefix.Length;
        var end = source.IndexOf(suffix, start, StringComparison.Ordinal);
        return end < 0 ? source : source[..start] + value + source[end..];
    }

    private static string ReplaceAssignment(string source, string assignmentTarget, string value)
    {
        var start = source.IndexOf(assignmentTarget, StringComparison.Ordinal);
        if (start < 0)
        {
            return source;
        }

        var equals = source.IndexOf('=', start);
        var semicolon = source.IndexOf(';', equals);
        if (equals < 0 || semicolon < 0)
        {
            return source;
        }

        return source[..(equals + 1)] + " " + value + source[semicolon..];
    }

    private static string NormalizeAssemblyVersion(string version)
    {
        var parts = version
            .Split(['.', '-', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .Take(4)
            .ToList();
        while (parts.Count < 4)
        {
            parts.Add(0);
        }

        return string.Join('.', parts);
    }
}

public static class WindowsExecutableValidation
{
    public static string? ValidateFile(string path)
    {
        if (!File.Exists(path))
        {
            return $"Installer executable missing: {path}";
        }

        var info = new FileInfo(path);
        if (info.Length < 64)
        {
            return $"Installer executable is too small to be valid: {path}";
        }

        using var stream = File.OpenRead(path);
        return ValidateStream(stream, path);
    }

    public static string? ValidateStream(Stream stream, string displayPath)
    {
        Span<byte> header = stackalloc byte[2];
        if (stream.Read(header) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
        {
            return $"Installer executable does not have a Windows PE header: {displayPath}";
        }

        return null;
    }
}

public sealed class BuildAssetManifest
{
    public string Product { get; init; } = AppConstants.ProductName;

    public string Version { get; init; } = string.Empty;

    public string Runtime { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<BuildAssetFile> Files { get; init; } = [];

    public static async Task<BuildAssetManifest> CreateAsync(
        string version,
        string runtime,
        string sourceDirectory,
        string assetsRoot,
        CancellationToken cancellationToken
    )
    {
        var files = new List<BuildAssetFile>();
        foreach (
            var path in Directory
                .EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                string.Equals(
                    Path.GetFileName(path),
                    "asset-manifest.json",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            files.Add(
                new BuildAssetFile
                {
                    Path = ToManifestPath(Path.GetRelativePath(assetsRoot, path)),
                    Size = new FileInfo(path).Length,
                    Sha256 = await ComputeSha256Async(path, cancellationToken),
                }
            );
        }

        return new BuildAssetManifest
        {
            Version = version,
            Runtime = runtime,
            Source = sourceDirectory,
            Files = files,
        };
    }

    public static async Task<BuildAssetManifest> ReadAsync(string manifestPath)
    {
        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<BuildAssetManifest>(
                stream,
                JsonDefaults.Options
            ) ?? throw new InvalidDataException($"Could not read asset manifest: {manifestPath}");
    }

    public async Task WriteAsync(string manifestPath)
    {
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(this, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }

    public IReadOnlyList<string> Verify(string assetsRoot)
    {
        var failures = new List<string>();
        foreach (var file in Files)
        {
            var fullPath = Path.Combine(
                assetsRoot,
                file.Path.Replace('/', Path.DirectorySeparatorChar)
            );
            if (!File.Exists(fullPath))
            {
                failures.Add($"Asset missing: {file.Path}");
                continue;
            }

            var info = new FileInfo(fullPath);
            if (info.Length != file.Size)
            {
                failures.Add($"Asset size mismatch: {file.Path}");
                continue;
            }

            var actualHash = ComputeSha256Async(fullPath, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Asset hash mismatch: {file.Path}");
            }
        }

        foreach (var requiredFile in AssetCommands.RequiredFiles)
        {
            var manifestPath = $"backend/windows-x64/{requiredFile}";
            if (
                !Files.Any(file =>
                    string.Equals(file.Path, manifestPath, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                failures.Add($"Required asset missing from manifest: {manifestPath}");
            }
        }

        return failures;
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

    private static string ToManifestPath(string path)
    {
        return path.Replace('\\', '/');
    }
}

public sealed class BuildAssetFile
{
    public string Path { get; init; } = string.Empty;

    public long Size { get; init; }

    public string Sha256 { get; init; } = string.Empty;
}

public sealed class PublishManifest
{
    public string Product { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Runtime { get; init; } = string.Empty;

    public string Configuration { get; init; } = string.Empty;

    public string Application { get; init; } = string.Empty;

    public string AssetsManifest { get; init; } = string.Empty;
}

public sealed class PackageManifest
{
    public string Product { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Runtime { get; init; } = string.Empty;

    public string PackageType { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class InstallerCleanupManifest
{
    public string ProductKey { get; init; } = string.Empty;

    public bool KeepUserDataOption { get; init; }

    public string KeepMyDataOptionName { get; init; } = string.Empty;

    public bool KeepMyDataDefault { get; init; }

    public string DefaultUninstallProfile { get; init; } = string.Empty;

    public IReadOnlyList<string> SafeDeleteRoots { get; init; } = [];

    public IReadOnlyList<string> AlwaysDelete { get; init; } = [];

    public IReadOnlyList<string> DeleteByDefault { get; init; } = [];

    public IReadOnlyList<string> PreserveWhenKeepMyData { get; init; } = [];

    public IReadOnlyList<string> RegistryValues { get; init; } = [];

    public IReadOnlyList<string> FirewallRules { get; init; } = [];

    public IReadOnlyList<string> ScheduledTasks { get; init; } = [];

    public IReadOnlyList<string> SafetyRules { get; init; } = [];
}

public sealed class InstallerPlan
{
    public string ProductName { get; init; } = string.Empty;

    public string InstallerName { get; init; } = string.Empty;

    public string AppUserModelId { get; init; } = string.Empty;

    public bool CurrentUserDefault { get; init; }

    public string PayloadDirectory { get; init; } = string.Empty;

    public bool MicaSetupRoute { get; init; }

    public string RequestExecutionLevel { get; init; } = string.Empty;

    public string InstallDirectoryHint { get; init; } = string.Empty;

    public bool LaunchAfterInstall { get; init; }

    public string StableReleaseSource { get; init; } = string.Empty;

    public bool BetaChannelReserved { get; init; }
}

public sealed class MicaSetupConfig
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Template { get; init; } = string.Empty;

    public string Package { get; init; } = string.Empty;

    public string Output { get; init; } = string.Empty;

    public string AppName { get; init; } = string.Empty;

    public string KeyName { get; init; } = string.Empty;

    public string ExeName { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string TargetFramework { get; init; } = "net472";

    public string ProductGuid { get; init; } = string.Empty;

    public string? Favicon { get; init; }

    public string? Icon { get; init; }

    public string? UnIcon { get; init; }

    public string? LicenseFile { get; init; }

    public string? License { get; init; }

    public string? LicenseType { get; init; }

    public string RequestExecutionLevel { get; init; } = "admin";

    public string? SingleInstanceMutex { get; init; }

    public bool IsCreateDesktopShortcut { get; init; } = true;

    public bool IsCreateUninst { get; init; } = true;

    public bool IsUninstLower { get; init; }

    public bool IsCreateStartMenu { get; init; } = true;

    public bool IsPinToStartMenu { get; init; }

    public bool IsCreateQuickLaunch { get; init; }

    public bool IsCreateRegistryKeys { get; init; } = true;

    public bool IsCreateAsAutoRun { get; init; }

    public bool IsCustomizeVisiableAutoRun { get; init; } = true;

    public string AutoRunLaunchCommand { get; init; } = "/autostart";

    public bool IsUseFolderPickerPreferClassic { get; init; }

    public bool IsUseInstallPathPreferX86 { get; init; }

    public bool IsUseInstallPathPreferAppDataLocalPrograms { get; init; }

    public bool IsUseInstallPathPreferAppDataRoaming { get; init; }

    public bool? IsUseRegistryPreferX86 { get; init; }

    public bool IsAllowFullFolderSecurity { get; init; }

    public bool IsAllowFirewall { get; init; }

    public bool IsRefreshExplorer { get; init; } = true;

    public bool IsInstallCertificate { get; init; }

    public bool IsEnableUninstallDelayUntilReboot { get; init; } = true;

    public bool IsEnvironmentVariable { get; init; }

    public bool IsUseTempPathFork { get; init; } = true;

    public string OverlayInstallRemoveExt { get; init; } = "exe,dll,pdb,json,config";

    public string? UnpackingPassword { get; init; }

    public string? MessageOfPage1 { get; init; }

    public string? MessageOfPage2 { get; init; }

    public string? MessageOfPage3 { get; init; }

    public static MicaSetupConfig Create(
        BuildContext context,
        string payloadArchivePath,
        string installerOutputPath
    )
    {
        var iconPath = Path.Combine(
            context.Options.RepositoryRoot,
            "resources",
            "icons",
            "codexcliplus.ico"
        );
        var noticePath = Path.Combine(
            context.Options.RepositoryRoot,
            "resources",
            "licenses",
            "NOTICE.txt"
        );
        var licensePath = File.Exists(noticePath)
            ? noticePath
            : Path.Combine(context.Options.RepositoryRoot, "LICENSE.txt");
        return new MicaSetupConfig
        {
            Template = "${MicaDir}/template/default.7z",
            Package = payloadArchivePath,
            Output = installerOutputPath,
            AppName = AppConstants.ProductName,
            KeyName = AppConstants.ProductKey,
            ExeName = AppConstants.ExecutableName,
            Publisher = "Blackblock Inc.",
            Version = context.Options.Version,
            TargetFramework = "net472",
            ProductGuid = "6f8dd8b7-21ea-4c6b-9695-40a27874ce4d",
            Favicon = File.Exists(iconPath) ? iconPath : null,
            Icon = File.Exists(iconPath) ? iconPath : null,
            UnIcon = File.Exists(iconPath) ? iconPath : null,
            LicenseFile = File.Exists(licensePath) ? licensePath : null,
            RequestExecutionLevel = "admin",
            SingleInstanceMutex = "BlackblockInc.CodexCliPlus.Setup",
            MessageOfPage1 = AppConstants.ProductName,
            MessageOfPage2 = "正在安装 CodexCliPlus",
            MessageOfPage3 = "安装完成",
        };
    }
}

public static class SafeFileSystem
{
    public static void CleanDirectory(string targetDirectory, string allowedRoot)
    {
        var fullTarget = Path.GetFullPath(targetDirectory);
        var fullAllowedRoot = Path.GetFullPath(allowedRoot);
        if (!fullTarget.StartsWith(fullAllowedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to clean outside BuildTool output root: {fullTarget}"
            );
        }

        if (Directory.Exists(fullTarget))
        {
            ClearReadOnlyAttributes(fullTarget);
            Directory.Delete(fullTarget, recursive: true);
        }

        Directory.CreateDirectory(fullTarget);
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        foreach (
            var path in Directory.EnumerateFileSystemEntries(
                directory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
    }

    public static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        IReadOnlyCollection<string>? excludedRootDirectoryNames = null
    )
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        }

        var excludedRoots = excludedRootDirectoryNames is null
            ? null
            : new HashSet<string>(excludedRootDirectoryNames, StringComparer.OrdinalIgnoreCase);
        CopyDirectoryCore(sourceDirectory, targetDirectory, sourceDirectory, excludedRoots);
    }

    private static void CopyDirectoryCore(
        string currentSourceDirectory,
        string currentTargetDirectory,
        string sourceRootDirectory,
        HashSet<string>? excludedRootDirectoryNames
    )
    {
        Directory.CreateDirectory(currentTargetDirectory);

        foreach (var file in Directory.EnumerateFiles(currentSourceDirectory))
        {
            var targetPath = Path.Combine(currentTargetDirectory, Path.GetFileName(file));
            File.Copy(file, targetPath, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(currentSourceDirectory))
        {
            var relativePath = Path.GetRelativePath(sourceRootDirectory, directory);
            if (IsExcludedRootDirectory(relativePath, excludedRootDirectoryNames))
            {
                continue;
            }

            CopyDirectoryCore(
                directory,
                Path.Combine(currentTargetDirectory, Path.GetFileName(directory)),
                sourceRootDirectory,
                excludedRootDirectoryNames
            );
        }
    }

    private static bool IsExcludedRootDirectory(
        string relativePath,
        HashSet<string>? excludedRootDirectoryNames
    )
    {
        if (excludedRootDirectoryNames is null || excludedRootDirectoryNames.Count == 0)
        {
            return false;
        }

        var firstSegment = relativePath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            )
            .FirstOrDefault();
        return firstSegment is not null && excludedRootDirectoryNames.Contains(firstSegment);
    }

    public static void RequirePublishRoot(string publishRoot)
    {
        var requiredFiles = new[]
        {
            AppConstants.ExecutableName,
            Path.Combine("assets", "webui", "upstream", "dist", "index.html"),
            Path.Combine("assets", "webui", "upstream", "sync.json"),
        };

        foreach (var relativePath in requiredFiles)
        {
            var fullPath = Path.Combine(publishRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Publish output is missing required file. Run publish first: {fullPath}",
                    fullPath
                );
            }
        }
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
