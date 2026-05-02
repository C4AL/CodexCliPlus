using CodexCliPlus.Core.Enums;
using CodexCliPlus.Infrastructure.Codex;

namespace CodexCliPlus.Tests.Codex;

[Collection("CodexConfigService")]
public sealed class CodexConfigServiceTests : IDisposable
{
    private readonly string _codexHome = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-codex-home-{Guid.NewGuid():N}"
    );
    private readonly string? _originalCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");

    [Fact]
    public async Task ApplyProfilesAsyncPreservesExistingConfigAndCreatesBackup()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var configPath = Path.Combine(_codexHome, "config.toml");
        await File.WriteAllTextAsync(
            configPath,
            "model = \"gpt-5.4\"\n[notice]\nhide_full_access_warning = true\n"
        );

        var service = new CodexConfigService();
        await service.ApplyProfilesAsync(9318, CodexSourceKind.Cpa);

        var content = await File.ReadAllTextAsync(configPath);
        var backups = Directory.GetFiles(_codexHome, "config.codexcliplus-backup-*.toml");

        Assert.Contains("profile = \"cpa\"", content, StringComparison.Ordinal);
        Assert.Contains("model = \"gpt-5.4\"", content, StringComparison.Ordinal);
        Assert.Contains("[profiles.official]", content, StringComparison.Ordinal);
        Assert.Contains("[profiles.cpa]", content, StringComparison.Ordinal);
        Assert.Contains("model_provider = \"cliproxyapi\"", content, StringComparison.Ordinal);
        Assert.Contains(
            "base_url = \"http://127.0.0.1:9318/v1\"",
            content,
            StringComparison.Ordinal
        );
        Assert.Single(backups);
    }

    [Fact]
    public async Task GetCodexRouteStateAsyncRecognizesLegacyTopLevelCpaProvider()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        await File.WriteAllTextAsync(
            Path.Combine(_codexHome, "config.toml"),
            "model_provider = \"cliproxyapi\"\nbase_url = \"http://127.0.0.1:8317/v1\"\nwire_api = \"responses\"\n"
        );

        var service = new CodexConfigService();
        var state = await service.GetCodexRouteStateAsync();

        Assert.Equal("cpa", state.CurrentMode);
        Assert.Equal("official", state.TargetMode);
    }

    [Fact]
    public void BuildLaunchCommandUsesRequestedProfileAndRepository()
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);
        var service = new CodexConfigService();

        var official = service.BuildLaunchCommand(CodexSourceKind.Official, @"C:\repo");
        var cpa = service.BuildLaunchCommand(CodexSourceKind.Cpa, null);

        Assert.Equal("codex --profile official -C \"C:\\repo\"", official);
        Assert.Equal("codex --profile cpa", cpa);
    }

    [Fact]
    public async Task ApplyProfilesAsyncThrowsForInvalidTomlAndKeepsOriginalFile()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var configPath = Path.Combine(_codexHome, "config.toml");
        var invalidToml = "model = \"gpt-5.4\"\n[notice\n";
        await File.WriteAllTextAsync(configPath, invalidToml);

        var service = new CodexConfigService();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyProfilesAsync(9318, CodexSourceKind.Official)
        );

        var finalContent = await File.ReadAllTextAsync(configPath);

        Assert.Contains(
            "Codex configuration validation failed",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(invalidToml, finalContent);
    }

    [Fact]
    public async Task InspectAsyncReadsAppliedProfileAndProjectConfig()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var repositoryPath = Path.Combine(_codexHome, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryPath, ".codex"));
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, ".codex", "config.toml"),
            "model = \"gpt-5.4\"\n"
        );

        var service = new CodexConfigService();
        await service.ApplyDesktopModeAsync(9318, CodexSourceKind.Cpa);

        var status = await service.InspectAsync(
            repositoryPath,
            @"C:\tools\codex.cmd",
            "1.2.3",
            "signed-in"
        );

        Assert.True(status.IsInstalled);
        Assert.True(status.HasUserConfig);
        Assert.True(status.HasProjectConfig);
        Assert.Equal("cpa", status.DefaultProfile);
        Assert.Equal("cpa", status.EffectiveSource);
        Assert.Equal("signed-in", status.AuthenticationState);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", _originalCodexHome);
        if (Directory.Exists(_codexHome))
        {
            Directory.Delete(_codexHome, recursive: true);
        }
    }
}
