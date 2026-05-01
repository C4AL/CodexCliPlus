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
        "package-online-installer",
        "package-offline-installer",
        "package-update",
        "verify-package",
        "write-checksums",
        "export-public-release",
        "clean-artifacts",
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
                "package-online-installer" => await PackageCommands.PackageInstallerAsync(
                    context,
                    InstallerPackageKind.Online
                ),
                "package-offline-installer" => await PackageCommands.PackageInstallerAsync(
                    context,
                    InstallerPackageKind.Offline
                ),
                "package-update" => await PackageCommands.PackageUpdateAsync(context),
                "verify-package" => await PackageCommands.VerifyPackagesAsync(context),
                "write-checksums" => await ReleaseArtifactCommands.WriteChecksumsAsync(context),
                "export-public-release" => await ReleaseArtifactCommands.ExportPublicReleaseAsync(
                    context
                ),
                "clean-artifacts" => ArtifactCleanupCommands.CleanAsync(context),
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

    public string CacheRoot => Path.Combine(Options.OutputRoot, "cache");

    public string AssetManifestPath => Path.Combine(AssetsRoot, "asset-manifest.json");

    public string WebUiAssetsRoot => Path.Combine(AssetsRoot, "webui");

    public string WebUiGeneratedRoot => Path.Combine(WebUiAssetsRoot, "upstream");

    public string WebUiGeneratedDistRoot => Path.Combine(WebUiGeneratedRoot, "dist");

    public string WebUiGeneratedSyncMetadataPath => Path.Combine(WebUiGeneratedRoot, "sync.json");

    public string PublishRoot => Path.Combine(Options.OutputRoot, "publish", Options.Runtime);

    public string PackageRoot => Path.Combine(Options.OutputRoot, "packages");

    public string ChecksumsPath => Path.Combine(Options.OutputRoot, "SHA256SUMS.txt");

    public string ReleaseManifestPath => Path.Combine(Options.OutputRoot, "release-manifest.json");

    public string PublicReleaseRoot => Path.Combine(Options.OutputRoot, "public-release");

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

    public string WebUiNodeModulesCacheRoot => Path.Combine(CacheRoot, "webui-node-modules");

    public string BackendSourceRoot =>
        Path.Combine(Options.RepositoryRoot, "resources", "backend", "source");

    public string BackendSourceManifestPath =>
        Path.Combine(
            Options.RepositoryRoot,
            "resources",
            "backend",
            "backend-source-manifest.json"
        );
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
