using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Abstractions.Configuration;
using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Infrastructure.Backend;

public sealed class BackendConfigWriter
{
    private readonly IAppConfigurationService _configurationService;
    private readonly IPathService _pathService;

    public BackendConfigWriter(
        IAppConfigurationService configurationService,
        IPathService pathService
    )
    {
        _configurationService = configurationService;
        _pathService = pathService;
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

        if (string.IsNullOrWhiteSpace(settings.ManagementKey))
        {
            if (settings.SecurityKeyOnboardingCompleted)
            {
                throw new InvalidOperationException(
                    "请先输入现有安全密钥。普通登录不会生成或重置安全密钥。"
                );
            }

            settings.ManagementKey = GenerateManagementKey();
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
        await File.WriteAllTextAsync(
            _pathService.Directories.BackendConfigFilePath,
            yaml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );

        await _configurationService.SaveAsync(settings, cancellationToken);

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
        return !string.IsNullOrWhiteSpace(TryLoadExistingManagementKeyHash());
    }

    public bool VerifyManagementKey(string managementKey)
    {
        if (string.IsNullOrWhiteSpace(managementKey))
        {
            return false;
        }

        var existingHash = TryLoadExistingManagementKeyHash();
        if (string.IsNullOrWhiteSpace(existingHash))
        {
            return false;
        }

        return VerifyManagementKeyHash(managementKey.Trim(), existingHash);
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
        var existingHash = TryLoadExistingManagementKeyHash();
        if (string.IsNullOrWhiteSpace(existingHash))
        {
            if (!allowManagementKeyRotation)
            {
                throw new InvalidOperationException(
                    "未找到现有后端安全密钥配置。普通登录不会生成或重置安全密钥。"
                );
            }

            return ManagementKeyHasher.Hash(managementKey);
        }

        if (VerifyManagementKeyHash(managementKey, existingHash))
        {
            return existingHash;
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

    private string? TryLoadExistingManagementKeyHash()
    {
        if (!File.Exists(_pathService.Directories.BackendConfigFilePath))
        {
            return null;
        }

        try
        {
            var yaml = File.ReadAllText(_pathService.Directories.BackendConfigFilePath);
            var match = Regex.Match(yaml, "secret-key:\\s*\"(?<hash>\\$2[aby]\\$.+)\"");
            return match.Success ? match.Groups["hash"].Value : null;
        }
        catch
        {
            return null;
        }
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
