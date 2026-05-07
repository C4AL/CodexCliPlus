using CodexCliPlus.Core.Enums;
using CodexCliPlus.Infrastructure.Codex;

namespace CodexCliPlus.Tests.Codex;

[Collection("CodexConfigService")]
[Trait("Category", "Fast")]
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

        Assert.Contains("profile = \"codexcliplus-cpa\"", content, StringComparison.Ordinal);
        Assert.Contains("model = \"gpt-5.4\"", content, StringComparison.Ordinal);
        Assert.Contains("[profiles.codexcliplus-official]", content, StringComparison.Ordinal);
        Assert.Contains("[profiles.codexcliplus-cpa]", content, StringComparison.Ordinal);
        Assert.Contains("model_provider = \"codexcliplus-cpa\"", content, StringComparison.Ordinal);
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
        Assert.StartsWith("third-party-cpa:", state.CurrentTargetId, StringComparison.Ordinal);
        Assert.Contains("第三方 CPA 127.0.0.1:8317", state.CurrentLabel, StringComparison.Ordinal);
        Assert.Contains(
            state.Targets,
            target =>
                target.Kind == "third-party-cpa"
                && target.BaseUrl == "http://127.0.0.1:8317/v1"
                && target.IsCurrent
        );
    }

    [Fact]
    public async Task GetCodexRouteStateAsyncRecognizesUnknownProviderWithConfiguredCpaBaseUrl()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        await File.WriteAllTextAsync(
            Path.Combine(_codexHome, "config.toml"),
            "model_provider = \"my-cpa\"\nbase_url = \"http://127.0.0.1:8317/v1\"\nwire_api = \"responses\"\n[model_providers.my-cpa]\nname = \"my-cpa\"\nbase_url = \"http://127.0.0.1:8317/v1\"\n"
        );

        var service = new CodexConfigService();
        var state = await service.GetCodexRouteStateAsync();

        Assert.Equal("cpa", state.CurrentMode);
        Assert.Contains("第三方 CPA 127.0.0.1:8317", state.CurrentLabel, StringComparison.Ordinal);
        Assert.Contains(
            state.Targets,
            target =>
                target.Kind == "third-party-cpa"
                && target.ProviderName == "my-cpa"
                && target.BaseUrl == "http://127.0.0.1:8317/v1"
                && target.IsCurrent
        );
    }

    [Fact]
    public async Task GetCodexUserFileSnapshotsAsyncOnlyReadsAllowedUserFiles()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var service = new CodexConfigService();
        var snapshots = await service.GetCodexUserFileSnapshotsAsync();

        Assert.Collection(
            snapshots,
            config =>
            {
                Assert.Equal("config", config.FileId);
                Assert.Equal(Path.Combine(_codexHome, "config.toml"), config.Path);
                Assert.False(config.Exists);
                Assert.Equal("toml", config.Language);
                Assert.True(config.Validation.IsValid);
            },
            auth =>
            {
                Assert.Equal("auth", auth.FileId);
                Assert.Equal(Path.Combine(_codexHome, "auth.json"), auth.Path);
                Assert.False(auth.Exists);
                Assert.Equal("json", auth.Language);
                Assert.True(auth.Validation.IsValid);
            }
        );

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ReadCodexUserFileAsync(@"..\config.toml")
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.BackupCodexUserFileAsync(@"C:\Users\Reol\.codex\auth.json")
        );
    }

    [Fact]
    public async Task ValidateCodexUserFileRejectsInvalidTomlAndJsonObject()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var service = new CodexConfigService();

        var toml = service.ValidateCodexUserFile("config", "[notice");
        var jsonArray = service.ValidateCodexUserFile("auth", "[]");
        var jsonSyntax = service.ValidateCodexUserFile("auth", "{");

        Assert.False(toml.IsValid);
        Assert.Contains("TOML", toml.Message, StringComparison.Ordinal);
        Assert.False(jsonArray.IsValid);
        Assert.Contains("JSON 对象", jsonArray.Message, StringComparison.Ordinal);
        Assert.False(jsonSyntax.IsValid);
        Assert.Contains("JSON", jsonSyntax.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveCodexUserFileAsync("auth", "[]", expectedLastWriteTimeUtc: null)
        );
        Assert.False(File.Exists(Path.Combine(_codexHome, "auth.json")));
    }

    [Fact]
    public async Task SaveCodexUserFileAsyncBacksUpExistingFileAndWritesUtf8WithoutBom()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var configPath = Path.Combine(_codexHome, "config.toml");
        await File.WriteAllTextAsync(configPath, "model = \"gpt-5.4\"\n");
        var service = new CodexConfigService();
        var snapshot = await service.ReadCodexUserFileAsync("config");

        var result = await service.SaveCodexUserFileAsync(
            "config",
            "model = \"gpt-5.5\"\n",
            snapshot.LastWriteTimeUtc
        );

        var bytes = await File.ReadAllBytesAsync(configPath);
        var backupContent = await File.ReadAllTextAsync(result.BackupPath!);

        Assert.Equal("config", result.FileId);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains(
            ".codexcliplus-source-backup-",
            Path.GetFileName(result.BackupPath),
            StringComparison.Ordinal
        );
        Assert.Equal("model = \"gpt-5.4\"\n", backupContent);
        Assert.Equal("model = \"gpt-5.5\"\n", await File.ReadAllTextAsync(configPath));
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
        );
    }

    [Fact]
    public async Task SaveCodexUserFileAsyncRejectsExternalTimestampConflict()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        await File.WriteAllTextAsync(authPath, "{\n  \"token\": \"original\"\n}\n");
        var service = new CodexConfigService();
        var snapshot = await service.ReadCodexUserFileAsync("auth");

        await Task.Delay(30);
        await File.WriteAllTextAsync(authPath, "{\n  \"token\": \"external\"\n}\n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveCodexUserFileAsync(
                "auth",
                "{\n  \"token\": \"next\"\n}\n",
                snapshot.LastWriteTimeUtc
            )
        );

        Assert.Contains("外部修改", exception.Message, StringComparison.Ordinal);
        Assert.Equal("{\n  \"token\": \"external\"\n}\n", await File.ReadAllTextAsync(authPath));
        Assert.Empty(Directory.GetFiles(_codexHome, "auth.codexcliplus-source-backup-*.json"));
    }

    [Fact]
    public async Task BackupCodexUserFileAsyncOnlyBacksUpExistingAllowedFiles()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        await File.WriteAllTextAsync(authPath, "{\n  \"token\": \"official\"\n}\n");
        var service = new CodexConfigService();

        var result = await service.BackupCodexUserFileAsync("auth");

        Assert.Equal("auth", result.FileId);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal("{\n  \"token\": \"official\"\n}\n", await File.ReadAllTextAsync(result.BackupPath));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BackupCodexUserFileAsync("config")
        );
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
