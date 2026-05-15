using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Models.Security;
using YamlDotNet.RepresentationModel;

namespace CodexCliPlus.Infrastructure.Security;

public sealed class SensitiveConfigMigrationService
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api-key",
        "api_key",
        "apikey",
        "api-keys",
        "api_keys",
        "access-token",
        "access_token",
        "refresh-token",
        "refresh_token",
        "id-token",
        "id_token",
        "token",
        "tokens",
        "authorization",
        "cookie",
        "secret",
        "secret-key",
        "secret_key",
        "client-secret",
        "client_secret",
        "private-key",
        "private_key",
        "upstream-api-key",
        "upstream_api_key",
        "upstream-api-keys",
        "upstream_api_keys",
        "x-api-key",
        "proxy-authorization",
    };

    private static readonly HashSet<string> SensitiveHeaderNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "authorization",
        "cookie",
        "x-api-key",
        "x-goog-api-key",
        "proxy-authorization",
        "anthropic-beta",
    };

    private static readonly Regex Normalizer = new("[_\\s]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ISecretVault _secretVault;

    public SensitiveConfigMigrationService(ISecretVault secretVault)
    {
        _secretVault = secretVault;
    }

    public async Task<SensitiveConfigMigrationResult> MigrateJsonAsync(
        string json,
        string source,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateResult(json, "json", []);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return await MigrateOpaqueSecretAsync(
                json,
                source,
                SecretKind.AuthJson,
                "json",
                cancellationToken
            );
        }

        if (root is null)
        {
            return CreateResult(json, "json", []);
        }

        var secrets = new List<VaultSecretExport>();
        await MigrateJsonNodeAsync(
            root,
            source,
            "$",
            parentKey: null,
            parentSensitive: false,
            secrets: secrets,
            cancellationToken: cancellationToken
        );
        return CreateResult(root.ToJsonString(JsonOptions), "json", secrets);
    }

    public async Task<SensitiveConfigMigrationResult> MigrateYamlAsync(
        string yaml,
        string source,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return CreateResult(yaml, "yaml", []);
        }

        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count == 0)
        {
            return CreateResult(yaml, "yaml", []);
        }

        var secrets = new List<VaultSecretExport>();
        await MigrateYamlNodeAsync(
            stream.Documents[0].RootNode,
            source,
            "$",
            parentKey: null,
            parentSensitive: false,
            secrets: secrets,
            cancellationToken: cancellationToken
        );

        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return CreateResult(writer.ToString(), "yaml", secrets);
    }

    private async Task<SensitiveConfigMigrationResult> MigrateOpaqueSecretAsync(
        string value,
        string source,
        SecretKind kind,
        string format,
        CancellationToken cancellationToken
    )
    {
        var export = await SaveSecretAsync(kind, value, source, "$", null, cancellationToken);
        return CreateResult(new SecretRef(export.SecretId).Uri, format, [export]);
    }

    private async Task MigrateJsonNodeAsync(
        JsonNode node,
        string source,
        string path,
        string? parentKey,
        bool parentSensitive,
        List<VaultSecretExport> secrets,
        CancellationToken cancellationToken
    )
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToArray())
                {
                    var key = property.Key;
                    var child = property.Value;
                    if (child is null)
                    {
                        continue;
                    }

                    var propertySensitive = parentSensitive || IsSensitiveKey(key);
                    if (IsHeadersKey(parentKey) && IsSensitiveHeaderKey(key))
                    {
                        propertySensitive = true;
                    }

                    var childPath = $"{path}.{key}";
                    if (
                        child is JsonValue jsonValue
                        && propertySensitive
                        && TryGetMigratableString(jsonValue, out var secretValue)
                    )
                    {
                        var export = await SaveSecretAsync(
                            ResolveKind(key, parentKey),
                            secretValue,
                            source,
                            childPath,
                            key,
                            cancellationToken
                        );
                        jsonObject[key] = new SecretRef(export.SecretId).Uri;
                        secrets.Add(export);
                        continue;
                    }

                    await MigrateJsonNodeAsync(
                        child,
                        source,
                        childPath,
                        key,
                        propertySensitive,
                        secrets,
                        cancellationToken
                    );
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var child = jsonArray[index];
                    if (child is null)
                    {
                        continue;
                    }

                    var childPath = $"{path}[{index}]";
                    if (
                        parentSensitive
                        && child is JsonValue jsonValue
                        && TryGetMigratableString(jsonValue, out var secretValue)
                    )
                    {
                        var export = await SaveSecretAsync(
                            ResolveKind(parentKey, null),
                            secretValue,
                            source,
                            childPath,
                            parentKey,
                            cancellationToken
                        );
                        jsonArray[index] = new SecretRef(export.SecretId).Uri;
                        secrets.Add(export);
                        continue;
                    }

                    await MigrateJsonNodeAsync(
                        child,
                        source,
                        childPath,
                        parentKey,
                        parentSensitive,
                        secrets,
                        cancellationToken
                    );
                }

                break;
        }
    }

    private async Task MigrateYamlNodeAsync(
        YamlNode node,
        string source,
        string path,
        string? parentKey,
        bool parentSensitive,
        List<VaultSecretExport> secrets,
        CancellationToken cancellationToken
    )
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                var replacements = new List<(YamlNode Key, YamlNode Value)>();
                foreach (var pair in mapping.Children.ToArray())
                {
                    var key = pair.Key is YamlScalarNode keyNode
                        ? keyNode.Value ?? string.Empty
                        : string.Empty;
                    var propertySensitive = parentSensitive || IsSensitiveKey(key);
                    if (IsHeadersKey(parentKey) && IsSensitiveHeaderKey(key))
                    {
                        propertySensitive = true;
                    }

                    var childPath = $"{path}.{key}";
                    if (
                        pair.Value is YamlScalarNode scalar
                        && propertySensitive
                        && TryGetMigratableString(scalar.Value, out var secretValue)
                    )
                    {
                        var export = await SaveSecretAsync(
                            ResolveKind(key, parentKey),
                            secretValue,
                            source,
                            childPath,
                            key,
                            cancellationToken
                        );
                        replacements.Add(
                            (pair.Key, new YamlScalarNode(new SecretRef(export.SecretId).Uri))
                        );
                        secrets.Add(export);
                        continue;
                    }

                    await MigrateYamlNodeAsync(
                        pair.Value,
                        source,
                        childPath,
                        key,
                        propertySensitive,
                        secrets,
                        cancellationToken
                    );
                }

                foreach (var replacement in replacements)
                {
                    mapping.Children[replacement.Key] = replacement.Value;
                }

                break;

            case YamlSequenceNode sequence:
                for (var index = 0; index < sequence.Children.Count; index++)
                {
                    var child = sequence.Children[index];
                    var childPath = $"{path}[{index}]";
                    if (
                        parentSensitive
                        && child is YamlScalarNode scalar
                        && TryGetMigratableString(scalar.Value, out var secretValue)
                    )
                    {
                        var export = await SaveSecretAsync(
                            ResolveKind(parentKey, null),
                            secretValue,
                            source,
                            childPath,
                            parentKey,
                            cancellationToken
                        );
                        sequence.Children[index] = new YamlScalarNode(
                            new SecretRef(export.SecretId).Uri
                        );
                        secrets.Add(export);
                        continue;
                    }

                    await MigrateYamlNodeAsync(
                        child,
                        source,
                        childPath,
                        parentKey,
                        parentSensitive,
                        secrets,
                        cancellationToken
                    );
                }

                break;
        }
    }

    private async Task<VaultSecretExport> SaveSecretAsync(
        SecretKind kind,
        string value,
        string source,
        string path,
        string? field,
        CancellationToken cancellationToken
    )
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["path"] = path,
        };
        if (!string.IsNullOrWhiteSpace(field))
        {
            metadata["field"] = field.Trim();
        }

        var record = await _secretVault.SaveSecretAsync(
            kind,
            value,
            source,
            metadata,
            cancellationToken: cancellationToken
        );

        return new VaultSecretExport
        {
            SecretId = record.SecretId,
            Kind = record.Kind,
            Source = record.Source,
            CreatedAtUtc = record.CreatedAtUtc,
            Status = record.Status,
            Value = value,
            Metadata = new Dictionary<string, string>(
                record.Metadata,
                StringComparer.OrdinalIgnoreCase
            ),
        };
    }

    private static SensitiveConfigMigrationResult CreateResult(
        string content,
        string format,
        List<VaultSecretExport> secrets
    )
    {
        return new SensitiveConfigMigrationResult
        {
            Content = content,
            Format = format,
            Secrets = secrets,
            Report = new SecretMigrationReport
            {
                TotalSecrets = secrets.Count,
                ApiKeys = secrets.Count(secret => secret.Kind == SecretKind.ApiKey),
                OAuthTokens = secrets.Count(secret =>
                    secret.Kind
                        is SecretKind.OAuthAccessToken
                            or SecretKind.OAuthRefreshToken
                            or SecretKind.OAuthToken
                ),
                Headers = secrets.Count(secret =>
                    secret.Kind
                        is SecretKind.AuthorizationHeader
                            or SecretKind.Cookie
                            or SecretKind.Header
                ),
                AuthJsonValues = secrets.Count(secret => secret.Kind == SecretKind.AuthJson),
            },
        };
    }

    private static bool TryGetMigratableString(JsonValue value, out string secretValue)
    {
        secretValue = string.Empty;
        return value.TryGetValue<string>(out var text)
            && TryGetMigratableString(text, out secretValue);
    }

    private static bool TryGetMigratableString(string? value, out string secretValue)
    {
        secretValue = value?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(secretValue) && !SecretRef.IsSecretRef(secretValue);
    }

    private static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = NormalizeKey(key);
        return SensitiveKeys.Contains(normalized)
            || normalized.EndsWith("-token", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("-secret", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("-api-key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeadersKey(string? key)
    {
        return string.Equals(key, "headers", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSensitiveHeaderKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return SensitiveHeaderNames.Contains(key.Trim())
            || IsSensitiveKey(key)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string key)
    {
        return Normalizer.Replace(key.Trim().ToLowerInvariant(), "-");
    }

    private static SecretKind ResolveKind(string? key, string? parentKey)
    {
        var normalizedKey = NormalizeKey(key ?? string.Empty);
        var normalizedParent = NormalizeKey(parentKey ?? string.Empty);

        if (normalizedKey.Contains("refresh-token", StringComparison.OrdinalIgnoreCase))
        {
            return SecretKind.OAuthRefreshToken;
        }

        if (
            normalizedKey.Contains("access-token", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.Contains("id-token", StringComparison.OrdinalIgnoreCase)
        )
        {
            return SecretKind.OAuthAccessToken;
        }

        if (
            normalizedKey.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || normalizedParent.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        )
        {
            return SecretKind.AuthorizationHeader;
        }

        if (normalizedKey.Contains("cookie", StringComparison.OrdinalIgnoreCase))
        {
            return SecretKind.Cookie;
        }

        if (normalizedKey.Contains("api-key", StringComparison.OrdinalIgnoreCase))
        {
            return SecretKind.ApiKey;
        }

        if (normalizedKey.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return SecretKind.OAuthToken;
        }

        if (IsHeadersKey(parentKey))
        {
            return SecretKind.Header;
        }

        if (
            normalizedKey.Contains("private-key", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.Contains("secret", StringComparison.OrdinalIgnoreCase)
        )
        {
            return SecretKind.ProviderCredential;
        }

        return SecretKind.Unknown;
    }
}
