using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using CPAD.Core.Abstractions.Configuration;
using CPAD.Core.Abstractions.Paths;
using CPAD.Core.Constants;
using CPAD.Core.Models;
using CPAD.Infrastructure.Security;

namespace CPAD.Infrastructure.Backend;

public sealed class BackendConfigWriter
{
    private readonly IAppConfigurationService _configurationService;
    private readonly IPathService _pathService;

    public BackendConfigWriter(
        IAppConfigurationService configurationService,
        IPathService pathService)
    {
        _configurationService = configurationService;
        _pathService = pathService;
    }

    public async Task<BackendRuntimeInfo> WriteAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _pathService.EnsureCreatedAsync(cancellationToken);

        settings.ManagementKey = string.IsNullOrWhiteSpace(settings.ManagementKey)
            ? GenerateManagementKey()
            : settings.ManagementKey;

        var requestedPort = AppConstants.DefaultBackendPort;
        settings.BackendPort = requestedPort;

        var authDirectory = ResolveAuthDirectory();
        Directory.CreateDirectory(authDirectory);
        var managementKeyHash = ResolveManagementKeyHash(settings.ManagementKey);

        var yaml = BuildYaml(
            settings.BackendPort,
            managementKeyHash,
            authDirectory);
        await File.WriteAllTextAsync(
            _pathService.Directories.BackendConfigFilePath,
            yaml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        await _configurationService.SaveAsync(settings, cancellationToken);

        if (!IsPortAvailable(settings.BackendPort))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"CPAD backend port {settings.BackendPort} is already in use. Stop the process using 127.0.0.1:{settings.BackendPort} and start CPAD again."));
        }

        var loopbackBaseUrl = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{settings.BackendPort}");
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
            ManagementApiBaseUrl = $"{loopbackBaseUrl}/v0/management"
        };
    }

    private static string BuildYaml(
        int port,
        string managementKey,
        string authDirectory)
    {
        var builder = new StringBuilder();
        AppendInvariant(builder, $"host: \"127.0.0.1\"{Environment.NewLine}");
        AppendInvariant(builder, $"port: {port}{Environment.NewLine}");
        builder.Append("remote-management:" + Environment.NewLine);
        builder.Append("  allow-remote: false" + Environment.NewLine);
        AppendInvariant(builder, $"  secret-key: \"{EscapeYaml(managementKey)}\"{Environment.NewLine}");
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
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private string ResolveManagementKeyHash(string managementKey)
    {
        var existingHash = TryLoadExistingManagementKeyHash();
        if (!string.IsNullOrWhiteSpace(existingHash) && BCrypt.Net.BCrypt.Verify(managementKey, existingHash))
        {
            return existingHash;
        }

        return ManagementKeyHasher.Hash(managementKey);
    }

    private static string GenerateManagementKey()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
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
