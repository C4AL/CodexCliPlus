using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Updates;

namespace CodexCliPlus.Tests.Updates;

public sealed class UpdateInstallerServiceTests
{
    [Fact]
    public async Task DownloadInstallerAsyncDownloadsUpdatePackageToUpdatesCacheAndValidatesDigest()
    {
        var installerBytes = Encoding.UTF8.GetBytes("installer-payload");
        using var pathService = new TestPathService();
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(installerBytes),
                }
            )
        );

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => Process.GetCurrentProcess()
        );

        var result = await service.DownloadInstallerAsync(
            CreateUpdateResult("1.2.3", installerBytes.Length, ComputeDigest(installerBytes))
        );

        Assert.False(result.UsedCachedFile);
        Assert.True(result.DigestValidated);
        Assert.Equal(AppDataMode.Installed, result.DataMode);
        Assert.Equal(
            Path.Combine(pathService.Directories.CacheDirectory, "updates"),
            result.CacheDirectory
        );
        Assert.Equal(
            Path.Combine(result.CacheDirectory, "CodexCliPlus.Update.1.2.3.win-x64.zip"),
            result.InstallerPath
        );
        Assert.True(File.Exists(result.InstallerPath));
        Assert.Equal(installerBytes, await File.ReadAllBytesAsync(result.InstallerPath));
    }

    [Fact]
    public async Task DownloadInstallerAsyncReusesValidatedCachedUpdatePackageWithoutNetworkRequest()
    {
        var installerBytes = Encoding.UTF8.GetBytes("cached-installer");
        using var pathService = new TestPathService();
        await pathService.EnsureCreatedAsync();

        var updatesDirectory = Path.Combine(pathService.Directories.CacheDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);
        var installerPath = Path.Combine(updatesDirectory, "CodexCliPlus.Update.1.2.3.win-x64.zip");
        await File.WriteAllBytesAsync(installerPath, installerBytes);

        var calls = 0;
        using var factory = new FixedHttpClientFactory(_ =>
        {
            calls++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(installerBytes),
                }
            );
        });

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => Process.GetCurrentProcess()
        );

        var result = await service.DownloadInstallerAsync(
            CreateUpdateResult("1.2.3", installerBytes.Length, ComputeDigest(installerBytes))
        );

        Assert.True(result.UsedCachedFile);
        Assert.True(result.DigestValidated);
        Assert.Equal(0, calls);
        Assert.Equal(installerPath, result.InstallerPath);
    }

    [Fact]
    public async Task DownloadInstallerAsyncThrowsWhenDownloadedDigestDoesNotMatch()
    {
        var installerBytes = Encoding.UTF8.GetBytes("downloaded-installer");
        using var pathService = new TestPathService();
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(installerBytes),
                }
            )
        );

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => Process.GetCurrentProcess()
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(
                CreateUpdateResult(
                    "1.2.3",
                    installerBytes.Length,
                    ComputeDigest(Encoding.UTF8.GetBytes("different-installer"))
                )
            )
        );

        Assert.Contains("digest mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            File.Exists(
                Path.Combine(
                    pathService.Directories.CacheDirectory,
                    "updates",
                    "CodexCliPlus.Update.1.2.3.win-x64.zip"
                )
            )
        );
        Assert.False(
            File.Exists(
                Path.Combine(
                    pathService.Directories.CacheDirectory,
                    "updates",
                    "CodexCliPlus.Update.1.2.3.win-x64.zip.download"
                )
            )
        );
    }

    [Fact]
    public async Task DownloadInstallerAsyncRejectsBetaReservedResultBeforeNetworkOrDiskUse()
    {
        using var pathService = new TestPathService();
        var calls = 0;
        using var factory = new FixedHttpClientFactory(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => throw new InvalidOperationException("Process should not be started.")
        );

        var updateResult = CreateUpdateResult(
            version: "1.2.3",
            size: 0,
            digest: null,
            channel: UpdateChannel.Beta,
            isChannelReserved: true,
            isUpdateAvailable: false
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult)
        );

        Assert.Contains("Beta is reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.CanPrepareInstaller(updateResult));
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(pathService.Directories.RootDirectory));
    }

    [Fact]
    public async Task DownloadInstallerAsyncRejectsNonInstalledModeBeforeNetworkOrDiskUse()
    {
        using var pathService = new TestPathService(AppDataMode.Development);
        var calls = 0;
        using var factory = new FixedHttpClientFactory(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => throw new InvalidOperationException("Process should not be started.")
        );
        var updateResult = CreateUpdateResult("1.2.3", 0, null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult)
        );

        Assert.Contains("Installed mode", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.CanPrepareInstaller(updateResult));
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(pathService.Directories.RootDirectory));
    }

    [Fact]
    public async Task DownloadInstallerAsyncRejectsStableResultWhenNoNewerVersionIsAvailable()
    {
        using var pathService = new TestPathService();
        using var factory = new FixedHttpClientFactory(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
        );
        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => throw new InvalidOperationException("Process should not be started.")
        );
        var updateResult = CreateUpdateResult(
            version: "1.2.3",
            size: 0,
            digest: null,
            isUpdateAvailable: false
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult)
        );

        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.CanPrepareInstaller(updateResult));
        Assert.False(Directory.Exists(pathService.Directories.RootDirectory));
    }

    [Fact]
    public async Task DownloadInstallerAsyncRejectsInstallableAssetWithoutDownloadUrlBeforeNetworkOrDiskUse()
    {
        using var pathService = new TestPathService();
        var calls = 0;
        using var factory = new FixedHttpClientFactory(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => throw new InvalidOperationException("Process should not be started.")
        );
        var updateResult = CreateUpdateResult(
            version: "1.2.3",
            size: 0,
            digest: null,
            downloadUrl: string.Empty
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult)
        );

        Assert.Contains("installable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.CanPrepareInstaller(updateResult));
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(pathService.Directories.RootDirectory));
    }

    [Fact]
    public async Task LaunchInstallerAsyncRejectsNonInstalledPreparedInstallerWithoutStartingProcess()
    {
        using var pathService = new TestPathService(AppDataMode.Development);
        var processStartCalls = 0;
        var service = new UpdateInstallerService(
            pathService,
            new FixedHttpClientFactory(_ =>
                throw new InvalidOperationException("HTTP should not be used during launch.")
            ),
            _ =>
            {
                processStartCalls++;
                return Process.GetCurrentProcess();
            }
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LaunchInstallerAsync(
                new PreparedUpdateInstaller
                {
                    DataMode = AppDataMode.Development,
                    InstallerPath = Path.Combine(
                        pathService.Directories.RootDirectory,
                        "CodexCliPlus.Update.1.2.3.win-x64.zip"
                    ),
                    CacheDirectory = pathService.Directories.CacheDirectory,
                    Version = "1.2.3",
                    Asset = new UpdateReleaseAsset
                    {
                        Name = "CodexCliPlus.Update.1.2.3.win-x64.zip",
                        DownloadUrl = "https://example.test/CodexCliPlus.Update.1.2.3.win-x64.zip",
                    },
                }
            )
        );

        Assert.Contains("Installed mode", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, processStartCalls);
    }

    [Fact]
    public async Task LaunchInstallerAsyncStartsBundledUpdaterForPreparedPackage()
    {
        using var pathService = new TestPathService();
        await pathService.EnsureCreatedAsync();
        var appDirectory = Path.Combine(pathService.Directories.RootDirectory, "app");
        var updaterDirectory = Path.Combine(appDirectory, "updater");
        Directory.CreateDirectory(updaterDirectory);
        var updaterPath = Path.Combine(updaterDirectory, "CodexCliPlus.Updater.exe");
        var appPath = Path.Combine(appDirectory, "CodexCliPlus.exe");
        await File.WriteAllBytesAsync(updaterPath, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(appPath, [1, 2, 3, 4]);

        var updatesDirectory = Path.Combine(pathService.Directories.CacheDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);
        var packageBytes = Encoding.UTF8.GetBytes("prepared-update-package");
        var installerPath = Path.Combine(updatesDirectory, "CodexCliPlus.Update.1.2.3.win-x64.zip");
        await File.WriteAllBytesAsync(installerPath, packageBytes);

        ProcessStartInfo? capturedStartInfo = null;
        var service = new UpdateInstallerService(
            pathService,
            new FixedHttpClientFactory(_ =>
                throw new InvalidOperationException("HTTP should not be used during launch.")
            ),
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return Process.GetCurrentProcess();
            },
            () => appDirectory
        );

        await service.LaunchInstallerAsync(
            new PreparedUpdateInstaller
            {
                InstallerPath = installerPath,
                CacheDirectory = updatesDirectory,
                Version = "1.2.3",
                DataMode = AppDataMode.Installed,
                Asset = new UpdateReleaseAsset
                {
                    Name = "CodexCliPlus.Update.1.2.3.win-x64.zip",
                    DownloadUrl = "https://example.test/CodexCliPlus.Update.1.2.3.win-x64.zip",
                    Digest = ComputeDigest(packageBytes),
                },
            }
        );

        Assert.NotNull(capturedStartInfo);
        Assert.False(capturedStartInfo!.UseShellExecute);
        Assert.Equal(updaterPath, capturedStartInfo.FileName);
        Assert.Equal(updaterDirectory, capturedStartInfo.WorkingDirectory);
        var arguments = capturedStartInfo.ArgumentList.ToArray();
        Assert.Contains("--pid", arguments);
        Assert.Contains("--app-dir", arguments);
        Assert.Contains(appDirectory, arguments);
        Assert.Contains("--package", arguments);
        Assert.Contains(installerPath, arguments);
        Assert.Contains("--sha256", arguments);
        Assert.Contains(ComputeDigest(packageBytes)["sha256:".Length..], arguments);
        Assert.Contains("--restart", arguments);
        Assert.Contains(appPath, arguments);
    }

    private static UpdateCheckResult CreateUpdateResult(
        string version,
        long size,
        string? digest,
        UpdateChannel channel = UpdateChannel.Stable,
        bool isChannelReserved = false,
        bool isUpdateAvailable = true,
        string? downloadUrl = null
    )
    {
        var asset = new UpdateReleaseAsset
        {
            Name = $"CodexCliPlus.Update.{version}.win-x64.zip",
            DownloadUrl =
                downloadUrl ?? $"https://example.test/CodexCliPlus.Update.{version}.win-x64.zip",
            Size = size,
            Digest = digest,
        };

        return new UpdateCheckResult
        {
            Channel = channel,
            LatestVersion = version,
            IsCheckSuccessful = true,
            IsChannelReserved = isChannelReserved,
            IsUpdateAvailable = isUpdateAvailable,
            HasInstallableAsset = true,
            InstallableAsset = asset,
            Assets = [asset],
        };
    }

    private static string ComputeDigest(byte[] bytes)
    {
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    private sealed class TestPathService : IPathService, IDisposable
    {
        public TestPathService(AppDataMode dataMode = AppDataMode.Installed)
        {
            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                $"codexcliplus-update-installer-{Guid.NewGuid():N}"
            );

            Directories = new AppDirectories(
                dataMode,
                rootDirectory,
                Path.Combine(rootDirectory, "logs"),
                Path.Combine(rootDirectory, "config"),
                Path.Combine(rootDirectory, "backend"),
                Path.Combine(rootDirectory, "cache"),
                Path.Combine(rootDirectory, "diagnostics"),
                Path.Combine(rootDirectory, "runtime"),
                Path.Combine(rootDirectory, "config", "appsettings.json"),
                Path.Combine(rootDirectory, "config", "backend.yaml")
            );
        }

        public AppDirectories Directories { get; }

        public Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Directories.RootDirectory);
            Directory.CreateDirectory(Directories.LogsDirectory);
            Directory.CreateDirectory(Directories.ConfigDirectory);
            Directory.CreateDirectory(Directories.BackendDirectory);
            Directory.CreateDirectory(Directories.CacheDirectory);
            Directory.CreateDirectory(Directories.DiagnosticsDirectory);
            Directory.CreateDirectory(Directories.RuntimeDirectory);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Directories.RootDirectory))
                {
                    Directory.Delete(Directories.RootDirectory, recursive: true);
                }
            }
            catch { }
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient _client;

        public FixedHttpClientFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _client = new HttpClient(new DelegatingHandler(handler));
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        private sealed class DelegatingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

            public DelegatingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return _handler(request);
            }
        }
    }
}
