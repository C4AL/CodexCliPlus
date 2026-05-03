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

public static class InstallerMetadata
{
    private static readonly string[] RequiredWebUiFiles =
    [
        "assets/webui/upstream/dist/index.html",
        "assets/webui/upstream/dist/assets/*",
        "assets/webui/upstream/sync.json",
    ];

    public static async Task WriteAsync(
        BuildContext context,
        string? appPackageRoot,
        string installerStageRoot,
        WebView2RuntimeAssets webView2Assets,
        InstallerPackageKind packageKind,
        OnlineInstallerPayload? onlinePayload
    )
    {
        var isOnline = packageKind == InstallerPackageKind.Online;
        var packagingRoot = isOnline
            ? Path.Combine(installerStageRoot, "packaging")
            : Path.Combine(
                appPackageRoot ?? throw new ArgumentNullException(nameof(appPackageRoot)),
                "packaging"
            );
        Directory.CreateDirectory(packagingRoot);
        var bootstrapper = webView2Assets.Bootstrapper;
        var onlineBootstrapper = new Dictionary<string, object?>
        {
            ["fileName"] = bootstrapper.FileName,
            ["sourceUrl"] = bootstrapper.SourceUrl,
            ["silentArguments"] = bootstrapper.SilentArguments,
        };
        if (bootstrapper.Bundled)
        {
            onlineBootstrapper["packagedPath"] = bootstrapper.PackagedPath;
            onlineBootstrapper["size"] = bootstrapper.Size;
            onlineBootstrapper["sha256"] = bootstrapper.Sha256;
        }

        var webView2 = new Dictionary<string, object?>
        {
            ["required"] = true,
            ["runtime"] = "Microsoft Edge WebView2 Runtime",
            ["detection"] = "CoreWebView2Environment.GetAvailableBrowserVersionString",
            ["bundledFirst"] = !isOnline,
            ["installStrategy"] = isOnline
                ? "download-bootstrapper-only"
                : "online-bootstrapper-then-bundled-standalone",
            ["onlineBootstrapper"] = onlineBootstrapper,
            ["downloadPage"] = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
            ["failureBehavior"] = "缺少 WebView2 且自动安装失败时阻止启动，并显示中文原因。",
            ["note"] = isOnline
                ? "Online SKU does not bundle WebView2 installers; machines missing WebView2 download Microsoft's bootstrapper during setup."
                : "Offline SKU bundles the WebView2 standalone runtime for no-network fallback and does not promise a seconds-level install.",
        };
        if (!isOnline && webView2Assets.OptionalStandaloneX64 is { } standaloneX64)
        {
            webView2["bundledStandaloneX64"] = new
            {
                standaloneX64.FileName,
                standaloneX64.PackagedPath,
                standaloneX64.SourceUrl,
                standaloneX64.SilentArguments,
                standaloneX64.Size,
                standaloneX64.Sha256,
            };
        }

        await WriteJsonAsync(
            Path.Combine(packagingRoot, "dependency-precheck.json"),
            new
            {
                installerSku = isOnline ? "online" : "offline",
                webView2,
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
                    verifyWithFiles = "assets/webui/upstream/dist/index.html + assets/webui/upstream/dist/assets/* + assets/webui/upstream/sync.json",
                    note = "The desktop shell serves the vendored official WebUI from local packaged files instead of a remote URL.",
                },
                installer = new
                {
                    sku = isOnline ? "online" : "offline",
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
                    expectedInstallerAsset = $"{AppConstants.InstallerNamePrefix}.Online.{context.Options.Version}.exe",
                    fallbackInstallerAsset = $"{AppConstants.InstallerNamePrefix}.Offline.{context.Options.Version}.exe",
                    updateKind = "file-manifest-diff",
                    installedBuildCanLaunchUpdater = true,
                    onlineInstallerPayload = onlinePayload,
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
