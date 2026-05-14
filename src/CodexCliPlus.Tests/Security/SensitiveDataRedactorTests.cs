using CodexCliPlus.Infrastructure.Security;

namespace CodexCliPlus.Tests.Security;

[Trait("Category", "Fast")]
public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void RedactCoversCommonSecretKeyVariants()
    {
        var input = string.Join(
            Environment.NewLine,
            "secret_key: \"under-secret\"",
            "managementKey = 'camel-management'",
            "api_key=\"sk-under\"",
            "OPENAI_API_KEY=sk-env",
            "authorization: Basic basic-secret",
            "x-goog-api-key: goog-secret"
        );

        var redacted = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("under-secret", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("camel-management", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-under", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-env", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("basic-secret", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("goog-secret", redacted, StringComparison.Ordinal);
        Assert.Equal(6, redacted.Split("***", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void RedactCoversQuotedJsonSecretProperties()
    {
        var input = string.Join(
            Environment.NewLine,
            "\"secret_key\": \"json-secret\"",
            "\"api_key\": \"sk-json\"",
            "\"OPENAI_API_KEY\": \"sk-env-json\"",
            "\"Authorization\": \"Bearer bearer-json\"",
            "\"refresh_token\": \"refresh-json\"",
            "\"x-goog-api-key\": \"goog-json\""
        );

        var redacted = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("json-secret", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-env-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("goog-json", redacted, StringComparison.Ordinal);
        Assert.Equal(6, redacted.Split("***", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void RedactCoversSensitiveMigrationKeyNames()
    {
        var input = string.Join(
            Environment.NewLine,
            "\"client_secret\": \"client-json\"",
            "\"private_key\": \"private-json\"",
            "\"secret\": \"plain-json\"",
            "\"tokens\": \"tokens-json\"",
            "\"id_token\": \"id-json\"",
            "\"anthropic-beta\": \"anthropic-json\""
        );

        var redacted = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("client-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("private-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("tokens-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("id-json", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("anthropic-json", redacted, StringComparison.Ordinal);
        Assert.Equal(6, redacted.Split("***", StringSplitOptions.None).Length - 1);
    }
}
