using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using CPAD.Core.Constants;

namespace CPAD.BuildTool;

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
        "publish",
        "package-portable",
        "package-dev",
        "package-installer",
        "verify-package"
    ];

    public static async Task<int> ExecuteAsync(
        string[] args,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        IProcessRunner? processRunner = null,
        ISigningService? signingService = null)
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
            signingService ??= new NoOpSigningService();

            var context = new BuildContext(options, logger, processRunner, signingService);
            logger.Info($"CPAD.BuildTool {options.Command}");
            logger.Info($"Repository: {options.RepositoryRoot}");
            logger.Info($"Output: {options.OutputRoot}");
            logger.Info($"Configuration: {options.Configuration}; Runtime: {options.Runtime}; Version: {options.Version}");

            return options.Command.ToLowerInvariant() switch
            {
                "fetch-assets" => await AssetCommands.FetchAssetsAsync(context),
                "verify-assets" => await AssetCommands.VerifyAssetsAsync(context),
                "publish" => await PublishCommands.PublishAsync(context),
                "package-portable" => await PackageCommands.PackagePortableAsync(context),
                "package-dev" => await PackageCommands.PackageDevAsync(context),
                "package-installer" => await PackageCommands.PackageInstallerAsync(context),
                "verify-package" => await PackageCommands.VerifyPackagesAsync(context),
                _ => 1
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
        logger.Info("CPAD.BuildTool");
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
    string Version)
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
            version);
        return true;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CliProxyApiDesktop.sln")))
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
    ISigningService signingService)
{
    public BuildOptions Options { get; } = options;

    public BuildLogger Logger { get; } = logger;

    public IProcessRunner ProcessRunner { get; } = processRunner;

    public ISigningService SigningService { get; } = signingService;

    public string AssetsRoot => Path.Combine(Options.OutputRoot, "assets");

    public string AssetManifestPath => Path.Combine(AssetsRoot, "asset-manifest.json");

    public string PublishRoot => Path.Combine(Options.OutputRoot, "publish", Options.Runtime);

    public string PackageRoot => Path.Combine(Options.OutputRoot, "packages");

    public string InstallerRoot => Path.Combine(Options.OutputRoot, "installer", Options.Runtime);

    public string ToolsRoot => Path.Combine(Options.OutputRoot, "tools");

    public string WebUiRoot => Path.Combine(Options.RepositoryRoot, "resources", "webui", "upstream");

    public string WebUiModulesRoot => Path.Combine(Options.RepositoryRoot, "resources", "webui", "modules");

    public string WebUiOverlayModuleRoot => Path.Combine(WebUiModulesRoot, "cpa-uv-overlay");

    public string WebUiOverlayManifestPath => Path.Combine(WebUiOverlayModuleRoot, "module.json");

    public string WebUiOverlaySourceRoot => Path.Combine(WebUiOverlayModuleRoot, "source");

    public string WebUiSourceRoot => Path.Combine(WebUiRoot, "source");

    public string WebUiSourcePackageJsonPath => Path.Combine(WebUiSourceRoot, "package.json");

    public string WebUiSourcePackageLockPath => Path.Combine(WebUiSourceRoot, "package-lock.json");

    public string WebUiSourceNodeModulesRoot => Path.Combine(WebUiSourceRoot, "node_modules");

    public string WebUiSourceDistRoot => Path.Combine(WebUiSourceRoot, "dist");

    public string WebUiVendoredDistRoot => Path.Combine(WebUiRoot, "dist");

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
        CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        BuildLogger logger,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
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
    Task SignAsync(string artifactPath, BuildContext context, CancellationToken cancellationToken = default);
}

public sealed class NoOpSigningService : ISigningService
{
    public async Task SignAsync(string artifactPath, BuildContext context, CancellationToken cancellationToken = default)
    {
        var markerPath = $"{artifactPath}.unsigned.json";
        var marker = new
        {
            artifact = Path.GetFileName(artifactPath),
            signing = "skipped",
            reason = "No signing certificate configured for the first BuildTool release chain."
        };
        await File.WriteAllTextAsync(
            markerPath,
            JsonSerializer.Serialize(marker, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }
}

public static class AssetCommands
{
    private static readonly string[] RequiredBackendFiles =
    [
        "cli-proxy-api.exe",
        "LICENSE",
        "README.md",
        "README_CN.md",
        "config.example.yaml"
    ];

    public static async Task<int> FetchAssetsAsync(BuildContext context)
    {
        var sourceDirectory = Path.Combine(
            context.Options.RepositoryRoot,
            "resources",
            "backend",
            "windows-x64");
        SafeFileSystem.CleanDirectory(context.AssetsRoot, context.Options.OutputRoot);
        var backendTarget = Path.Combine(context.AssetsRoot, "backend", "windows-x64");

        if (Directory.Exists(sourceDirectory) && RequiredBackendFiles.All(fileName => File.Exists(Path.Combine(sourceDirectory, fileName))))
        {
            foreach (var fileName in RequiredBackendFiles)
            {
                var sourcePath = Path.Combine(sourceDirectory, fileName);
                Directory.CreateDirectory(backendTarget);
                File.Copy(sourcePath, Path.Combine(backendTarget, fileName), overwrite: true);
                context.Logger.Info($"asset copied: backend/windows-x64/{fileName}");
            }
        }
        else
        {
            context.Logger.Info($"local backend resources are incomplete; downloading {BackendReleaseMetadata.ArchiveUrl}");
            await DownloadBackendArchiveAsync(backendTarget, context.Logger);
            sourceDirectory = BackendReleaseMetadata.ArchiveUrl;
        }

        var manifest = await BuildAssetManifest.CreateAsync(
            context.Options.Version,
            context.Options.Runtime,
            sourceDirectory,
            context.AssetsRoot,
            cancellationToken: default);
        Directory.CreateDirectory(context.AssetsRoot);
        await manifest.WriteAsync(context.AssetManifestPath);
        context.Logger.Info($"asset manifest: {context.AssetManifestPath}");
        return 0;
    }

    public static async Task<int> VerifyAssetsAsync(BuildContext context)
    {
        if (!File.Exists(context.AssetManifestPath))
        {
            context.Logger.Info($"asset manifest not found; fetching assets: {context.AssetManifestPath}");
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
                archiveBytes = await httpClient.GetByteArrayAsync(BackendReleaseMetadata.ArchiveUrl);
                break;
            }
            catch (Exception exception)
            {
                lastError = exception;
                logger.Error($"backend archive download attempt {attempt}/3 failed: {exception.Message}");
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
                lastError);
        }

        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
        if (!string.Equals(actualSha256, BackendReleaseMetadata.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Downloaded backend archive hash mismatch. Expected {BackendReleaseMetadata.ArchiveSha256}, got {actualSha256}.");
        }

        using var archiveStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        foreach (var requiredFile in RequiredBackendFiles)
        {
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(item.Name, requiredFile, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                throw new InvalidDataException($"Downloaded backend archive is missing {requiredFile}.");
            }

            entry.ExtractToFile(Path.Combine(backendTarget, requiredFile), overwrite: true);
            logger.Info($"asset downloaded: backend/windows-x64/{requiredFile}");
        }
    }

    public static IReadOnlyList<string> RequiredFiles => RequiredBackendFiles;
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
            excludedRootDirectoryNames: ["node_modules", "dist"]);
        SafeFileSystem.CopyDirectory(context.WebUiOverlaySourceRoot, context.WebUiBuildSourceRoot);

        context.Logger.Info("installing vendored WebUI dependencies in temporary worktree");
        var installExitCode = await context.ProcessRunner.RunAsync(
            ResolveNpmExecutable(),
            ["ci"],
            context.WebUiBuildSourceRoot,
            context.Logger);
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
            context.Logger);
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

