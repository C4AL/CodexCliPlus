using CPAD.Core.Enums;
using CPAD.Infrastructure.Codex;

namespace CPAD.Tests.Codex;

[Collection("CodexConfigService")]
public sealed class CodexAuthSwitchTests : IDisposable
{
    private readonly string _codexHome = Path.Combine(Path.GetTempPath(), $"cpad-codex-auth-{Guid.NewGuid():N}");
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
        await File.WriteAllTextAsync(authPath, "{\n  \"auth_mode\": \"chatgpt\",\n  \"token\": \"official\"\n}\n");

        var service = new CodexConfigService();
        await service.ApplyDesktopModeAsync(9318, CodexSourceKind.Cpa);
        await service.ApplyDesktopModeAsync(9318, CodexSourceKind.Official);

        var config = await File.ReadAllTextAsync(service.GetUserConfigPath());
        var currentAuth = await File.ReadAllTextAsync(authPath);

        Assert.Contains("profile = \"official\"", config, StringComparison.Ordinal);
        Assert.Contains("[profiles.cpa]", config, StringComparison.Ordinal);
        Assert.Contains("\"token\": \"official\"", currentAuth, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OPENAI_API_KEY\": \"sk-dummy\"", currentAuth, StringComparison.Ordinal);
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
