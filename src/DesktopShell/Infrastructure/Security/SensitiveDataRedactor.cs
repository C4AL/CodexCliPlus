using System.Text.RegularExpressions;

namespace CodexCliPlus.Infrastructure.Security;

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
        redacted = HeaderSecretPattern().Replace(redacted, "$1***$3");
        redacted = ApiKeyPattern().Replace(redacted, "$1***$3");
        redacted = TokenPattern().Replace(redacted, "$1***$3");
        return redacted;
    }

    [GeneratedRegex(
        "(?im)([\"']?(?:secret[-_]?key|client[-_]?secret|private[-_]?key|secret)[\"']?\\s*[:=]\\s*[\"']?)([^\\r\\n\"']+)([\"']?)"
    )]
    private static partial Regex SecretKeyPattern();

    [GeneratedRegex(
        "(?im)([\"']?(?:proxy-)?authorization[\"']?\\s*:\\s*[\"']?(?:(?:bearer|basic)\\s+)?)([^\\r\\n\"']+)"
    )]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex(
        "(?im)([\"']?(?:x-management-key|x-api-key|x-goog-api-key|cookie|set-cookie|anthropic-beta)[\"']?\\s*:\\s*[\"']?)([^\\r\\n\"']+)([\"']?)"
    )]
    private static partial Regex HeaderSecretPattern();

    [GeneratedRegex(
        "(?im)([\"']?(?:(?:[a-z0-9]+[-_])*(?:api[-_]?key)|management[-_ ]?key)[\"']?\\s*[:=]\\s*[\"']?)([^\\r\\n\"']+)([\"']?)"
    )]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(
        "(?im)([\"']?(?:access[-_]?token|refresh[-_]?token|id[-_]?token|tokens?)[\"']?\\s*[:=]\\s*[\"']?)([^\\r\\n\"']+)([\"']?)"
    )]
    private static partial Regex TokenPattern();
}
