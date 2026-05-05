using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using CodexCliPlus.BuildTool;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Backend;
using CodexCliPlus.Infrastructure.Configuration;
using CodexCliPlus.Infrastructure.Dependencies;
using CodexCliPlus.Infrastructure.Logging;
using CodexCliPlus.Infrastructure.Paths;
using CodexCliPlus.Infrastructure.Platform;
using CodexCliPlus.Infrastructure.Security;
using CodexCliPlus.Infrastructure.Updates;

namespace CodexCliPlus.Tests.Smoke;

[Collection("Smoke")]
[Trait("Category", "Smoke")]
public sealed class SmokeTests
{
    private static readonly string[] BackendAssetFiles =
    [
        BackendExecutableNames.ManagedExecutableFileName,
        "LICENSE",
        "README.md",
        "README_CN.md",
        "config.example.yaml",
    ];

    [Fact]
    public async Task DesktopLaunchSmokeStartsWithIsolatedRootAndLeavesOnlyOwnedProcessesForCleanup()
    {
        using var scope = new SmokeEnvironmentScope();

        Assert.True(
            File.Exists(SmokeEnvironmentScope.ApplicationPath),
            $"Expected desktop executable at '{SmokeEnvironmentScope.ApplicationPath}'."
        );

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = SmokeEnvironmentScope.ApplicationPath,
                WorkingDirectory = Path.GetDirectoryName(SmokeEnvironmentScope.ApplicationPath)!,
                UseShellExecute = false,
            },
        };

        process.StartInfo.Environment["CODEXCLIPLUS_APP_ROOT"] = scope.RootDirectory;
        process.StartInfo.Environment["CODEXCLIPLUS_APP_MODE"] = "development";
        process.StartInfo.Environment["USERPROFILE"] = scope.UserProfileDirectory;
        process.StartInfo.Environment["HOME"] = scope.HomeDirectory;
        process.StartInfo.Environment["CODEX_HOME"] = scope.CodexHomeDirectory;
        process.StartInfo.Environment["TEMP"] = scope.TempDirectory;
        process.StartInfo.Environment["TMP"] = scope.TempDirectory;

        Assert.True(process.Start(), "CodexCliPlus.exe did not start.");

        try
        {
            await SmokeEnvironmentScope.WaitForAsync(
                () =>
                    Directory.Exists(Path.Combine(scope.RootDirectory, "config"))
                    && Directory.Exists(Path.Combine(scope.RootDirectory, "logs"))
                    && Directory.Exists(Path.Combine(scope.RootDirectory, "backend"))
                    && Directory.Exists(Path.Combine(scope.RootDirectory, "cache"))
                    && Directory.Exists(Path.Combine(scope.RootDirectory, "diagnostics"))
                    && Directory.Exists(Path.Combine(scope.RootDirectory, "runtime")),
                TimeSpan.FromSeconds(12),
                "CodexCliPlus.exe did not initialize the isolated app root in time."
            );

            if (process.HasExited)
            {
                Assert.Fail($"CodexCliPlus.exe exited early with code {process.ExitCode}.");
            }

            var ownedBackendPids = scope.GetOwnedBackendProcessIds();
            Assert.All(
                ownedBackendPids,
                pid => Assert.True(pid > 0, "Owned backend PID should be a positive integer.")
            );
        }
        finally
        {
            scope.StopOwnedBackendProcesses();
            SmokeEnvironmentScope.StopExactProcess(process);
        }
    }

    [Fact]
    public async Task PathAndCredentialSmokeUsesIsolatedDirectoriesAndDpapiSecretFiles()
    {
        using var scope = new SmokeEnvironmentScope();
        var pathService = SmokeEnvironmentScope.CreatePathService();

        await pathService.EnsureCreatedAsync();

        Assert.Equal(AppDataMode.Development, pathService.Directories.DataMode);
        Assert.Equal(scope.RootDirectory, pathService.Directories.RootDirectory);
        Assert.StartsWith(
            scope.RootDirectory,
            pathService.Directories.ConfigDirectory,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.StartsWith(
            scope.RootDirectory,
            pathService.Directories.CacheDirectory,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.StartsWith(
            scope.RootDirectory,
            pathService.Directories.DiagnosticsDirectory,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.StartsWith(
            scope.RootDirectory,
            pathService.Directories.RuntimeDirectory,
            StringComparison.OrdinalIgnoreCase
        );

        var configurationService = new JsonAppConfigurationService(pathService);
        var settings = await configurationService.LoadAsync();
        Assert.False(settings.CheckForUpdatesOnStartup);

        var store = new DpapiCredentialStore(pathService);
        await store.SaveSecretAsync("smoke-management", "super-secret");

        var secretPath = Path.Combine(
            pathService.Directories.ConfigDirectory,
            AppConstants.SecretsDirectoryName,
            "smoke-management.bin"
        );
        var loaded = await store.LoadSecretAsync("smoke-management");
        var rawBytes = await File.ReadAllBytesAsync(secretPath);

        Assert.Equal("super-secret", loaded);
        Assert.StartsWith(scope.RootDirectory, secretPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(rawBytes.AsSpan().SequenceEqual("super-secret"u8));

        await store.DeleteSecretAsync("smoke-management");
        Assert.False(File.Exists(secretPath));
    }

    [Fact]
    public async Task BackendHostingSmokeRunsHealthEndpointFromIsolatedManagedBackendPath()
    {
        using var scope = new SmokeEnvironmentScope();
        var pathService = SmokeEnvironmentScope.CreatePathService();
        var logger = new FileAppLogger(pathService);
        var assetService = new BackendAssetService(new HttpClient(), pathService, logger);
        var layout = await assetService.EnsureAssetsAsync();
        var port = SmokeEnvironmentScope.FindAvailablePort();
        var configPath = scope.WriteBackendConfig(port);

        Assert.Equal(scope.GetBackendExecutablePath(), layout.ExecutablePath);
        Assert.StartsWith(
            scope.RootDirectory,
            layout.WorkingDirectory,
            StringComparison.OrdinalIgnoreCase
        );

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = layout.ExecutablePath,
                WorkingDirectory = layout.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-config");
        process.StartInfo.ArgumentList.Add(configPath);
        process.StartInfo.Environment["CODEXCLIPLUS_APP_ROOT"] = scope.RootDirectory;
        process.StartInfo.Environment["CODEXCLIPLUS_APP_MODE"] = "development";
        process.StartInfo.Environment["USERPROFILE"] = scope.UserProfileDirectory;
        process.StartInfo.Environment["HOME"] = scope.HomeDirectory;
        process.StartInfo.Environment["CODEX_HOME"] = scope.CodexHomeDirectory;
        process.StartInfo.Environment["TEMP"] = scope.TempDirectory;
        process.StartInfo.Environment["TMP"] = scope.TempDirectory;

        Assert.True(
            process.Start(),
            $"{BackendExecutableNames.ManagedExecutableFileName} did not start."
        );

        try
        {
            await SmokeEnvironmentScope.WaitForHttpOkAsync(
                $"http://127.0.0.1:{port}/healthz",
                TimeSpan.FromSeconds(20)
            );

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            httpClient.DefaultRequestHeaders.Add("X-Management-Key", "smoke-only");
            var managementBaseUrl = $"http://127.0.0.1:{port}/v0/management";

            using var configResponse = await httpClient.GetAsync($"{managementBaseUrl}/config");
            Assert.Equal(HttpStatusCode.OK, configResponse.StatusCode);
            using var configJson = JsonDocument.Parse(
                await configResponse.Content.ReadAsStringAsync()
            );
            Assert.Equal(
                60,
                configJson
                    .RootElement.GetProperty("redis-usage-queue-retention-seconds")
                    .GetInt32()
            );

            using var apiKeyUsageResponse = await httpClient.GetAsync(
                $"{managementBaseUrl}/api-key-usage"
            );
            Assert.Equal(HttpStatusCode.OK, apiKeyUsageResponse.StatusCode);
            using var apiKeyUsageJson = JsonDocument.Parse(
                await apiKeyUsageResponse.Content.ReadAsStringAsync()
            );
            Assert.Equal(JsonValueKind.Object, apiKeyUsageJson.RootElement.ValueKind);

            using var usageResponse = await httpClient.GetAsync($"{managementBaseUrl}/usage");
            Assert.Equal(HttpStatusCode.OK, usageResponse.StatusCode);
            using var usageJson = JsonDocument.Parse(await usageResponse.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Object, usageJson.RootElement.ValueKind);

            if (process.HasExited)
            {
                Assert.Fail(
                    $"{BackendExecutableNames.ManagedExecutableFileName} exited early with code {process.ExitCode}."
                );
            }
            Assert.Contains(process.Id, scope.GetOwnedBackendProcessIds());
        }
        finally
        {
            SmokeEnvironmentScope.StopExactProcess(process);
            scope.StopOwnedBackendProcesses();
        }
    }

    [Fact]
    public async Task DependencyRepairSmokeTransitionsFromHealthyStateToRepairModeAfterBackendDamage()
    {
        using var scope = new SmokeEnvironmentScope();
        var pathService = SmokeEnvironmentScope.CreatePathService();
        var logger = new FileAppLogger(pathService);
        var assetService = new BackendAssetService(new HttpClient(), pathService, logger);
        var configurationService = new JsonAppConfigurationService(pathService);
        var credentialStore = new DpapiCredentialStore(pathService);
        var layout = await assetService.EnsureAssetsAsync();
        var expectedAssetRoot = CreateExpectedBackendAssetRoot(
            pathService,
            layout.WorkingDirectory
        );
        var dependencyService = new DependencyHealthService(
            pathService,
            new DirectoryAccessService(pathService),
            credentialStore,
            new GitHubReleaseUpdateService(new ThrowingHttpClientFactory()),
            () => ".NET 10.0.0",
            () => expectedAssetRoot
        );

        var settings = await configurationService.LoadAsync();
        settings.ManagementKey = "smoke-management";
        await configurationService.SaveAsync(settings);
        await File.WriteAllTextAsync(
            pathService.Directories.BackendConfigFilePath,
            FormattableString.Invariant($"port: {AppConstants.DefaultBackendPort}"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        var healthy = await dependencyService.EvaluateAsync(new BackendStatusSnapshot());
        Assert.True(healthy.IsAvailable);
        Assert.False(healthy.RequiresRepairMode);
        Assert.Empty(healthy.Issues);

        await File.WriteAllTextAsync(
            Path.Combine(
                pathService.Directories.BackendDirectory,
                BackendExecutableNames.ManagedExecutableFileName
            ),
            "tampered",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        var repair = await dependencyService.EvaluateAsync(new BackendStatusSnapshot());

        Assert.False(repair.IsAvailable);
        Assert.True(repair.RequiresRepairMode);
        Assert.Contains(
            repair.Issues,
            issue => string.Equals(issue.Code, "backend-runtime", StringComparison.Ordinal)
        );
    }

    private static string CreateExpectedBackendAssetRoot(
        AppPathService pathService,
        string sourceDirectory
    )
    {
        var expectedAssetRoot = Path.Combine(
            pathService.Directories.CacheDirectory,
            "expected-backend-assets"
        );
        Directory.CreateDirectory(expectedAssetRoot);

        foreach (var fileName in BackendAssetFiles)
        {
            File.Copy(
                Path.Combine(sourceDirectory, fileName),
                Path.Combine(expectedAssetRoot, fileName),
                overwrite: true
            );
        }

        return expectedAssetRoot;
    }

    [Fact]
    public async Task UpdateCheckSmokeParsesStableInstallerMetadataWithoutNetworkSideEffects()
    {
        using var scope = new SmokeEnvironmentScope();
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "tag_name": "v1.2.3",
                          "html_url": "https://github.com/C4AL/CodexCliPlus/releases/tag/v1.2.3",
                          "published_at": "2026-04-23T00:00:00Z",
                          "assets": [
                            {
                              "name": "CodexCliPlus.Update.1.2.3.win-x64.zip",
                              "browser_download_url": "https://example.test/CodexCliPlus.Update.1.2.3.win-x64.zip",
                              "size": 4096,
                              "digest": "sha256:abc123"
                            },
                            {
                              "name": "CodexCliPlus.Setup.Offline.1.2.3.exe",
                              "browser_download_url": "https://example.test/CodexCliPlus.Setup.Offline.1.2.3.exe",
                              "size": 2048
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            )
        );

        var service = new GitHubReleaseUpdateService(factory);

        var result = await service.CheckAsync("1.0.0");

        Assert.True(result.IsCheckSuccessful);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.3", result.LatestVersion);
        Assert.Equal("Update available", result.Status);
        Assert.True(result.HasInstallableAsset);
        Assert.NotNull(result.InstallableAsset);
        Assert.Equal("CodexCliPlus.Update.1.2.3.win-x64.zip", result.InstallableAsset!.Name);
        Assert.Equal(2, result.Assets.Count);
    }

    [Fact]
    public async Task InstallerArtifactSmokeValidatesOnlineOfflineInstallerAndUpdateOutputs()
    {
        using var scope = new SmokeEnvironmentScope();
        var outputRoot = Path.Combine(scope.OutputDirectory, "buildtool");
        var packageRoot = Path.Combine(outputRoot, "packages");
        const string version = "9.9.9";

        Directory.CreateDirectory(packageRoot);

        await CreateInstallerSmokePackageAsync(packageRoot, "Online", version);
        await CreateInstallerSmokePackageAsync(packageRoot, "Offline", version);
        await CreateUpdateSmokePackageAsync(packageRoot, version);

        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await BuildToolApp.ExecuteAsync(
            [
                "verify-package",
                "--repo-root",
                scope.RepositoryRoot,
                "--output",
                outputRoot,
                "--version",
                version,
            ],
            output,
            error,
            new ThrowingProcessRunner()
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("package verification passed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    private static async Task CreateInstallerSmokePackageAsync(
        string packageRoot,
        string moniker,
        string version
    )
    {
        var installerName = $"CodexCliPlus.Setup.{moniker}.{version}.exe";
        var installerPath = Path.Combine(packageRoot, installerName);
        SmokeEnvironmentScope.CreatePeStub(installerPath);
        await ArtifactSignatureMetadata.WriteUnsignedAsync(
            installerPath,
            "Smoke package verification uses local unsigned artifacts.",
            CancellationToken.None
        );
        var entries = new Dictionary<string, byte[]>
        {
            ["app-package/CodexCliPlus.exe"] = Encoding.UTF8.GetBytes("codexcliplus"),
            ["app-package/assets/webui/upstream/dist/index.html"] = Encoding.UTF8.GetBytes(
                "<html></html>"
            ),
            ["app-package/assets/webui/upstream/dist/assets/app.js"] = Encoding.UTF8.GetBytes(
                "console.log('ok');"
            ),
            ["app-package/assets/webui/upstream/sync.json"] = Encoding.UTF8.GetBytes("{}"),
            ["mica-setup.json"] = Encoding.UTF8.GetBytes("{}"),
            ["micasetup.json"] = Encoding.UTF8.GetBytes("{}"),
            [$"output/{installerName}"] = SmokeEnvironmentScope.CreatePeStubBytes(),
            [
                $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.BootstrapperFileName}"
            ] = SmokeEnvironmentScope.CreatePeStubBytes(),
            ["app-package/packaging/uninstall-cleanup.json"] = Encoding.UTF8.GetBytes("{}"),
            ["app-package/packaging/dependency-precheck.json"] = Encoding.UTF8.GetBytes("{}"),
            ["app-package/packaging/update-policy.json"] = Encoding.UTF8.GetBytes("{}"),
        };

        if (moniker.Equals("Offline", StringComparison.OrdinalIgnoreCase))
        {
            entries[
                $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.StandaloneX64FileName}"
            ] = SmokeEnvironmentScope.CreatePeStubBytes();
        }

        SmokeEnvironmentScope.CreateZipWithByteEntries(
            Path.Combine(packageRoot, $"CodexCliPlus.Setup.{moniker}.{version}.win-x64.zip"),
            entries
        );
    }

    private static async Task CreateUpdateSmokePackageAsync(string packageRoot, string version)
    {
        var packagePath = Path.Combine(packageRoot, $"CodexCliPlus.Update.{version}.win-x64.zip");
        SmokeEnvironmentScope.CreateZipWithByteEntries(
            packagePath,
            new Dictionary<string, byte[]>
            {
                ["update-manifest.json"] = Encoding.UTF8.GetBytes("{}"),
                ["payload/CodexCliPlus.exe"] = SmokeEnvironmentScope.CreatePeStubBytes(),
            }
        );
        await ArtifactSignatureMetadata.WriteUnsignedAsync(
            packagePath,
            "Smoke package verification uses local unsigned artifacts.",
            CancellationToken.None
        );
    }
}
