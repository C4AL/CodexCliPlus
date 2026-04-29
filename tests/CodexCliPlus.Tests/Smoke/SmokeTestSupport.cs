using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using CodexCliPlus.BuildTool;
using CodexCliPlus.Core.Abstractions.Processes;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Infrastructure.Paths;

namespace CodexCliPlus.Tests.Smoke;

internal sealed class SmokeEnvironmentScope : IDisposable
{
    private static readonly string[] EnvironmentKeys =
    [
        "CODEXCLIPLUS_APP_ROOT",
        "CODEXCLIPLUS_APP_MODE",
        "USERPROFILE",
        "HOME",
        "CODEX_HOME",
        "TEMP",
        "TMP",
    ];

    private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

    public SmokeEnvironmentScope(string mode = "development")
    {
        RepositoryRoot = FindRepositoryRoot();
        RootDirectory = Path.Combine(Path.GetTempPath(), $"codexcliplus-smoke-{Guid.NewGuid():N}");
        UserProfileDirectory = Path.Combine(RootDirectory, "userprofile");
        HomeDirectory = UserProfileDirectory;
        CodexHomeDirectory = Path.Combine(RootDirectory, "codex-home");
        TempDirectory = Path.Combine(RootDirectory, "tmp");
        OutputDirectory = Path.Combine(RootDirectory, "output");

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(UserProfileDirectory);
        Directory.CreateDirectory(Path.Combine(UserProfileDirectory, "Desktop"));
        Directory.CreateDirectory(CodexHomeDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(OutputDirectory);
        Directory.CreateDirectory(Path.Combine(UserProfileDirectory, ".cli-proxy-api"));

        foreach (var key in EnvironmentKeys)
        {
            _originalValues[key] = Environment.GetEnvironmentVariable(key);
        }

        SetEnvironment("CODEXCLIPLUS_APP_ROOT", RootDirectory);
        SetEnvironment("CODEXCLIPLUS_APP_MODE", mode);
        SetEnvironment("USERPROFILE", UserProfileDirectory);
        SetEnvironment("HOME", HomeDirectory);
        SetEnvironment("CODEX_HOME", CodexHomeDirectory);
        SetEnvironment("TEMP", TempDirectory);
        SetEnvironment("TMP", TempDirectory);
    }

    public string RepositoryRoot { get; }

    public string RootDirectory { get; }

    public string UserProfileDirectory { get; }

    public string HomeDirectory { get; }

    public string CodexHomeDirectory { get; }

    public string TempDirectory { get; }

    public string OutputDirectory { get; }

    public static string ApplicationPath =>
        Path.Combine(AppContext.BaseDirectory, AppConstants.ExecutableName);

    public static AppPathService CreatePathService()
    {
        return new AppPathService();
    }

    public string GetBackendExecutablePath()
    {
        return Path.Combine(
            RootDirectory,
            "backend",
            BackendExecutableNames.ManagedExecutableFileName
        );
    }

    public static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public string WriteBackendConfig(int port)
    {
        var authDirectory = Path.Combine(RootDirectory, "backend", "auth");
        var configPath = Path.Combine(RootDirectory, "config", AppConstants.BackendConfigFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        Directory.CreateDirectory(authDirectory);

        var yaml =
            $"host: \"127.0.0.1\"{Environment.NewLine}"
            + $"port: {port}{Environment.NewLine}"
            + "remote-management:"
            + Environment.NewLine
            + "  allow-remote: false"
            + Environment.NewLine
            + "  secret-key: \"smoke-only\""
            + Environment.NewLine
            + "  disable-control-panel: true"
            + Environment.NewLine
            + "  disable-auto-update-panel: true"
            + Environment.NewLine
            + $"auth-dir: \"{EscapeYaml(authDirectory)}\"{Environment.NewLine}"
            + "api-keys:"
            + Environment.NewLine
            + "  - \"sk-smoke\""
            + Environment.NewLine
            + "logging-to-file: true"
            + Environment.NewLine
            + "oauth-model-alias:"
            + Environment.NewLine
            + "  codex:"
            + Environment.NewLine
            + "    - name: \"gpt-5.4\""
            + Environment.NewLine
            + "      alias: \"gpt-5-codex\""
            + Environment.NewLine
            + "      fork: true"
            + Environment.NewLine;

        File.WriteAllText(
            configPath,
            yaml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        return configPath;
    }

    public static async Task WaitForAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        string failureMessage
    )
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(failureMessage);
    }

    public static async Task WaitForHttpOkAsync(string url, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastError = exception;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            lastError is null
                ? $"Timed out waiting for HTTP 200 from '{url}'."
                : $"Timed out waiting for HTTP 200 from '{url}': {lastError.Message}"
        );
    }

    public IReadOnlyList<int> GetOwnedBackendProcessIds()
    {
        var backendPath = GetBackendExecutablePath();
        return Process
            .GetProcessesByName(GetManagedBackendProcessName())
            .Where(process => MatchesProcessPath(process, backendPath))
            .Select(process => process.Id)
            .Order()
            .ToArray();
    }

    public void StopOwnedBackendProcesses()
    {
        foreach (var process in Process.GetProcessesByName(GetManagedBackendProcessName()))
        {
            try
            {
                if (MatchesProcessPath(process, GetBackendExecutablePath()))
                {
                    StopExactProcess(process);
                }
            }
            catch
            {
                // Ignore process races during cleanup.
            }
        }
    }

    public void Dispose()
    {
        StopOwnedBackendProcesses();

        foreach (var pair in _originalValues)
        {
            SetEnvironment(pair.Key, pair.Value);
        }

        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup races after test assertions have already completed.
        }
    }

    public static void StopExactProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill();
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { }
    }

    public static void CreateZipWithEntries(string packagePath, params string[] entryNames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                1024,
                leaveOpen: false
            );
            writer.Write("smoke");
        }
    }

    public static void CreateZipWithByteEntries(
        string packagePath,
        IReadOnlyDictionary<string, byte[]> entries
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        foreach (var entryPair in entries)
        {
            var entry = archive.CreateEntry(entryPair.Key);
            using var stream = entry.Open();
            stream.Write(entryPair.Value, 0, entryPair.Value.Length);
        }
    }

    public static byte[] CreatePeStubBytes()
    {
        var bytes = new byte[80];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    public static void CreatePeStub(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, CreatePeStubBytes());
    }

    private static string GetManagedBackendProcessName()
    {
        return Path.GetFileNameWithoutExtension(BackendExecutableNames.ManagedExecutableFileName);
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

        throw new DirectoryNotFoundException(
            "Could not locate CodexCliPlus.sln from the test output directory."
        );
    }

    private static string EscapeYaml(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static bool MatchesProcessPath(Process process, string expectedPath)
    {
        try
        {
            return string.Equals(
                process.MainModule?.FileName,
                expectedPath,
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch
        {
            return false;
        }
    }

    private static void SetEnvironment(string key, string? value)
    {
        Environment.SetEnvironmentVariable(key, value);
    }
}

internal sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        throw new InvalidOperationException(
            $"Unexpected outbound HTTP client request for '{name}'."
        );
    }
}

internal sealed class FixedHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly HttpClient _client;

    public FixedHttpClientFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _client = new HttpClient(new FixedHandler(handler));
    }

    public HttpClient CreateClient(string name)
    {
        return _client;
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private sealed class FixedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public FixedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
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

internal sealed class ThrowingProcessRunner : IProcessRunner
{
    public Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        BuildLogger logger,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default
    )
    {
        throw new InvalidOperationException(
            $"Smoke verification should not spawn '{fileName} {string.Join(" ", arguments)}'."
        );
    }
}
