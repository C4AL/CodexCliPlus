using CodexCliPlus.Infrastructure.Backend;

namespace CodexCliPlus.Tests.Backend;

[Trait("Category", "LocalIntegration")]
public sealed class BackendConfigReaderTests
{
    [Theory]
    [InlineData("\"C:\\\\Users\\\\Reol\\\\AppData\\\\Local\\\\CodexCliPlus\\\\backend\\\\auth\"")]
    [InlineData("'C:\\Users\\Reol\\AppData\\Local\\CodexCliPlus\\backend\\auth'")]
    [InlineData("C:\\Users\\Reol\\AppData\\Local\\CodexCliPlus\\backend\\auth")]
    public void TryReadAuthDirectoryAcceptsYamlScalarStyles(string scalar)
    {
        var yaml =
            "host: \"127.0.0.1\""
            + Environment.NewLine
            + "auth-dir: "
            + scalar
            + Environment.NewLine;

        var authDirectory = BackendConfigReader.TryReadAuthDirectory(yaml);

        Assert.Equal(
            "C:\\Users\\Reol\\AppData\\Local\\CodexCliPlus\\backend\\auth",
            authDirectory
        );
    }

    [Theory]
    [InlineData(
        "\"$2a$11$aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"",
        "$2a$11$aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    )]
    [InlineData("'ccp-secret://sec-management-key'", "ccp-secret://sec-management-key")]
    [InlineData("plain-management-key", "plain-management-key")]
    public void TryReadRemoteManagementSecretKeyAcceptsYamlScalarStyles(
        string scalar,
        string expected
    )
    {
        var yaml =
            "remote-management:"
            + Environment.NewLine
            + "  allow-remote: false"
            + Environment.NewLine
            + "  secret-key: "
            + scalar
            + Environment.NewLine;

        var secretKey = BackendConfigReader.TryReadRemoteManagementSecretKey(yaml);

        Assert.Equal(expected, secretKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auth-dir:")]
    [InlineData("auth-dir:\n  nested: true")]
    [InlineData("auth-dir: [broken")]
    public void TryReadAuthDirectoryReturnsNullWhenAuthDirectoryIsMissingOrInvalid(
        string yaml
    )
    {
        Assert.Null(BackendConfigReader.TryReadAuthDirectory(yaml));
    }
}