        SafeFileSystem.CleanDirectory(context.WebUiVendoredDistRoot, context.Options.RepositoryRoot);
        SafeFileSystem.CopyDirectory(context.WebUiBuildDistRoot, context.WebUiVendoredDistRoot);
        context.Logger.Info($"vendored WebUI dist refreshed: {context.WebUiVendoredDistRoot}");
        return 0;
    }

    private static void EnsureVendoredLayout(BuildContext context)
    {
        if (!Directory.Exists(context.WebUiSourceRoot))
        {
            throw new DirectoryNotFoundException($"Vendored WebUI source directory not found: {context.WebUiSourceRoot}");
        }

        if (!File.Exists(context.WebUiSourcePackageJsonPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI package.json not found: {context.WebUiSourcePackageJsonPath}",
                context.WebUiSourcePackageJsonPath);
        }

        if (!File.Exists(context.WebUiSourcePackageLockPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI package-lock.json not found: {context.WebUiSourcePackageLockPath}",
                context.WebUiSourcePackageLockPath);
        }

        if (!File.Exists(context.WebUiSyncMetadataPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI sync metadata not found: {context.WebUiSyncMetadataPath}",
                context.WebUiSyncMetadataPath);
        }

        if (!Directory.Exists(context.WebUiOverlayModuleRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI overlay directory not found: {context.WebUiOverlayModuleRoot}");
        }

        if (!File.Exists(context.WebUiOverlayManifestPath))
        {
            throw new FileNotFoundException(
                $"Vendored WebUI overlay manifest not found: {context.WebUiOverlayManifestPath}",
                context.WebUiOverlayManifestPath);
        }

        if (!Directory.Exists(context.WebUiOverlaySourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Vendored WebUI overlay source directory not found: {context.WebUiOverlaySourceRoot}");
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

        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

        var appProject = Path.Combine(context.Options.RepositoryRoot, "src", "CPAD.App", "CPAD.App.csproj");
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
            "/p:PublishSingleFile=false"
        };
        var exitCode = await context.ProcessRunner.RunAsync(
            "dotnet",
            arguments,
            context.Options.RepositoryRoot,
            context.Logger);
        if (exitCode != 0)
        {
            context.Logger.Error($"dotnet publish failed with exit code {exitCode}.");
            return exitCode;
        }

        SafeFileSystem.CopyDirectory(
            Path.Combine(context.AssetsRoot, "backend"),
            Path.Combine(context.PublishRoot, "assets", "backend"));
        CopyBackendLicenseDocument(context);
        await WritePublishManifestAsync(context);
        context.Logger.Info($"publish output: {context.PublishRoot}");
        return 0;
    }

    private static void CopyBackendLicenseDocument(BuildContext context)
    {
        var source = Path.Combine(context.AssetsRoot, "backend", "windows-x64", "LICENSE");
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Backend license missing from verified assets.", source);
        }

        var targetDirectory = Path.Combine(context.PublishRoot, "Licenses");
        Directory.CreateDirectory(targetDirectory);
        File.Copy(source, Path.Combine(targetDirectory, "CLIProxyAPI.LICENSE.txt"), overwrite: true);
    }

    private static async Task WritePublishManifestAsync(BuildContext context)
    {
        var manifest = new PublishManifest
        {
            Product = "Cli Proxy API Desktop",
            Version = context.Options.Version,
            Runtime = context.Options.Runtime,
            Configuration = context.Options.Configuration,
            Application = "CPAD.exe",
            AssetsManifest = Path.GetRelativePath(context.PublishRoot, context.AssetManifestPath)
        };
        await File.WriteAllTextAsync(
            Path.Combine(context.PublishRoot, "publish-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

public static class PackageCommands
{
    private const string ProductName = "Cli Proxy API Desktop";
    private const string ProductKey = "CPAD";
    private const string AppUserModelId = "BlackblockInc.CPAD";

    public static async Task<int> PackagePortableAsync(BuildContext context)
    {
        var stageRoot = Path.Combine(context.PackageRoot, "staging", "portable");
        SafeFileSystem.RequirePublishRoot(context.PublishRoot);
        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, stageRoot);
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "portable-mode.json"),
            JsonSerializer.Serialize(
                new
                {
                    dataMode = "portable",
                    dataRoot = ".\\data",
                    updatePolicy = new
                    {
                        channel = "stable",
                        automaticStartupCheck = false,
                        automaticInstall = false,
                        reason = "Portable builds avoid system-level traces and do not auto-update by default."
                    }
                },
                JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var packagePath = Path.Combine(
            context.PackageRoot,
            $"CPAD.Portable.{context.Options.Version}.{context.Options.Runtime}.zip");
        await CreatePackageAsync(context, stageRoot, packagePath, "portable");
        return 0;
    }

    public static async Task<int> PackageDevAsync(BuildContext context)
    {
        var stageRoot = Path.Combine(context.PackageRoot, "staging", "dev");
        SafeFileSystem.RequirePublishRoot(context.PublishRoot);
        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, Path.Combine(stageRoot, "app"));
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "app", "dev-mode.json"),
            JsonSerializer.Serialize(
                new
                {
                    dataMode = "development",
                    dataRoot = "app\\artifacts\\dev-data",
                    updatePolicy = new
                    {
                        channel = "stable",
                        automaticStartupCheck = false,
                        automaticInstall = false,
                        reason = "Development packages keep update checks manual so local builds are not replaced by releases."
                    }
                },
                JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Directory.CreateDirectory(Path.Combine(stageRoot, "app", "artifacts", "dev-data"));
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "app", "artifacts", "dev-data", ".gitkeep"),
            string.Empty,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var packagePath = Path.Combine(
            context.PackageRoot,
            $"CPAD.Dev.{context.Options.Version}.{context.Options.Runtime}.zip");
        await CreatePackageAsync(context, stageRoot, packagePath, "development");
        return 0;
    }

    public static async Task<int> PackageInstallerAsync(BuildContext context)
    {
        SafeFileSystem.RequirePublishRoot(context.PublishRoot);
        var stageRoot = Path.Combine(context.InstallerRoot, "stage");
        var appPackageRoot = Path.Combine(stageRoot, "app-package");
        var payloadArchivePath = Path.Combine(stageRoot, "publish.7z");
        var installerOutputPath = Path.Combine(
            context.PackageRoot,
            $"CPAD.Setup.{context.Options.Version}.exe");

        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, appPackageRoot);

        var installerPlan = new InstallerPlan
        {
            ProductName = ProductName,
            InstallerName = Path.GetFileName(installerOutputPath),
            AppUserModelId = AppUserModelId,
            CurrentUserDefault = true,
            PayloadDirectory = "app-package",
            MicaSetupRoute = true,
            RequestExecutionLevel = "user",
            InstallDirectoryHint = "%LocalAppData%\\Programs\\CPAD",
            LaunchAfterInstall = true,
            StableReleaseSource = "https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases/latest",
            BetaChannelReserved = true
        };
        await WriteJsonAsync(Path.Combine(stageRoot, "mica-setup.json"), installerPlan);
        await InstallerMetadata.WriteAsync(context, appPackageRoot, stageRoot);

        var toolchain = await MicaSetupToolchain.AcquireAsync(context);
        var archiveExitCode = await CreateMicaPayloadArchiveAsync(context, toolchain, appPackageRoot, payloadArchivePath);
        if (archiveExitCode != 0)
        {
            return archiveExitCode;
        }

        var micaConfigPath = Path.Combine(stageRoot, "micasetup.json");
        var micaConfig = MicaSetupConfig.Create(context, payloadArchivePath, installerOutputPath);
        await File.WriteAllTextAsync(
            micaConfigPath,
            JsonSerializer.Serialize(micaConfig, MicaSetupConfig.JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var builder = new MicaSetupInstallerBuilder(toolchain);
        var buildExitCode = await builder.BuildAsync(context, micaConfigPath, payloadArchivePath, installerOutputPath);
        if (buildExitCode != 0)
        {
            return buildExitCode;
        }

        await context.SigningService.SignAsync(installerOutputPath, context);
        Directory.CreateDirectory(Path.Combine(stageRoot, "output"));
        File.Copy(installerOutputPath, Path.Combine(stageRoot, "output", Path.GetFileName(installerOutputPath)), overwrite: true);

        var stagingPackagePath = Path.Combine(
            context.PackageRoot,
            $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip");
        await CreatePackageAsync(context, stageRoot, stagingPackagePath, "installer-staging");
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

    private static async Task CreatePackageAsync(
        BuildContext context,
        string stageRoot,
        string packagePath,
        string packageType)
    {
        Directory.CreateDirectory(context.PackageRoot);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        var manifest = new PackageManifest
        {
            Product = "Cli Proxy API Desktop",
            Version = context.Options.Version,
            Runtime = context.Options.Runtime,
            PackageType = packageType,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "package-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        ZipFile.CreateFromDirectory(stageRoot, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        await context.SigningService.SignAsync(packagePath, context);
        context.Logger.Info($"{packageType} package: {packagePath}");
    }

    private static async Task<int> CreateMicaPayloadArchiveAsync(
        BuildContext context,
        MicaSetupToolchain toolchain,
        string appPackageRoot,
        string archivePath)
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
            context.Logger);
        if (exitCode == 0 && File.Exists(archivePath))
        {
            return 0;
        }

        context.Logger.Error("7z did not create a MicaSetup payload archive; falling back to a zip container with the same payload.");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(appPackageRoot, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return File.Exists(archivePath) ? 0 : exitCode == 0 ? 1 : exitCode;
    }

    private static Task WriteJsonAsync(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

public sealed class PackageVerifier(BuildContext context)
{
    public IReadOnlyList<string> VerifyAll()
    {
        var failures = new List<string>();
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Portable.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "CPAD.exe",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Portable.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "portable-mode.json",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Portable.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "assets/webui/upstream/dist/index.html",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Portable.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "assets/webui/upstream/sync.json",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Dev.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app/CPAD.exe",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Dev.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app/dev-mode.json",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Dev.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app/artifacts/dev-data/.gitkeep",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Dev.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app/assets/webui/upstream/dist/index.html",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Dev.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app/assets/webui/upstream/sync.json",
            failures);
        VerifyExecutable(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.exe"),
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app-package/CPAD.exe",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app-package/assets/webui/upstream/dist/index.html",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app-package/assets/webui/upstream/sync.json",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "mica-setup.json",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "micasetup.json",
            failures);
        VerifyZipExecutable(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            $"output/CPAD.Setup.{context.Options.Version}.exe",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app-package/packaging/uninstall-cleanup.json",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app-package/packaging/dependency-precheck.json",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app-package/packaging/update-policy.json",
            failures);

        return failures;
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
                string.Equals(NormalizeEntryName(entry.FullName), requiredEntry, StringComparison.OrdinalIgnoreCase));
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

    private static void VerifyZipExecutable(string path, string requiredEntry, List<string> failures)
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
                string.Equals(NormalizeEntryName(item.FullName), requiredEntry, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                failures.Add($"Package '{path}' is missing entry '{requiredEntry}'.");
                return;
            }

            if (entry.Length < 64)
            {
                failures.Add($"Installer executable is too small to be valid: {path}!{requiredEntry}");
                return;
            }

            using var stream = entry.Open();
            var validationFailure = WindowsExecutableValidation.ValidateStream(stream, $"{path}!{requiredEntry}");
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
        "assets/webui/upstream/sync.json"
    ];

    public static async Task WriteAsync(BuildContext context, string appPackageRoot, string installerStageRoot)
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
                    note = "The desktop shell is a WebView2 host and cannot render the vendored official WebUI without the runtime."
                },
                runtime = new
                {
                    selfContained = true,
                    targetFramework = "net10.0-windows",
                    bundledFirst = true,
                    requiredExecutable = "CPAD.exe",
                    onlineFallback = "https://dotnet.microsoft.com/download/dotnet/10.0",
                    note = "The installed app is self-contained; online runtime repair is reserved for future framework-dependent payloads."
                },
                backend = new
                {
                    bundledPath = "assets/backend/windows-x64",
                    requiredFiles = AssetCommands.RequiredFiles,
                    bundledFirst = true,
                    onlineFallback = "https://github.com/router-for-me/CLIProxyAPI/releases",
                    verifyWithManifest = "assets/backend/windows-x64 + asset-manifest.json"
                },
                webUi = new
                {
                    bundledPath = "assets/webui/upstream",
                    requiredFiles = RequiredWebUiFiles,
                    bundledFirst = true,
                    verifyWithFiles = "assets/webui/upstream/dist/index.html + assets/webui/upstream/sync.json",
                    note = "The desktop shell serves the vendored official WebUI from local packaged files instead of a remote URL."
                },
                installer = new
                {
                    precheck = "Setup payload contains dependency-precheck.json so the installer chain can validate WebView2, bundled backend files, and vendored WebUI assets before launch.",
                    launchAfterInstall = true
                }
            });

        await WriteJsonAsync(
            Path.Combine(packagingRoot, "update-policy.json"),
            new
            {
                stable = new
                {
                    enabled = true,
                    source = "https://github.com/Blackblock-inc/Cli-Proxy-API-Desktop/releases/latest",
                    expectedInstallerAsset = $"CPAD.Setup.{context.Options.Version}.exe",
                    installedBuildCanLaunchInstaller = true
                },
                beta = new
                {
                    reserved = true,
                    enabled = false
                },
                portable = new
                {
                    automaticStartupCheck = false,
                    automaticInstall = false,
                    reason = "Portable mode should not write system-level traces or replace itself automatically."
                }
            });

        var cleanup = new InstallerCleanupManifest
        {
            ProductKey = "CPAD",
            KeepUserDataOption = true,
            KeepMyDataOptionName = "KeepMyData",
            KeepMyDataDefault = false,
            DefaultUninstallProfile = "full-clean",
            SafeDeleteRoots =
            [
                "%LocalAppData%\\CPAD",
                "%AppData%\\CPAD",
                "%LocalAppData%\\Programs\\CPAD",
                "%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs\\Cli Proxy API Desktop",
                "%AppData%\\Microsoft\\Windows\\Start Menu\\Programs\\Cli Proxy API Desktop"
            ],
            AlwaysDelete =
            [
                "%LocalAppData%\\Programs\\CPAD",
                "%AppData%\\Microsoft\\Windows\\Start Menu\\Programs\\Cli Proxy API Desktop",
                "%ProgramData%\\Microsoft\\Windows\\Start Menu\\Programs\\Cli Proxy API Desktop"
            ],
            DeleteByDefault =
            [
                "%LocalAppData%\\CPAD\\config",
                "%LocalAppData%\\CPAD\\config\\secrets\\*.bin",
                "%LocalAppData%\\CPAD\\cache",
                "%LocalAppData%\\CPAD\\cache\\updates",
                "%LocalAppData%\\CPAD\\logs",
                "%LocalAppData%\\CPAD\\backend",
                "%LocalAppData%\\CPAD\\diagnostics",
                "%LocalAppData%\\CPAD\\runtime",
                "%AppData%\\CPAD"
            ],
            PreserveWhenKeepMyData =
            [
                "%LocalAppData%\\CPAD\\config",
                "%LocalAppData%\\CPAD\\config\\secrets",
                "%LocalAppData%\\CPAD\\logs",
                "%LocalAppData%\\CPAD\\diagnostics"
            ],
            RegistryValues =
            [
                "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\CPAD",
                "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CPAD"
            ],
            FirewallRules =
            [
                "CPAD",
                "Cli Proxy API Desktop"
            ],
            ScheduledTasks =
            [
                "CPAD",
                "Cli Proxy API Desktop"
            ],
            SafetyRules =
            [
                "Only delete roots whose resolved final segment is CPAD or Cli Proxy API Desktop.",
                "Never follow a cleanup item outside AppData, LocalAppData, the selected install directory, Start Menu, CPAD firewall rules, or CPAD scheduled tasks.",
                "Default uninstall runs the full-clean profile and deletes CPAD user data.",
                "KeepMyData preserves config, credential references, logs, and diagnostics while removing installed binaries and integration points."
            ]
        };
        await WriteJsonAsync(Path.Combine(packagingRoot, "uninstall-cleanup.json"), cleanup);
        await WriteJsonAsync(Path.Combine(installerStageRoot, "uninstall-cleanup.json"), cleanup);
    }

    private static Task WriteJsonAsync(string path, object value)
    {
        return File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

public sealed class MicaSetupToolchain
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/lemutec/MicaSetup/releases/latest";

    private MicaSetupToolchain(
        string rootDirectory,
        string sevenZipPath,
        string makeMicaPath,
        string templatePath,
        string version)
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
        var repoOwnedRoot = Path.Combine(context.Options.RepositoryRoot, "build", "micasetup", "toolchain");
        var repoOwnedToolchain = await TryCreateFromDirectoryAsync(repoOwnedRoot, "repo-owned");
        if (repoOwnedToolchain is not null)
        {
            context.Logger.Info($"MicaSetup tools repo-owned: {repoOwnedToolchain.Version}");
            return repoOwnedToolchain;
        }

        var root = Path.Combine(context.ToolsRoot, "micasetup");
        var cachedToolchain = await TryCreateFromDirectoryAsync(root, "cached");
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
        var asset = release.Assets
            .FirstOrDefault(item => item.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                && item.Name.StartsWith("MicaSetup.Tools.", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Latest MicaSetup release does not contain a MicaSetup.Tools nupkg asset.");

        var downloadPath = Path.Combine(root, "download", asset.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
        await DownloadWithRetryAsync(asset.DownloadUrl, downloadPath, context.Logger);
        ZipFile.ExtractToDirectory(downloadPath, root, overwriteFiles: true);

        if (!File.Exists(sevenZip) || !File.Exists(makeMica) || !File.Exists(template))
        {
            throw new FileNotFoundException("Downloaded MicaSetup.Tools package is missing makemica.exe, 7z.exe, or template/default.7z.");
        }

        await File.WriteAllTextAsync(versionPath, release.TagName, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        context.Logger.Info($"MicaSetup tools downloaded: {release.TagName}");
        return new MicaSetupToolchain(root, sevenZip, makeMica, template, release.TagName);
    }

    private static async Task<MicaSetupToolchain?> TryCreateFromDirectoryAsync(string root, string fallbackVersion)
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

        return new MicaSetupToolchain(root, sevenZip, makeMica, template, version);
    }

    private static async Task<MicaSetupRelease> QueryLatestReleaseAsync()
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CPAD-BuildTool", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"MicaSetup latest release query failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var assets = new List<MicaSetupReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
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
            assets);
    }

    private static async Task DownloadWithRetryAsync(string url, string targetPath, BuildLogger logger)
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
                logger.Error($"MicaSetup tools download attempt {attempt}/3 failed: {exception.Message}");
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                }
            }
        }

        throw new InvalidOperationException($"Failed to download MicaSetup tools: {lastError?.Message}", lastError);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record MicaSetupRelease(string TagName, IReadOnlyList<MicaSetupReleaseAsset> Assets);

    private sealed record MicaSetupReleaseAsset(string Name, string DownloadUrl);
}

