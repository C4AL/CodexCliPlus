using System.Text.RegularExpressions;

namespace CPAD.Infrastructure.Security;

internal static partial class SensitiveDataRedactor
{
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var redacted = text;
        redacted = SecretKeyPattern().Replace(redacted, "$1***$3");
        redacted = AuthorizationPattern().Replace(redacted, "$1***");
        redacted = HeaderSecretPattern().Replace(redacted, "$1***");
        redacted = ApiKeyPattern().Replace(redacted, "$1***$3");
        redacted = TokenPattern().Replace(redacted, "$1***$3");
        return redacted;
    }

    [GeneratedRegex("(?im)(secret-key\\s*[:=]\\s*[\"']?)([^\\r\\n\"']+)([\"']?)")]
    private static partial Regex SecretKeyPattern();

    [GeneratedRegex("(?im)(authorization\\s*:\\s*bearer\\s+)([^\\s]+)")]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex("(?im)((?:x-management-key|x-api-key|cookie)\\s*:\\s*)([^\\r\\n]+)")]
    private static partial Regex HeaderSecretPattern();

    [GeneratedRegex("(?im)((?:api-key|management key)\\s*[:=]\\s*[\"']?)([^\\r\\n\"']+)([\"']?)")]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex("(?im)((?:access-token|refresh-token|token)\\s*[:=]\\s*[\"']?)([^\\r\\n\"']+)([\"']?)")]
    private static partial Regex TokenPattern();
}
