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

    public bool CleanupInstallerAfterInstallDefault { get; init; }

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
        var displayPngPath = Path.Combine(
            context.Options.RepositoryRoot,
            "resources",
            "icons",
            "codexcliplus-display.png"
        );
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
            Favicon = File.Exists(displayPngPath)
                ? displayPngPath
                : File.Exists(iconPath) ? iconPath : null,
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
