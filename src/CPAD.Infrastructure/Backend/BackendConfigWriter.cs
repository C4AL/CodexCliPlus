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

using YamlDotNet.Serialization;

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

        var requestedPort = settings.BackendPort <= 0 ? AppConstants.DefaultBackendPort : settings.BackendPort;
        settings.BackendPort = FindAvailablePort(requestedPort);

        var legacyConfig = TryLoadLegacyConfig();
        var authDirectory = ResolveAuthDirectory(legacyConfig);
        Directory.CreateDirectory(authDirectory);
        var managementKeyHash = ResolveManagementKeyHash(settings.ManagementKey);

        var yaml = BuildYaml(
            settings.BackendPort,
            managementKeyHash,
            authDirectory,
            legacyConfig);
        await File.WriteAllTextAsync(
            _pathService.Directories.BackendConfigFilePath,
            yaml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        await _configurationService.SaveAsync(settings, cancellationToken);

        var loopbackBaseUrl = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{settings.BackendPort}");
        return new BackendRuntimeInfo
        {
            RequestedPort = requestedPort,
            Port = settings.BackendPort,
            PortWasAdjusted = settings.BackendPort != requestedPort,
            PortMessage = settings.BackendPort == requestedPort
                ? null
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Preferred port {requestedPort} was unavailable. Switched to {settings.BackendPort}."),
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
        string authDirectory,
        LegacyCliProxyApiConfig? legacyConfig)
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

        var apiKeys = legacyConfig?.ApiKeys?.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).ToArray()
            ?? ["sk-dummy"];

        if (apiKeys.Length == 0)
        {
            apiKeys = ["sk-dummy"];
        }

        builder.Append("api-keys:" + Environment.NewLine);
        foreach (var apiKey in apiKeys)
        {
            AppendInvariant(builder, $"  - \"{EscapeYaml(apiKey)}\"{Environment.NewLine}");
        }

        if (legacyConfig?.CommercialMode is not null)
        {
            AppendInvariant(
                builder,
                $"commercial-mode: {legacyConfig.CommercialMode.Value.ToString().ToLowerInvariant()}{Environment.NewLine}");
        }

        builder.Append("logging-to-file: true" + Environment.NewLine);
        builder.Append("logs-max-total-size-mb: 64" + Environment.NewLine);
        builder.Append("usage-statistics-enabled: true" + Environment.NewLine);

        if (!string.IsNullOrWhiteSpace(legacyConfig?.ProxyUrl))
        {
            AppendInvariant(builder, $"proxy-url: \"{EscapeYaml(legacyConfig.ProxyUrl)}\"{Environment.NewLine}");
        }

        if (legacyConfig?.AuthAutoRefreshWorkers is not null)
        {
            AppendInvariant(builder, $"auth-auto-refresh-workers: {legacyConfig.AuthAutoRefreshWorkers.Value}{Environment.NewLine}");
        }

        if (legacyConfig?.Routing is not null)
        {
            builder.Append("routing:" + Environment.NewLine);

            if (!string.IsNullOrWhiteSpace(legacyConfig.Routing.Strategy))
            {
                AppendInvariant(builder, $"  strategy: \"{EscapeYaml(legacyConfig.Routing.Strategy)}\"{Environment.NewLine}");
            }

            if (legacyConfig.Routing.SessionAffinity is not null)
            {
                AppendInvariant(
                    builder,
                    $"  session-affinity: {legacyConfig.Routing.SessionAffinity.Value.ToString().ToLowerInvariant()}{Environment.NewLine}");
            }

            if (!string.IsNullOrWhiteSpace(legacyConfig.Routing.SessionAffinityTtl))
            {
                AppendInvariant(builder, $"  session-affinity-ttl: \"{EscapeYaml(legacyConfig.Routing.SessionAffinityTtl)}\"{Environment.NewLine}");
            }
        }

        AppendOauthModelAlias(builder, legacyConfig);
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

    private static LegacyCliProxyApiConfig? TryLoadLegacyConfig()
    {
        var legacyConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cli-proxy-api",
            "config.yaml");

        if (!File.Exists(legacyConfigPath))
        {
            return null;
        }

        try
        {
            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            using var reader = File.OpenText(legacyConfigPath);
            return deserializer.Deserialize<LegacyCliProxyApiConfig>(reader);
        }
        catch
        {
            return null;
        }
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

    private string ResolveAuthDirectory(LegacyCliProxyApiConfig? legacyConfig)
    {
        if (!string.IsNullOrWhiteSpace(legacyConfig?.AuthDirectory) && Directory.Exists(legacyConfig.AuthDirectory))
        {
            var hasAuthFiles = Directory.EnumerateFileSystemEntries(legacyConfig.AuthDirectory).Any();
            if (hasAuthFiles)
            {
                return legacyConfig.AuthDirectory;
            }
        }

        return Path.Combine(_pathService.Directories.BackendDirectory, "auth");
    }

    private static void AppendOauthModelAlias(StringBuilder builder, LegacyCliProxyApiConfig? legacyConfig)
    {
        var aliases = legacyConfig?.OauthModelAlias;
        builder.Append("oauth-model-alias:" + Environment.NewLine);

        if (aliases is null || aliases.Count == 0)
        {
            builder.Append("  codex:" + Environment.NewLine);
            builder.Append("    - name: \"gpt-5.4\"" + Environment.NewLine);
            builder.Append("      alias: \"gpt-5-codex\"" + Environment.NewLine);
            builder.Append("      fork: true" + Environment.NewLine);
            return;
        }

        foreach (var pair in aliases)
        {
            AppendInvariant(builder, $"  {pair.Key}:{Environment.NewLine}");
            foreach (var alias in pair.Value)
            {
                AppendInvariant(builder, $"    - name: \"{EscapeYaml(alias.Name)}\"{Environment.NewLine}");
                AppendInvariant(builder, $"      alias: \"{EscapeYaml(alias.Alias)}\"{Environment.NewLine}");
                AppendInvariant(builder, $"      fork: {alias.Fork.ToString().ToLowerInvariant()}{Environment.NewLine}");
            }
        }
    }

    private static void AppendInvariant(StringBuilder builder, FormattableString value)
    {
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static int FindAvailablePort(int preferredPort)
    {
        for (var port = preferredPort; port < preferredPort + 32; port++)
        {
            if (IsPortAvailable(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"No available loopback port was found starting from {preferredPort}."));
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

    private sealed class LegacyCliProxyApiConfig
    {
        [YamlMember(Alias = "auth-dir")]
        public string? AuthDirectory { get; init; }

        [YamlMember(Alias = "api-keys")]
        public string[]? ApiKeys { get; init; }

        [YamlMember(Alias = "commercial-mode")]
        public bool? CommercialMode { get; init; }

        [YamlMember(Alias = "proxy-url")]
        public string? ProxyUrl { get; init; }

        [YamlMember(Alias = "auth-auto-refresh-workers")]
        public int? AuthAutoRefreshWorkers { get; init; }

        [YamlMember(Alias = "routing")]
        public LegacyRoutingConfig? Routing { get; init; }

        [YamlMember(Alias = "oauth-model-alias")]
        public Dictionary<string, List<LegacyOauthAlias>>? OauthModelAlias { get; init; }
    }

    private sealed class LegacyRoutingConfig
    {
        [YamlMember(Alias = "strategy")]
        public string? Strategy { get; init; }

        [YamlMember(Alias = "session-affinity")]
        public bool? SessionAffinity { get; init; }

        [YamlMember(Alias = "session-affinity-ttl")]
        public string? SessionAffinityTtl { get; init; }
    }

    private sealed class LegacyOauthAlias
    {
        [YamlMember(Alias = "name")]
        public string Name { get; init; } = string.Empty;

        [YamlMember(Alias = "alias")]
        public string Alias { get; init; } = string.Empty;

        [YamlMember(Alias = "fork")]
        public bool Fork { get; init; }
    }
}
