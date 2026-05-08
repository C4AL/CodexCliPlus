using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Core.Models.Security;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Infrastructure.Backend;

public sealed class BackendConfigWriter
{
    private readonly IAppConfigurationService _configurationService;
    private readonly IPathService _pathService;
    private readonly ISecretVault? _secretVault;

    public BackendConfigWriter(
        IAppConfigurationService configurationService,
        IPathService pathService,
        ISecretVault? secretVault = null
    )
    {
        _configurationService = configurationService;
        _pathService = pathService;
        _secretVault = secretVault;
    }

    public Task<BackendRuntimeInfo> WriteAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default
    )
    {
        return WriteAsync(settings, new BackendConfigWriteOptions(), cancellationToken);
    }

    public async Task<BackendRuntimeInfo> WriteAsync(
        AppSettings settings,
        BackendConfigWriteOptions options,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        await _pathService.EnsureCreatedAsync(cancellationToken);

        var settingsFileExists = File.Exists(_pathService.Directories.SettingsFilePath);
        var generatedManagementKey = false;

        if (string.IsNullOrWhiteSpace(settings.ManagementKey))
        {
            if (settings.SecurityKeyOnboardingCompleted)
            {
                throw new InvalidOperationException(
                    "请先输入现有安全密钥。普通登录不会生成或重置安全密钥。"
                );
            }

            settings.ManagementKey = GenerateManagementKey();
            generatedManagementKey = true;
        }
        else
        {
            settings.ManagementKey = settings.ManagementKey.Trim();
        }

        var requestedPort = AppConstants.DefaultBackendPort;
        settings.BackendPort = requestedPort;

        var authDirectory = ResolveAuthDirectory();
        Directory.CreateDirectory(authDirectory);
        var managementKeyHash = ResolveManagementKeyHash(
            settings.ManagementKey,
            options.AllowManagementKeyRotation || !settings.SecurityKeyOnboardingCompleted
        );

        var yaml = BuildYaml(settings.BackendPort, managementKeyHash, authDirectory);
        if (
            !await FileContentEqualsAsync(
                _pathService.Directories.BackendConfigFilePath,
                yaml,
                cancellationToken
            )
        )
        {
            await WriteTextAtomicallyAsync(
                _pathService.Directories.BackendConfigFilePath,
                yaml,
                cancellationToken
            );
        }

        if (!settingsFileExists || generatedManagementKey)
        {
            await _configurationService.SaveAsync(settings, cancellationToken);
        }

        if (options.ValidatePort && !IsPortAvailable(settings.BackendPort))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"CodexCliPlus backend port {settings.BackendPort} is already in use. Stop the process using 127.0.0.1:{settings.BackendPort} and start CodexCliPlus again."
                )
            );
        }

        var loopbackBaseUrl = string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{settings.BackendPort}"
        );
        return new BackendRuntimeInfo
        {
            RequestedPort = requestedPort,
            Port = settings.BackendPort,
            PortWasAdjusted = false,
            PortMessage = null,
            ManagementKey = settings.ManagementKey,
            ConfigPath = _pathService.Directories.BackendConfigFilePath,
            BaseUrl = loopbackBaseUrl,
            HealthUrl = $"{loopbackBaseUrl}/healthz",
            ManagementApiBaseUrl = $"{loopbackBaseUrl}/v0/management",
        };
    }

    public bool HasExistingManagementKeyHash()
    {
        return !string.IsNullOrWhiteSpace(TryLoadExistingManagementKeySecret());
    }

    public bool VerifyManagementKey(string managementKey)
    {
        if (string.IsNullOrWhiteSpace(managementKey))
        {
            return false;
        }

        var existingSecret = TryLoadExistingManagementKeySecret();
        if (string.IsNullOrWhiteSpace(existingSecret))
        {
            return false;
        }

        return VerifyManagementKeySecret(managementKey.Trim(), existingSecret);
    }

    private static string BuildYaml(int port, string managementKey, string authDirectory)
    {
        var builder = new StringBuilder();
        AppendInvariant(builder, $"host: \"127.0.0.1\"{Environment.NewLine}");
        AppendInvariant(builder, $"port: {port}{Environment.NewLine}");
        builder.Append("remote-management:" + Environment.NewLine);
        builder.Append("  allow-remote: false" + Environment.NewLine);
        AppendInvariant(
            builder,
            $"  secret-key: \"{EscapeYaml(managementKey)}\"{Environment.NewLine}"
        );
        builder.Append("  disable-control-panel: true" + Environment.NewLine);
        builder.Append("  disable-auto-update-panel: true" + Environment.NewLine);
        AppendInvariant(builder, $"auth-dir: \"{EscapeYaml(authDirectory)}\"{Environment.NewLine}");

        builder.Append("api-keys:" + Environment.NewLine);
        builder.Append("  - \"sk-dummy\"" + Environment.NewLine);

        builder.Append("logging-to-file: true" + Environment.NewLine);
        builder.Append("logs-max-total-size-mb: 64" + Environment.NewLine);
        builder.Append("usage-statistics-enabled: true" + Environment.NewLine);
        builder.Append("redis-usage-queue-retention-seconds: 60" + Environment.NewLine);

        AppendOauthModelAlias(builder);
        return builder.ToString();
    }

    private static string EscapeYaml(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private string ResolveManagementKeyHash(string managementKey, bool allowManagementKeyRotation)
    {
        var existingSecret = TryLoadExistingManagementKeySecret();
        if (string.IsNullOrWhiteSpace(existingSecret))
        {
            if (!allowManagementKeyRotation)
            {
                throw new InvalidOperationException(
                    "未找到现有后端安全密钥配置。普通登录不会生成或重置安全密钥。"
                );
            }

            return ManagementKeyHasher.Hash(managementKey);
        }

        if (VerifyManagementKeySecret(managementKey, existingSecret))
        {
            return LooksLikeBcryptHash(existingSecret)
                ? existingSecret
                : ManagementKeyHasher.Hash(managementKey);
        }

        if (!allowManagementKeyRotation)
        {
            throw new InvalidOperationException("安全密钥验证失败，现有后端密钥不会被重写。");
        }

        return ManagementKeyHasher.Hash(managementKey);
    }

    private static string GenerateManagementKey()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }

    private string? TryLoadExistingManagementKeySecret()
    {
        if (!File.Exists(_pathService.Directories.BackendConfigFilePath))
        {
            return null;
        }

        try
        {
            var yaml = File.ReadAllText(_pathService.Directories.BackendConfigFilePath);
            return ResolveSecretReference(
                BackendConfigReader.TryReadRemoteManagementSecretKey(yaml)
            );
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveSecretReference(string? value)
    {
        var secret = value?.Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        if (!SecretRef.TryParse(secret, out var secretRef))
        {
            return secret;
        }

        if (_secretVault is null)
        {
            return null;
        }

        return _secretVault
            .RevealSecretAsync(secretRef!.SecretId)
            .GetAwaiter()
            .GetResult()
            ?.Trim();
    }

    private static bool VerifyManagementKeySecret(string managementKey, string secret)
    {
        return LooksLikeBcryptHash(secret)
            ? VerifyManagementKeyHash(managementKey, secret)
            : FixedTimeEquals(managementKey, secret);
    }

    private static bool VerifyManagementKeyHash(string managementKey, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(managementKey, hash);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeBcryptHash(string value)
    {
        return value.StartsWith("$2a$", StringComparison.Ordinal)
            || value.StartsWith("$2b$", StringComparison.Ordinal)
            || value.StartsWith("$2y$", StringComparison.Ordinal);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private string ResolveAuthDirectory()
    {
        return Path.Combine(_pathService.Directories.BackendDirectory, "auth");
    }

    private static void AppendOauthModelAlias(StringBuilder builder)
    {
        builder.Append("oauth-model-alias:" + Environment.NewLine);
        builder.Append("  codex:" + Environment.NewLine);
        builder.Append("    - name: \"gpt-5.4\"" + Environment.NewLine);
        builder.Append("      alias: \"gpt-5-codex\"" + Environment.NewLine);
        builder.Append("      fork: true" + Environment.NewLine);
    }

    private static void AppendInvariant(StringBuilder builder, FormattableString value)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static async Task<bool> FileContentEqualsAsync(
        string path,
        string expectedContent,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var currentContent = await File.ReadAllTextAsync(path, cancellationToken);
        return string.Equals(currentContent, expectedContent, StringComparison.Ordinal);
    }

    private static async Task WriteTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var stream = new FileStream(
                    tempPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                    }
                )
            )
            {
                using (
                    var writer = new StreamWriter(
                        stream,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        bufferSize: 1024,
                        leaveOpen: true
                    )
                )
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                }

                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}

public sealed class BackendConfigWriteOptions
{
    public bool AllowManagementKeyRotation { get; init; }

    public bool ValidatePort { get; init; } = true;
}
