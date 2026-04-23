using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Enums;
using CPAD.Core.Models;
using CPAD.Infrastructure.Updates;

namespace CPAD.Tests.Updates;

public sealed class UpdateInstallerServiceTests
{
    [Fact]
    public async Task DownloadInstallerAsyncDownloadsInstallerToUpdatesCacheAndValidatesDigest()
    {
        var installerBytes = Encoding.UTF8.GetBytes("installer-payload");
        using var pathService = new TestPathService();
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installerBytes)
        }));

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => Process.GetCurrentProcess());

        var result = await service.DownloadInstallerAsync(
            CreateUpdateResult("1.2.3", installerBytes.Length, ComputeDigest(installerBytes)));

        Assert.False(result.UsedCachedFile);
        Assert.True(result.DigestValidated);
        Assert.Equal(AppDataMode.Installed, result.DataMode);
        Assert.Equal(Path.Combine(pathService.Directories.CacheDirectory, "updates"), result.CacheDirectory);
        Assert.Equal(Path.Combine(result.CacheDirectory, "CPAD.Setup.1.2.3.exe"), result.InstallerPath);
        Assert.True(File.Exists(result.InstallerPath));
        Assert.Equal(installerBytes, await File.ReadAllBytesAsync(result.InstallerPath));
    }

    [Fact]
    public async Task DownloadInstallerAsyncReusesValidatedCachedInstallerWithoutNetworkRequest()
    {
        var installerBytes = Encoding.UTF8.GetBytes("cached-installer");
        using var pathService = new TestPathService();
        await pathService.EnsureCreatedAsync();

        var updatesDirectory = Path.Combine(pathService.Directories.CacheDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);
        var installerPath = Path.Combine(updatesDirectory, "CPAD.Setup.1.2.3.exe");
        await File.WriteAllBytesAsync(installerPath, installerBytes);

        var calls = 0;
        using var factory = new FixedHttpClientFactory(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(installerBytes)
            });
        });

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => Process.GetCurrentProcess());

        var result = await service.DownloadInstallerAsync(
            CreateUpdateResult("1.2.3", installerBytes.Length, ComputeDigest(installerBytes)));

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
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installerBytes)
        }));

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => Process.GetCurrentProcess());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadInstallerAsync(
            CreateUpdateResult("1.2.3", installerBytes.Length, ComputeDigest(Encoding.UTF8.GetBytes("different-installer")))));

        Assert.Contains("digest mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(pathService.Directories.CacheDirectory, "updates", "CPAD.Setup.1.2.3.exe")));
        Assert.False(File.Exists(Path.Combine(pathService.Directories.CacheDirectory, "updates", "CPAD.Setup.1.2.3.exe.download")));
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
            _ => throw new InvalidOperationException("Process should not be started."));

        var updateResult = CreateUpdateResult(
            version: "1.2.3",
            size: 0,
            digest: null,
            channel: UpdateChannel.Beta,
            isChannelReserved: true,
            isUpdateAvailable: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult));

        Assert.Contains("Beta is reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.CanPrepareInstaller(updateResult));
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(pathService.Directories.RootDirectory));
    }

    [Fact]
    public async Task DownloadInstallerAsyncRejectsPortableModeBeforeNetworkOrDiskUse()
    {
        using var pathService = new TestPathService(AppDataMode.Portable);
        var calls = 0;
        using var factory = new FixedHttpClientFactory(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => throw new InvalidOperationException("Process should not be started."));
        var updateResult = CreateUpdateResult("1.2.3", 0, null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult));

        Assert.Contains("Portable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.CanPrepareInstaller(updateResult));
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(pathService.Directories.RootDirectory));
    }

    [Fact]
    public async Task DownloadInstallerAsyncRejectsStableResultWhenNoNewerVersionIsAvailable()
    {
        using var pathService = new TestPathService();
        using var factory = new FixedHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new UpdateInstallerService(
            pathService,
            factory,
            _ => throw new InvalidOperationException("Process should not be started."));
        var updateResult = CreateUpdateResult(
            version: "1.2.3",
            size: 0,
            digest: null,
            isUpdateAvailable: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult));

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
            _ => throw new InvalidOperationException("Process should not be started."));
        var updateResult = CreateUpdateResult(
            version: "1.2.3",
            size: 0,
            digest: null,
            downloadUrl: string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadInstallerAsync(updateResult));

        Assert.Contains("installable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.CanPrepareInstaller(updateResult));
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(pathService.Directories.RootDirectory));
    }

    [Fact]
    public async Task LaunchInstallerAsyncRejectsPortablePreparedInstallerWithoutStartingProcess()
    {
        using var pathService = new TestPathService(AppDataMode.Portable);
        var processStartCalls = 0;
        var service = new UpdateInstallerService(
            pathService,
            new FixedHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used during launch.")),
            _ =>
            {
                processStartCalls++;
                return Process.GetCurrentProcess();
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LaunchInstallerAsync(new PreparedUpdateInstaller
            {
                DataMode = AppDataMode.Portable,
                InstallerPath = Path.Combine(pathService.Directories.RootDirectory, "CPAD.Setup.1.2.3.exe"),
                CacheDirectory = pathService.Directories.CacheDirectory,
                Version = "1.2.3",
                Asset = new UpdateReleaseAsset
                {
                    Name = "CPAD.Setup.1.2.3.exe",
                    DownloadUrl = "https://example.test/CPAD.Setup.1.2.3.exe"
                }
            }));

        Assert.Contains("Portable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, processStartCalls);
    }

    [Fact]
    public async Task LaunchInstallerAsyncUsesShellExecuteForPreparedInstaller()
    {
        using var pathService = new TestPathService();
        await pathService.EnsureCreatedAsync();

        var updatesDirectory = Path.Combine(pathService.Directories.CacheDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);
        var installerPath = Path.Combine(updatesDirectory, "CPAD.Setup.1.2.3.exe");
        await File.WriteAllBytesAsync(installerPath, [1, 2, 3, 4]);

        ProcessStartInfo? capturedStartInfo = null;
        var service = new UpdateInstallerService(
            pathService,
            new FixedHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used during launch.")),
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return Process.GetCurrentProcess();
            });

        await service.LaunchInstallerAsync(new PreparedUpdateInstaller
        {
            InstallerPath = installerPath,
            CacheDirectory = updatesDirectory,
            Version = "1.2.3",
            DataMode = AppDataMode.Installed,
            Asset = new UpdateReleaseAsset
            {
                Name = "CPAD.Setup.1.2.3.exe",
                DownloadUrl = "https://example.test/CPAD.Setup.1.2.3.exe"
            }
        });

        Assert.NotNull(capturedStartInfo);
        Assert.True(capturedStartInfo!.UseShellExecute);
        Assert.Equal(installerPath, capturedStartInfo.FileName);
        Assert.Equal(updatesDirectory, capturedStartInfo.WorkingDirectory);
    }

    private static UpdateCheckResult CreateUpdateResult(
        string version,
        long size,
        string? digest,
        UpdateChannel channel = UpdateChannel.Stable,
        bool isChannelReserved = false,
        bool isUpdateAvailable = true,
        string? downloadUrl = null)
    {
        var asset = new UpdateReleaseAsset
        {
            Name = $"CPAD.Setup.{version}.exe",
            DownloadUrl = downloadUrl ?? $"https://example.test/CPAD.Setup.{version}.exe",
            Size = size,
            Digest = digest
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
            Assets = [asset]
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
                $"cpad-update-installer-{Guid.NewGuid():N}");

            Directories = new AppDirectories(
                dataMode,
                rootDirectory,
                Path.Combine(rootDirectory, "logs"),
                Path.Combine(rootDirectory, "config"),
                Path.Combine(rootDirectory, "backend"),
                Path.Combine(rootDirectory, "cache"),
                Path.Combine(rootDirectory, "diagnostics"),
                Path.Combine(rootDirectory, "runtime"),
                Path.Combine(rootDirectory, "config", "desktop.json"),
                Path.Combine(rootDirectory, "config", "cliproxyapi.yaml"));
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
            catch
            {
            }
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
                CancellationToken cancellationToken)
            {
                return _handler(request);
            }
        }
    }
}
