using CodexCliPlus.Core.Enums;
using CodexCliPlus.Infrastructure.Codex;

namespace CodexCliPlus.Tests.Codex;

[Collection("CodexConfigService")]
public sealed class CodexAuthSwitchTests : IDisposable
{
    private readonly string _codexHome = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-codex-auth-{Guid.NewGuid():N}"
    );
    private readonly string? _originalCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");

    [Fact]
    public async Task ApplyDesktopModeAsyncWritesDummyAuthForCpaAndBacksUpOfficialAuth()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        await File.WriteAllTextAsync(authPath, "{\n  \"auth_mode\": \"chatgpt\"\n}\n");

        var service = new CodexConfigService();
        await service.ApplyDesktopModeAsync(9318, CodexSourceKind.Cpa);

        var currentAuth = await File.ReadAllTextAsync(authPath);
        var backupAuth = await File.ReadAllTextAsync(service.GetDesktopAuthBackupPath());

        Assert.Contains("\"OPENAI_API_KEY\": \"sk-dummy\"", currentAuth, StringComparison.Ordinal);
        Assert.Contains("\"auth_mode\": \"chatgpt\"", backupAuth, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyDesktopModeAsyncRestoresOfficialAuthFromPreset()
    {
        Directory.CreateDirectory(Path.Combine(_codexHome, "switch-presets", "official"));
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        var presetPath = Path.Combine(_codexHome, "switch-presets", "official", "auth.json");

        await File.WriteAllTextAsync(authPath, "{\n  \"OPENAI_API_KEY\": \"sk-dummy\"\n}\n");
        await File.WriteAllTextAsync(presetPath, "{\n  \"auth_mode\": \"chatgpt\"\n}\n");

        var service = new CodexConfigService();
        await service.ApplyDesktopModeAsync(9318, CodexSourceKind.Official);

        var currentAuth = await File.ReadAllTextAsync(authPath);
        Assert.Contains("\"auth_mode\": \"chatgpt\"", currentAuth, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyDesktopModeAsyncCanRoundTripCpaBackToOfficial()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        await File.WriteAllTextAsync(
            authPath,
            "{\n  \"auth_mode\": \"chatgpt\",\n  \"token\": \"official\"\n}\n"
        );

        var service = new CodexConfigService();
        await service.ApplyDesktopModeAsync(9318, CodexSourceKind.Cpa);
        await service.ApplyDesktopModeAsync(9318, CodexSourceKind.Official);

        var config = await File.ReadAllTextAsync(service.GetUserConfigPath());
        var currentAuth = await File.ReadAllTextAsync(authPath);

        Assert.Contains("profile = \"codexcliplus-official\"", config, StringComparison.Ordinal);
        Assert.Contains("[profiles.codexcliplus-cpa]", config, StringComparison.Ordinal);
        Assert.Contains("\"token\": \"official\"", currentAuth, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"OPENAI_API_KEY\": \"sk-dummy\"",
            currentAuth,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task SwitchCodexRouteAsyncCleansLegacyTopLevelCpaRouteAndRestoresOfficialAuth()
    {
        Directory.CreateDirectory(Path.Combine(_codexHome, "codexcliplus-auth"));
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        var configPath = Path.Combine(_codexHome, "config.toml");
        var service = new CodexConfigService();

        await File.WriteAllTextAsync(
            configPath,
            "model_provider = \"cliproxyapi\"\nbase_url = \"http://127.0.0.1:8317/v1\"\nwire_api = \"responses\"\n[model_providers.cliproxyapi]\nname = \"cliproxyapi-old\"\nbase_url = \"http://127.0.0.1:8317/v1\"\n"
        );
        await File.WriteAllTextAsync(authPath, "{\n  \"OPENAI_API_KEY\": \"sk-dummy\"\n}\n");
        await File.WriteAllTextAsync(
            service.GetDesktopAuthBackupPath(),
            "{\n  \"auth_mode\": \"chatgpt\",\n  \"token\": \"official\"\n}\n"
        );

        var result = await service.SwitchCodexRouteAsync("official", 1327);

        var config = await File.ReadAllTextAsync(configPath);
        var currentAuth = await File.ReadAllTextAsync(authPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("official", result.State.CurrentMode);
        Assert.True(File.Exists(result.ConfigBackupPath));
        Assert.Contains("profile = \"codexcliplus-official\"", config, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:8317/v1", config, StringComparison.Ordinal);
        Assert.DoesNotContain("[model_providers.cpa]", config, StringComparison.Ordinal);
        Assert.Contains("[model_providers.cliproxyapi]", config, StringComparison.Ordinal);
        Assert.Contains("\"token\": \"official\"", currentAuth, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OPENAI_API_KEY\": \"sk-dummy\"", currentAuth, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchCodexRouteAsyncWritesManagedCpaRouteAndDummyAuth()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        await File.WriteAllTextAsync(authPath, "{\n  \"auth_mode\": \"chatgpt\"\n}\n");

        var service = new CodexConfigService();
        var result = await service.SwitchCodexRouteAsync("cpa", 1327);

        var config = await File.ReadAllTextAsync(service.GetUserConfigPath());
        var currentAuth = await File.ReadAllTextAsync(authPath);
        var officialBackup = await File.ReadAllTextAsync(service.GetDesktopAuthBackupPath());

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("cpa", result.State.CurrentMode);
        Assert.Equal("codexcliplus-cpa", result.State.CurrentTargetId);
        Assert.Contains("profile = \"codexcliplus-cpa\"", config, StringComparison.Ordinal);
        Assert.Contains("model_provider = \"codexcliplus-cpa\"", config, StringComparison.Ordinal);
        Assert.Contains("chatgpt_base_url = \"http://127.0.0.1:1327/backend-api\"", config, StringComparison.Ordinal);
        Assert.Contains("base_url = \"http://127.0.0.1:1327/v1\"", config, StringComparison.Ordinal);
        Assert.Contains("[model_providers.codexcliplus-cpa]", config, StringComparison.Ordinal);
        Assert.DoesNotContain("[model_providers.cpa]", config, StringComparison.Ordinal);
        Assert.Contains("\"OPENAI_API_KEY\": \"sk-dummy\"", currentAuth, StringComparison.Ordinal);
        Assert.Contains("\"auth_mode\": \"chatgpt\"", officialBackup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchCodexRouteAsyncFailsToOfficialWhenOfficialAuthIsMissing()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        var configPath = Path.Combine(_codexHome, "config.toml");
        var originalConfig = "profile = \"cpa\"\n";
        var originalAuth = "{\n  \"OPENAI_API_KEY\": \"sk-dummy\"\n}\n";
        await File.WriteAllTextAsync(configPath, originalConfig);
        await File.WriteAllTextAsync(authPath, originalAuth);

        var service = new CodexConfigService();
        var result = await service.SwitchCodexRouteAsync("official", 1327);

        Assert.False(result.Succeeded);
        Assert.Contains("找不到可恢复的官方认证文件", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(originalConfig, await File.ReadAllTextAsync(configPath));
        Assert.Equal(originalAuth, await File.ReadAllTextAsync(authPath));
    }

    [Fact]
    public async Task SwitchCodexRouteAsyncCanRepeatWithoutDuplicateManagedTables()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        await File.WriteAllTextAsync(
            Path.Combine(_codexHome, "auth.json"),
            "{\n  \"auth_mode\": \"chatgpt\"\n}\n"
        );

        var service = new CodexConfigService();
        await service.SwitchCodexRouteAsync("cpa", 1327);
        var result = await service.SwitchCodexRouteAsync("cpa", 1327);

        var config = await File.ReadAllTextAsync(service.GetUserConfigPath());

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, CountOccurrences(config, "[profiles.codexcliplus-cpa]"));
        Assert.Equal(1, CountOccurrences(config, "[model_providers.codexcliplus-cpa]"));
        Assert.Equal(0, CountOccurrences(config, "[model_providers.cpa]"));
    }

    [Fact]
    public async Task SwitchCodexRouteAsyncCanSwitchToConfiguredThirdPartyCpaWithoutOverwritingProvider()
    {
        Directory.CreateDirectory(_codexHome);
        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHome);

        var authPath = Path.Combine(_codexHome, "auth.json");
        var configPath = Path.Combine(_codexHome, "config.toml");
        await File.WriteAllTextAsync(authPath, "{\n  \"auth_mode\": \"chatgpt\"\n}\n");
        await File.WriteAllTextAsync(
            configPath,
            "[model_providers.cliproxyapi]\nname = \"cliproxyapi-old\"\nbase_url = \"http://127.0.0.1:8317/v1\"\nwire_api = \"responses\"\n"
        );

        var service = new CodexConfigService();
        var result = await service.SwitchCodexRouteAsync(
            "third-party-cpa:http://127.0.0.1:8317/v1",
            1327
        );

        var config = await File.ReadAllTextAsync(configPath);
        var currentAuth = await File.ReadAllTextAsync(authPath);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("cpa", result.State.CurrentMode);
        Assert.StartsWith("third-party-cpa:", result.State.CurrentTargetId, StringComparison.Ordinal);
        Assert.Contains("model_provider = \"cliproxyapi\"", config, StringComparison.Ordinal);
        Assert.Contains("[model_providers.cliproxyapi]", config, StringComparison.Ordinal);
        Assert.Contains("name = \"cliproxyapi-old\"", config, StringComparison.Ordinal);
        Assert.Contains("base_url = \"http://127.0.0.1:8317/v1\"", config, StringComparison.Ordinal);
        Assert.Contains("\"OPENAI_API_KEY\": \"sk-dummy\"", currentAuth, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
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
