using System.Diagnostics;
using CodexCliPlus.OfflineBuilder;

namespace CodexCliPlus.Tests.OfflineBuilder;

[Collection("ProcessEnvironment")]
[Trait("Category", "Packaging")]
public sealed class OfflineBuilderTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-offline-builder-{Guid.NewGuid():N}"
    );

    [Fact]
    public void OptionsUseExpectedDefaultsAndParsePassThroughValues()
    {
        var repositoryRoot = CreateRepositoryRoot();

        var defaultParsed = OfflineBuilderOptions.TryParse(
            [],
            repositoryRoot,
            out var defaults,
            out var defaultError
        );
        var customParsed = OfflineBuilderOptions.TryParse(
            [
                "--version",
                "2.3.4",
                "--runtime",
                "win-x64",
                "--output",
                "custom-out",
                "--desktop",
                "desktop-out",
                "--force-rebuild",
                "installer",
            ],
            repositoryRoot,
            out var custom,
            out var customError
        );

        Assert.True(defaultParsed, defaultError);
        Assert.Equal("1.0.0", defaults.Version);
        Assert.Equal("win-x64", defaults.Runtime);
        Assert.Equal(Path.Combine(repositoryRoot, "artifacts", "buildtool"), defaults.OutputRoot);
        Assert.Equal(OfflineForceRebuild.None, defaults.ForceRebuild);

        Assert.True(customParsed, customError);
        Assert.Equal("2.3.4", custom.Version);
        Assert.Equal("win-x64", custom.Runtime);
        Assert.Equal(Path.Combine(repositoryRoot, "custom-out"), custom.OutputRoot);
        Assert.Equal(Path.Combine(repositoryRoot, "desktop-out"), custom.DesktopRoot);
        Assert.Equal(OfflineForceRebuild.Installer, custom.ForceRebuild);
        Assert.Equal("installer", OfflineBuilderOptions.ToBuildToolValue(custom.ForceRebuild));
    }

    [Fact]
    public async Task ToolchainResolverReusesUsableToolCache()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var options = CreateOptions(repositoryRoot);
        CreateCachedTools(repositoryRoot);
        var runner = new VersionProcessRunner();
        var downloader = new RecordingDownloader();
        var extractor = new RecordingExtractor();
        var resolver = new PortableToolchainResolver(runner, downloader, extractor);

        var resolution = await resolver.EnsureAsync(options);

        Assert.Empty(downloader.Downloads);
        Assert.Empty(extractor.Extractions);
        Assert.Empty(resolution.DownloadedTools);
        Assert.Contains(
            Path.Combine(repositoryRoot, "artifacts", "toolcache", "dotnet", "10.0.203"),
            resolution.Environment["DOTNET_ROOT"],
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            Path.Combine(repositoryRoot, "artifacts", "toolcache", "go", "1.26.2"),
            resolution.Environment["GOROOT"],
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Equal("local", resolution.Environment["GOTOOLCHAIN"]);
        Assert.Equal(
            Path.Combine(options.OutputRoot, "cache", "npm"),
            resolution.Environment["NPM_CONFIG_CACHE"]
        );
    }

    [Fact]
    public async Task ToolchainResolverDownloadsMissingToolsIntoLocalCache()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var options = CreateOptions(repositoryRoot);
        var runner = new VersionProcessRunner();
        var downloader = new RecordingDownloader();
        var extractor = new RecordingExtractor();
        var resolver = new PortableToolchainResolver(runner, downloader, extractor);
        var oldPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", string.Empty);

            var resolution = await resolver.EnsureAsync(options);

            Assert.Equal(3, downloader.Downloads.Count);
            Assert.Contains(
                downloader.Downloads,
                uri => uri.Contains("dotnet-sdk-10.0.203-win-x64.zip", StringComparison.Ordinal)
            );
            Assert.Contains(
                downloader.Downloads,
                uri => uri.Contains("node-v24.14.1-win-x64.zip", StringComparison.Ordinal)
            );
            Assert.Contains(
                downloader.Downloads,
                uri => uri.Contains("go1.26.2.windows-amd64.zip", StringComparison.Ordinal)
            );
            Assert.Contains(".NET SDK", resolution.DownloadedTools);
            Assert.Contains("Node.js", resolution.DownloadedTools);
            Assert.Contains("Go", resolution.DownloadedTools);
            Assert.True(File.Exists(resolution.DotNetExecutable));
            Assert.True(File.Exists(resolution.NodeExecutable));
            Assert.True(File.Exists(resolution.GoExecutable));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public async Task ToolchainResolverPropagatesCancellationFromVersionProbe()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var options = CreateOptions(repositoryRoot);
        CreateCachedTools(repositoryRoot);
        using var cancellation = new CancellationTokenSource();
        var runner = new ProbeCancelingProcessRunner(cancellation, _ => true);
        var downloader = new RecordingDownloader();
        var resolver = new PortableToolchainResolver(
            runner,
            downloader,
            new RecordingExtractor()
        );
        var oldPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", string.Empty);

            var exception = await Record.ExceptionAsync(() =>
                resolver.EnsureAsync(options, cancellation.Token)
            );

            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.Empty(downloader.Downloads);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public async Task ToolchainResolverPropagatesCancellationFromGoRootProbe()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var options = CreateOptions(repositoryRoot);
        CreateCachedTools(repositoryRoot);
        Directory.Delete(
            Path.Combine(repositoryRoot, "artifacts", "toolcache", "go"),
            recursive: true
        );
        var systemToolDirectory = Path.Combine(_rootDirectory, "system-tools");
        CreateExecutable(Path.Combine(systemToolDirectory, "go.exe"));
        using var cancellation = new CancellationTokenSource();
        var runner = new ProbeCancelingProcessRunner(
            cancellation,
            arguments => arguments.SequenceEqual(["env", "GOROOT"])
        );
        var downloader = new RecordingDownloader();
        var resolver = new PortableToolchainResolver(
            runner,
            downloader,
            new RecordingExtractor()
        );
        var oldPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            Environment.SetEnvironmentVariable("PATH", systemToolDirectory);

            var exception = await Record.ExceptionAsync(() =>
                resolver.EnsureAsync(options, cancellation.Token)
            );

            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.Empty(downloader.Downloads);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public async Task BuildServiceRestoresCleansBuildsAndMovesInstallerToDesktop()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        var desktopRoot = Path.Combine(_rootDirectory, "desktop");
        Directory.CreateDirectory(desktopRoot);
        var existingDesktopInstaller = Path.Combine(
            desktopRoot,
            "CodexCliPlus.Setup.Offline.9.9.9.exe"
        );
        File.WriteAllText(existingDesktopInstaller, "old");

        var options = new OfflineBuilderOptions(
            repositoryRoot,
            "9.9.9",
            "win-x64",
            outputRoot,
            desktopRoot,
            OfflineForceRebuild.Installer
        );
        var runner = new BuildProcessRunner(options);
        var service = new OfflinePackageBuildService(
            runner,
            new FixedToolchainResolver(outputRoot)
        );

        var desktopInstaller = await service.BuildAsync(options);

        Assert.Equal(existingDesktopInstaller, desktopInstaller);
        Assert.Null(WindowsExecutableValidator.ValidateFile(desktopInstaller));
        Assert.False(
            File.Exists(
                Path.Combine(outputRoot, "packages", "CodexCliPlus.Setup.Offline.9.9.9.exe")
            )
        );
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(["restore", "CodexCliPlus.sln", "--locked-mode"], runner.Calls[0].Arguments);
        Assert.Contains("clean-artifacts", runner.Calls[1].Arguments);
        Assert.Contains("build-release", runner.Calls[2].Arguments);
        Assert.Contains("--packages", runner.Calls[2].Arguments);
        Assert.Contains("offline", runner.Calls[2].Arguments);
        Assert.Contains("--compression", runner.Calls[2].Arguments);
        Assert.Contains("optimal", runner.Calls[2].Arguments);
        Assert.Contains("--force-rebuild", runner.Calls[2].Arguments);
        Assert.Contains("installer", runner.Calls[2].Arguments);
    }

    [Fact]
    public async Task BuildServiceReportsChineseErrorWhenInstallerIsMissing()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var options = CreateOptions(repositoryRoot);
        var service = new OfflinePackageBuildService(
            new BuildProcessRunner(options, createInstaller: false),
            new FixedToolchainResolver(options.OutputRoot)
        );

        var exception = await Assert.ThrowsAsync<OfflineBuilderException>(() =>
            service.BuildAsync(options)
        );

        Assert.Contains("离线安装包校验失败", exception.Message, StringComparison.Ordinal);
        Assert.Contains("文件不存在", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildServiceReportsChineseErrorWhenInstallerValidationFails()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var options = CreateOptions(repositoryRoot);
        var service = new OfflinePackageBuildService(
            new BuildProcessRunner(options, invalidInstaller: true),
            new FixedToolchainResolver(options.OutputRoot)
        );

        var exception = await Assert.ThrowsAsync<OfflineBuilderException>(() =>
            service.BuildAsync(options)
        );

        Assert.Contains("离线安装包校验失败", exception.Message, StringComparison.Ordinal);
        Assert.Contains("文件过小", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessRunnerPropagatesPreCanceledTokenBeforeStartingProcess()
    {
        Directory.CreateDirectory(_rootDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new OfflineBuilderProcessRunner();

        var exception = await Record.ExceptionAsync(() =>
            runner.RunAsync(
                "codexcliplus-missing-process.exe",
                ["--version"],
                _rootDirectory,
                cancellationToken: cancellation.Token
            )
        );

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task ProcessRunnerKillsStartedProcessWhenCancelled()
    {
        Directory.CreateDirectory(_rootDirectory);
        var pidPath = Path.Combine(
            _rootDirectory,
            $"offline-builder-process-{Guid.NewGuid():N}.pid"
        );
        var escapedPidPath = pidPath.Replace("'", "''", StringComparison.Ordinal);
        int? processId = null;
        using var cancellation = new CancellationTokenSource();
        var runner = new OfflineBuilderProcessRunner();

        try
        {
            var runTask = runner.RunAsync(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    $"Set-Content -LiteralPath '{escapedPidPath}' -Value $PID -Encoding ascii; Start-Sleep -Seconds 60",
                ],
                _rootDirectory,
                cancellationToken: cancellation.Token
            );
            processId = await WaitForPidAsync(pidPath);

            cancellation.Cancel();

            var exception = await Record.ExceptionAsync(async () => await runTask);

            Assert.IsAssignableFrom<OperationCanceledException>(exception);
            Assert.False(await ProcessExistsAsync(processId.Value));
        }
        finally
        {
            if (processId is { } pid)
            {
                StopProcessIfRunning(pid);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private OfflineBuilderOptions CreateOptions(string repositoryRoot)
    {
        return new OfflineBuilderOptions(
            repositoryRoot,
            "9.9.9",
            "win-x64",
            Path.Combine(_rootDirectory, "out"),
            Path.Combine(_rootDirectory, "desktop"),
            OfflineForceRebuild.None
        );
    }

    private string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryRoot);
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "src", "CodexCliPlus.BuildTool"));
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);
        File.WriteAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "CodexCliPlus.BuildTool",
                "CodexCliPlus.BuildTool.csproj"
            ),
            "<Project />"
        );
        File.WriteAllText(
            Path.Combine(repositoryRoot, "global.json"),
            """
            {
              "sdk": {
                "version": "10.0.203"
              }
            }
            """
        );
        File.WriteAllText(
            Path.Combine(repositoryRoot, ".mise.toml"),
            """
            [tools]
            dotnet = "10.0.203"
            node = "24.14.1"
            go = "1.26.2"
            """
        );
        return repositoryRoot;
    }

    private static void CreateCachedTools(string repositoryRoot)
    {
        CreateExecutable(
            Path.Combine(
                repositoryRoot,
                "artifacts",
                "toolcache",
                "dotnet",
                "10.0.203",
                "win-x64",
                "dotnet.exe"
            )
        );
        CreateExecutable(
            Path.Combine(
                repositoryRoot,
                "artifacts",
                "toolcache",
                "node",
                "24.14.1",
                "win-x64",
                "node.exe"
            )
        );
        CreateExecutable(
            Path.Combine(
                repositoryRoot,
                "artifacts",
                "toolcache",
                "go",
                "1.26.2",
                "windows-amd64",
                "go",
                "bin",
                "go.exe"
            )
        );
    }

    private static void CreateExecutable(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, CreateExecutableBytes());
    }

    private static async Task<int> WaitForPidAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var text = await TryReadSharedTextAsync(path);
                if (int.TryParse(text?.Trim(), out var processId))
                {
                    return processId;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Process PID file was not created.");
    }

    private static async Task<string?> TryReadSharedTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<bool> ProcessExistsAsync(int processId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }

            await Task.Delay(100);
        }

        return true;
    }

    private static void StopProcessIfRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch { }
    }

    private static byte[] CreateExecutableBytes()
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private sealed record ProcessCall(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        IReadOnlyDictionary<string, string?> Environment
    );

    private sealed class VersionProcessRunner : IOfflineBuilderProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            if (arguments.SequenceEqual(["env", "GOROOT"]))
            {
                var binDirectory = Path.GetDirectoryName(fileName)!;
                return Task.FromResult(
                    new ProcessRunResult(
                        0,
                        Directory.GetParent(binDirectory)!.FullName,
                        string.Empty
                    )
                );
            }

            var name = Path.GetFileNameWithoutExtension(fileName);
            var output = name.ToLowerInvariant() switch
            {
                "dotnet" => "10.0.203",
                "node" => "v24.14.1",
                "go" => "go version go1.26.2 windows/amd64",
                _ => string.Empty,
            };
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty));
        }
    }

    private sealed class ProbeCancelingProcessRunner : IOfflineBuilderProcessRunner
    {
        private readonly CancellationTokenSource cancellation;
        private readonly Func<IReadOnlyList<string>, bool> shouldCancel;

        public ProbeCancelingProcessRunner(
            CancellationTokenSource cancellation,
            Func<IReadOnlyList<string>, bool> shouldCancel
        )
        {
            this.cancellation = cancellation;
            this.shouldCancel = shouldCancel;
        }

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            if (shouldCancel(arguments))
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var name = Path.GetFileNameWithoutExtension(fileName);
            var output = name.ToLowerInvariant() switch
            {
                "dotnet" => "10.0.203",
                "node" => "v24.14.1",
                "go" => "go version go1.26.2 windows/amd64",
                _ => string.Empty,
            };
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty));
        }
    }

    private sealed class BuildProcessRunner : IOfflineBuilderProcessRunner
    {
        private readonly OfflineBuilderOptions options;
        private readonly bool createInstaller;
        private readonly bool invalidInstaller;

        public BuildProcessRunner(
            OfflineBuilderOptions options,
            bool createInstaller = true,
            bool invalidInstaller = false
        )
        {
            this.options = options;
            this.createInstaller = createInstaller;
            this.invalidInstaller = invalidInstaller;
        }

        public List<ProcessCall> Calls { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(
                new ProcessCall(
                    fileName,
                    arguments.ToArray(),
                    workingDirectory,
                    environment?.ToDictionary() ?? new Dictionary<string, string?>()
                )
            );

            if (arguments.Contains("build-release") && createInstaller)
            {
                var installerPath = Path.Combine(
                    options.OutputRoot,
                    "packages",
                    $"CodexCliPlus.Setup.Offline.{options.Version}.exe"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);
                File.WriteAllBytes(
                    installerPath,
                    invalidInstaller ? [1, 2, 3] : CreateExecutableBytes()
                );
            }

            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class FixedToolchainResolver : IPortableToolchainResolver
    {
        private readonly string outputRoot;

        public FixedToolchainResolver(string outputRoot)
        {
            this.outputRoot = outputRoot;
        }

        public Task<ToolchainResolution> EnsureAsync(
            OfflineBuilderOptions options,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                new ToolchainResolution(
                    "dotnet.exe",
                    "node.exe",
                    "go.exe",
                    new Dictionary<string, string?>
                    {
                        ["PATH"] = "tools",
                        ["DOTNET_ROOT"] = "dotnet",
                        ["GOROOT"] = "go",
                        ["GOTOOLCHAIN"] = "local",
                        ["NPM_CONFIG_CACHE"] = Path.Combine(outputRoot, "cache", "npm"),
                    },
                    []
                )
            );
        }
    }

    private sealed class RecordingDownloader : IToolArchiveDownloader
    {
        public List<string> Downloads { get; } = [];

        public Task DownloadAsync(
            Uri sourceUri,
            string targetPath,
            CancellationToken cancellationToken
        )
        {
            Downloads.Add(sourceUri.ToString());
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, [1]);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingExtractor : IToolArchiveExtractor
    {
        public List<string> Extractions { get; } = [];

        public void ExtractToDirectory(string archivePath, string destinationDirectory)
        {
            Extractions.Add(archivePath);
            var fileName = Path.GetFileName(archivePath);
            if (fileName.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase))
            {
                CreateExecutable(Path.Combine(destinationDirectory, "dotnet.exe"));
                return;
            }

            if (fileName.StartsWith("node-", StringComparison.OrdinalIgnoreCase))
            {
                CreateExecutable(
                    Path.Combine(destinationDirectory, "node-v24.14.1-win-x64", "node.exe")
                );
                return;
            }

            if (fileName.StartsWith("go-", StringComparison.OrdinalIgnoreCase))
            {
                CreateExecutable(Path.Combine(destinationDirectory, "go", "bin", "go.exe"));
            }
        }
    }
}

[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollectionScope;