public sealed class MicaSetupInstallerBuilder(MicaSetupToolchain toolchain)
{
    private const string CleanupMethodMarker = "private static void CleanupCpadUserData()";

    private const string UninstallCleanupSource =
        """
                private static void CleanupCpadUserData()
                {
                    RegistyAutoRunHelper.Disable("CPAD");

                    if (Option.Current.KeepMyData)
                    {
                        return;
                    }

                    foreach (string path in EnumerateCpadCleanupRoots())
                    {
                        TryDeleteSafePath(path);
                    }

                    TryDeleteFirewallRule("CPAD");
                    TryDeleteFirewallRule("Cli Proxy API Desktop");
                    TryDeleteScheduledTask("CPAD");
                    TryDeleteScheduledTask("Cli Proxy API Desktop");
                }

                private static IEnumerable<string> EnumerateCpadCleanupRoots()
                {
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CPAD");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CPAD");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CPAD");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Cli Proxy API Desktop");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Cli Proxy API Desktop");
                }

                private static void TryDeleteSafePath(string path)
                {
                    if (string.IsNullOrWhiteSpace(path) || !IsSafeCpadPath(path))
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

                private static bool IsSafeCpadPath(string path)
                {
                    string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string name = Path.GetFileName(fullPath);
                    if (!string.Equals(name, "CPAD", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(name, "Cli Proxy API Desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
                        || IsEqualOrUnder(fullPath, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
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
        string installerOutputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(installerOutputPath)!);
        if (File.Exists(installerOutputPath))
        {
            File.Delete(installerOutputPath);
        }

        if (!HasVisualStudioInstaller())
        {
            context.Logger.Info("Visual Studio Installer not detected; using dotnet msbuild against the official MicaSetup template.");
            return await BuildWithDotnetMsbuildAsync(context, micaConfigPath, payloadArchivePath, installerOutputPath);
        }

        var makeMicaExitCode = await context.ProcessRunner.RunAsync(
            toolchain.MakeMicaPath,
            [micaConfigPath],
            Path.GetDirectoryName(micaConfigPath)!,
            context.Logger);
        if (makeMicaExitCode == 0 && File.Exists(installerOutputPath))
        {
            var validationFailure = WindowsExecutableValidation.ValidateFile(installerOutputPath);
            if (validationFailure is null)
            {
                context.Logger.Info("MicaSetup installer generated by makemica.exe");
                return 0;
            }

            context.Logger.Error($"makemica.exe produced an invalid installer: {validationFailure}");
            File.Delete(installerOutputPath);
        }

        context.Logger.Info(makeMicaExitCode == 0
            ? "makemica.exe completed without a valid installer executable; falling back to dotnet msbuild against the official MicaSetup template."
            : $"makemica.exe failed with exit code {makeMicaExitCode}; falling back to dotnet msbuild against the official MicaSetup template.");
        return await BuildWithDotnetMsbuildAsync(context, micaConfigPath, payloadArchivePath, installerOutputPath);
    }

    private static bool HasVisualStudioInstaller()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var programFilesRoot in GetProgramFilesRoots())
        {
            var installerRoot = Path.Combine(programFilesRoot, "Microsoft Visual Studio", "Installer");
            if (File.Exists(Path.Combine(installerRoot, "setup.exe"))
                || File.Exists(Path.Combine(installerRoot, "vswhere.exe")))
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
        if (!string.IsNullOrWhiteSpace(programFiles)
            && !string.Equals(programFiles, programFilesX86, StringComparison.OrdinalIgnoreCase))
        {
            yield return programFiles;
        }
    }

    private async Task<int> BuildWithDotnetMsbuildAsync(
        BuildContext context,
        string micaConfigPath,
        string payloadArchivePath,
        string installerOutputPath)
    {
        var stageRoot = Path.GetDirectoryName(micaConfigPath)!;
        var distRoot = Path.Combine(stageRoot, ".dist");
        SafeFileSystem.CleanDirectory(distRoot, context.Options.OutputRoot);

        var extractExitCode = await context.ProcessRunner.RunAsync(
            toolchain.SevenZipPath,
            ["x", toolchain.TemplatePath, $"-o{distRoot}", "-y"],
            stageRoot,
            context.Logger);
        if (extractExitCode != 0)
        {
            context.Logger.Error($"MicaSetup template extraction failed with exit code {extractExitCode}.");
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
                "/restore"
            ],
            distRoot,
            context.Logger);
        if (uninstExitCode != 0)
        {
            context.Logger.Error($"MicaSetup uninstaller build failed with exit code {uninstExitCode}.");
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
                "/restore"
            ],
            distRoot,
            context.Logger);
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

    private static void ApplyTemplateConfig(BuildContext context, string distRoot, string payloadArchivePath)
    {
        var setupResourcePath = Path.Combine(distRoot, "Resources", "Setups", "publish.7z");
        Directory.CreateDirectory(Path.GetDirectoryName(setupResourcePath)!);
        File.Copy(payloadArchivePath, setupResourcePath, overwrite: true);

        var cleanupManifestSource = Path.Combine(Path.GetDirectoryName(payloadArchivePath)!, "uninstall-cleanup.json");
        if (File.Exists(cleanupManifestSource))
        {
            File.Copy(
                cleanupManifestSource,
                Path.Combine(distRoot, "Resources", "Setups", "uninstall-cleanup.json"),
                overwrite: true);
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
            (Path.Combine(repositoryRoot, "LICENSE.txt"), "CPAD.LICENSE.txt"),
            (Path.Combine(context.AssetsRoot, "backend", "windows-x64", "LICENSE"), "CLIProxyAPI.LICENSE.txt"),
            (Path.Combine(repositoryRoot, "resources", "licenses", "BetterGI.GPL-3.0.txt"), "BetterGI.GPL-3.0.txt"),
            (Path.Combine(repositoryRoot, "resources", "licenses", "NOTICE.txt"), "NOTICE.txt")
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
        source = source.Replace(".UseElevated()", string.Empty, StringComparison.Ordinal);
        source = ReplaceBetween(source, ".UseSingleInstance(\"", "\")", isUninstaller ? "BlackblockInc.CPAD.Uninstall" : "BlackblockInc.CPAD.Setup");
        source = ReplaceBetween(source, "[assembly: Guid(\"", "\")]", "6f8dd8b7-21ea-4c6b-9695-40a27874ce4d");
        source = ReplaceBetween(source, "[assembly: AssemblyTitle(\"", "\")]", isUninstaller ? "Cli Proxy API Desktop Uninstall" : "Cli Proxy API Desktop Setup");
        source = ReplaceBetween(source, "[assembly: AssemblyProduct(\"", "\")]", "Cli Proxy API Desktop");
        source = ReplaceBetween(source, "[assembly: AssemblyDescription(\"", "\")]", isUninstaller ? "Cli Proxy API Desktop Uninstall" : "Cli Proxy API Desktop Setup");
        source = ReplaceBetween(source, "[assembly: AssemblyCompany(\"", "\")]", "Blackblock Inc.");
        source = ReplaceBetween(source, "[assembly: AssemblyVersion(\"", "\")]", NormalizeAssemblyVersion(version));
        source = ReplaceBetween(source, "[assembly: AssemblyFileVersion(\"", "\")]", NormalizeAssemblyVersion(version));
        source = ReplaceBetween(source, "[assembly: RequestExecutionLevel(\"", "\")]", "user");

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
        source = ReplaceAssignment(source, "option.IsUseInstallPathPreferAppDataLocalPrograms", "true");
        source = ReplaceAssignment(source, "option.IsUseInstallPathPreferAppDataRoaming", "false");
        source = ReplaceAssignment(source, "option.IsAllowFullFolderSecurity", "false");
        source = ReplaceAssignment(source, "option.IsAllowFirewall", "false");
        source = ReplaceAssignment(source, "option.IsRefreshExplorer", "true");
        source = ReplaceAssignment(source, "option.IsInstallCertificate", "false");
        source = ReplaceAssignment(source, "option.IsEnableUninstallDelayUntilReboot", "true");
        source = ReplaceAssignment(source, "option.IsEnvironmentVariable", "false");
        source = ReplaceAssignment(source, "option.AppName", "\"Cli Proxy API Desktop\"");
        source = ReplaceAssignment(source, "option.KeyName", "\"CPAD\"");
        source = ReplaceAssignment(source, "option.ExeName", "\"CPAD.exe\"");
        source = ReplaceAssignment(source, "option.DisplayVersion", $"\"{version}\"");
        source = ReplaceAssignment(source, "option.Publisher", "\"Blackblock Inc.\"");
        source = ReplaceAssignment(source, "option.MessageOfPage1", "\"Cli Proxy API Desktop\"");
        source = ReplaceAssignment(source, "option.MessageOfPage2", isUninstaller ? "\"Uninstalling CPAD\"" : "\"Installing CPAD\"");
        source = ReplaceAssignment(source, "option.MessageOfPage3", isUninstaller ? "\"Uninstall completed\"" : "\"Installation completed\"");
        if (isUninstaller)
        {
            source = EnsureOptionAssignment(source, "option.KeepMyData", "false", "option.ExeName");
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PatchForCurrentUserInstall(string distRoot)
    {
        var startMenuPath = Path.Combine(distRoot, "Helper", "System", "StartMenuHelper.cs");
        if (File.Exists(startMenuPath))
        {
            var source = File.ReadAllText(startMenuPath)
                .Replace("Environment.SpecialFolder.CommonApplicationData), @\"Microsoft\\Windows\\Start Menu\\Programs\"", "Environment.SpecialFolder.StartMenu), \"Programs\"", StringComparison.Ordinal)
                .Replace("Environment.SpecialFolder.CommonApplicationData), @\"Microsoft\\Windows\\Start Menu\\Programs\"", "Environment.SpecialFolder.StartMenu), \"Programs\"", StringComparison.Ordinal);
            File.WriteAllText(startMenuPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var registryPath = Path.Combine(distRoot, "Helper", "System", "RegistyUninstallHelper.cs");
        if (File.Exists(registryPath))
        {
            var source = File.ReadAllText(registryPath)
                .Replace("RegistryHive.LocalMachine", "RegistryHive.CurrentUser", StringComparison.Ordinal);
            File.WriteAllText(registryPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var installHelperPath = Path.Combine(distRoot, "Helper", "Setup", "InstallHelper.cs");
        if (File.Exists(installHelperPath))
        {
            var source = File.ReadAllText(installHelperPath)
                .Replace("if (Option.Current.IsCreateRegistryKeys && RuntimeHelper.IsElevated)", "if (Option.Current.IsCreateRegistryKeys)", StringComparison.Ordinal)
                .Replace(
                    "if (RuntimeHelper.IsElevated) { StartMenuHelper.CreateStartMenuFolder(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName), Option.Current.IsCreateUninst); }",
                    "StartMenuHelper.CreateStartMenuFolder(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName), Option.Current.IsCreateUninst);",
                    StringComparison.Ordinal)
                .Replace(
                    """
                    if (RuntimeHelper.IsElevated)
                    {
                        StartMenuHelper.CreateStartMenuFolder(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName), Option.Current.IsCreateUninst);
                    }
                    """,
                    "StartMenuHelper.CreateStartMenuFolder(Option.Current.DisplayName, Path.Combine(Option.Current.InstallLocation, Option.Current.ExeName), Option.Current.IsCreateUninst);",
                    StringComparison.Ordinal);
            File.WriteAllText(installHelperPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var uninstMainViewModel = Path.Combine(distRoot, "ViewModels", "Uninst", "MainViewModel.cs");
        if (File.Exists(uninstMainViewModel))
        {
            var source = File.ReadAllText(uninstMainViewModel)
                .Replace("private bool isElevated = RuntimeHelper.IsElevated;", "private bool isElevated = true;", StringComparison.Ordinal);
            File.WriteAllText(uninstMainViewModel, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
        source = source.Replace("using System.IO;", "using System.Diagnostics;\r\nusing System.IO;", StringComparison.Ordinal);
        source = source.Replace(
            "else { // For security reason, uninst should always keep data because of unundering admin. Option.Current.KeepMyData = true; uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(); if (string.IsNullOrWhiteSpace(uinfo.UninstallData)) { MessageBox.Info(ApplicationDispatcherHelper.MainWindow, \"InstallationInfoLostHint\".Tr()); } }",
            "else { uinfo = PrepareUninstallPathHelper.GetPrepareUninstallPath(); if (string.IsNullOrWhiteSpace(uinfo.UninstallData)) { MessageBox.Info(ApplicationDispatcherHelper.MainWindow, \"InstallationInfoLostHint\".Tr()); } }",
            StringComparison.Ordinal);
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
            StringComparison.Ordinal);
        source = source.Replace(
            "try { RegistyUninstallHelper.Delete(Option.Current.KeyName); }",
            "CleanupCpadUserData(); try { RegistyUninstallHelper.Delete(Option.Current.KeyName); }",
            StringComparison.Ordinal);
        source = source.Replace(
            """
                    try
                    {
                        RegistyUninstallHelper.Delete(Option.Current.KeyName);
                    }
            """,
            """
                    CleanupCpadUserData();

                    try
                    {
                        RegistyUninstallHelper.Delete(Option.Current.KeyName);
                    }
            """,
            StringComparison.Ordinal);

        if (!source.Contains(CleanupMethodMarker, StringComparison.Ordinal))
        {
            source = source.Replace(
                "public static void DeleteUninst()",
                UninstallCleanupSource + "\r\n\r\n                public static void DeleteUninst()",
                StringComparison.Ordinal);
        }

        File.WriteAllText(path, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string EnsureOptionAssignment(string source, string assignmentTarget, string value, string insertAfterTarget)
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
    public string Product { get; init; } = "Cli Proxy API Desktop";

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
        CancellationToken cancellationToken)
    {
        var files = new List<BuildAssetFile>();
        foreach (var path in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(path), "asset-manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            files.Add(new BuildAssetFile
            {
                Path = ToManifestPath(Path.GetRelativePath(assetsRoot, path)),
                Size = new FileInfo(path).Length,
                Sha256 = await ComputeSha256Async(path, cancellationToken)
            });
        }

        return new BuildAssetManifest
        {
            Version = version,
            Runtime = runtime,
            Source = sourceDirectory,
            Files = files
        };
    }

    public static async Task<BuildAssetManifest> ReadAsync(string manifestPath)
    {
        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<BuildAssetManifest>(stream, JsonDefaults.Options)
            ?? throw new InvalidDataException($"Could not read asset manifest: {manifestPath}");
    }

    public async Task WriteAsync(string manifestPath)
    {
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(this, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public IReadOnlyList<string> Verify(string assetsRoot)
    {
        var failures = new List<string>();
        foreach (var file in Files)
        {
            var fullPath = Path.Combine(assetsRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
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

            var actualHash = ComputeSha256Async(fullPath, CancellationToken.None).GetAwaiter().GetResult();
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"Asset hash mismatch: {file.Path}");
            }
        }

        foreach (var requiredFile in AssetCommands.RequiredFiles)
        {
            var manifestPath = $"backend/windows-x64/{requiredFile}";
            if (!Files.Any(file => string.Equals(file.Path, manifestPath, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Required asset missing from manifest: {manifestPath}");
            }
        }

        return failures;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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

    public string RequestExecutionLevel { get; init; } = "user";

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

    public bool IsUseInstallPathPreferAppDataLocalPrograms { get; init; } = true;

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

    public static MicaSetupConfig Create(BuildContext context, string payloadArchivePath, string installerOutputPath)
    {
        var iconPath = Path.Combine(context.Options.RepositoryRoot, "resources", "icons", "ico-transparent.png");
        var noticePath = Path.Combine(context.Options.RepositoryRoot, "resources", "licenses", "NOTICE.txt");
        var licensePath = File.Exists(noticePath)
            ? noticePath
            : Path.Combine(context.Options.RepositoryRoot, "LICENSE.txt");
        return new MicaSetupConfig
        {
            Template = "${MicaDir}/template/default.7z",
            Package = payloadArchivePath,
            Output = installerOutputPath,
            AppName = "Cli Proxy API Desktop",
            KeyName = "CPAD",
            ExeName = "CPAD.exe",
            Publisher = "Blackblock Inc.",
            Version = context.Options.Version,
            TargetFramework = "net472",
            ProductGuid = "6f8dd8b7-21ea-4c6b-9695-40a27874ce4d",
            Favicon = File.Exists(iconPath) ? iconPath : null,
            Icon = File.Exists(iconPath) ? iconPath : null,
            UnIcon = File.Exists(iconPath) ? iconPath : null,
            LicenseFile = File.Exists(licensePath) ? licensePath : null,
            RequestExecutionLevel = "user",
            SingleInstanceMutex = "BlackblockInc.CPAD.Setup",
            MessageOfPage1 = "Cli Proxy API Desktop",
            MessageOfPage2 = "正在安装 CPAD",
            MessageOfPage3 = "安装完成"
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
            throw new InvalidOperationException($"Refusing to clean outside BuildTool output root: {fullTarget}");
        }

        if (Directory.Exists(fullTarget))
        {
            Directory.Delete(fullTarget, recursive: true);
        }

        Directory.CreateDirectory(fullTarget);
    }

    public static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        IReadOnlyCollection<string>? excludedRootDirectoryNames = null)
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
        HashSet<string>? excludedRootDirectoryNames)
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
                excludedRootDirectoryNames);
        }
    }

    private static bool IsExcludedRootDirectory(
        string relativePath,
        HashSet<string>? excludedRootDirectoryNames)
    {
        if (excludedRootDirectoryNames is null || excludedRootDirectoryNames.Count == 0)
        {
            return false;
        }

        var firstSegment = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstSegment is not null && excludedRootDirectoryNames.Contains(firstSegment);
    }

    public static void RequirePublishRoot(string publishRoot)
    {
        var requiredFiles = new[]
        {
            "CPAD.exe",
            Path.Combine("assets", "webui", "upstream", "dist", "index.html"),
            Path.Combine("assets", "webui", "upstream", "sync.json")
        };

        foreach (var relativePath in requiredFiles)
        {
            var fullPath = Path.Combine(publishRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Publish output is missing required file. Run publish first: {fullPath}",
                    fullPath);
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
