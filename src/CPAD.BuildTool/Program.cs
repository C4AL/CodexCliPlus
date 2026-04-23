using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
}

public sealed class BuildLogger(TextWriter standardOutput, TextWriter standardError)
{
    public void Info(string message)
    {
        standardOutput.WriteLine($"[info] {message}");
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
                logger.Error(eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
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
    private const string BackendArchiveUrl =
        "https://github.com/router-for-me/CLIProxyAPI/releases/download/v6.9.34/CLIProxyAPI_6.9.34_windows_amd64.zip";

    private const string BackendArchiveSha256 =
        "34ca9b7bf53a6dd89b874ed3e204371673b7eb1abf34792498af4e65bf204815";

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
            context.Logger.Info($"local backend resources are incomplete; downloading {BackendArchiveUrl}");
            await DownloadBackendArchiveAsync(backendTarget, context.Logger);
            sourceDirectory = BackendArchiveUrl;
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
            throw new FileNotFoundException(
                $"Asset manifest not found. Run fetch-assets first: {context.AssetManifestPath}",
                context.AssetManifestPath);
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
        using var httpClient = new HttpClient();
        byte[]? archiveBytes = null;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                logger.Info($"backend archive download attempt {attempt}/3");
                archiveBytes = await httpClient.GetByteArrayAsync(BackendArchiveUrl);
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
        if (!string.Equals(actualSha256, BackendArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Downloaded backend archive hash mismatch. Expected {BackendArchiveSha256}, got {actualSha256}.");
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

public static class PublishCommands
{
    public static async Task<int> PublishAsync(BuildContext context)
    {
        var verifyCode = await AssetCommands.VerifyAssetsAsync(context);
        if (verifyCode != 0)
        {
            return verifyCode;
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
        await WritePublishManifestAsync(context);
        context.Logger.Info($"publish output: {context.PublishRoot}");
        return 0;
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
    public static async Task<int> PackagePortableAsync(BuildContext context)
    {
        var stageRoot = Path.Combine(context.PackageRoot, "staging", "portable");
        SafeFileSystem.RequirePublishRoot(context.PublishRoot);
        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, stageRoot);
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "portable-mode.json"),
            JsonSerializer.Serialize(new { dataMode = "portable", dataRoot = ".\\data" }, JsonDefaults.Options),
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
        Directory.CreateDirectory(Path.Combine(stageRoot, "dev-data"));
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "dev-data", ".gitkeep"),
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
        SafeFileSystem.CleanDirectory(stageRoot, context.Options.OutputRoot);
        SafeFileSystem.CopyDirectory(context.PublishRoot, Path.Combine(stageRoot, "payload", "app"));

        var installerPlan = new InstallerPlan
        {
            ProductName = "Cli Proxy API Desktop",
            InstallerName = $"CPAD.Setup.{context.Options.Version}.exe",
            AppUserModelId = "BlackblockInc.CPAD",
            CurrentUserDefault = true,
            PayloadDirectory = "payload/app",
            MicaSetupRoute = true
        };
        await File.WriteAllTextAsync(
            Path.Combine(stageRoot, "mica-setup.json"),
            JsonSerializer.Serialize(installerPlan, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var packagePath = Path.Combine(
            context.PackageRoot,
            $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip");
        await CreatePackageAsync(context, stageRoot, packagePath, "installer");
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
            Path.Combine(context.PackageRoot, $"CPAD.Dev.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "app/CPAD.exe",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "payload/app/CPAD.exe",
            failures);
        VerifyZip(
            Path.Combine(context.PackageRoot, $"CPAD.Setup.{context.Options.Version}.{context.Options.Runtime}.zip"),
            "mica-setup.json",
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

    private static string NormalizeEntryName(string name)
    {
        return name.Replace('\\', '/');
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

public sealed class InstallerPlan
{
    public string ProductName { get; init; } = string.Empty;

    public string InstallerName { get; init; } = string.Empty;

    public string AppUserModelId { get; init; } = string.Empty;

    public bool CurrentUserDefault { get; init; }

    public string PayloadDirectory { get; init; } = string.Empty;

    public bool MicaSetupRoute { get; init; }
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

    public static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var targetPath = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    public static void RequirePublishRoot(string publishRoot)
    {
        var appPath = Path.Combine(publishRoot, "CPAD.exe");
        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException($"Publish output is missing CPAD.exe. Run publish first: {appPath}", appPath);
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
