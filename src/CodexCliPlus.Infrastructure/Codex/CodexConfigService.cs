using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace CodexCliPlus.Infrastructure.Codex;

public sealed class CodexConfigService
{
    private const string ManagedRootStart = "# BEGIN CODEXCLIPLUS MANAGED ROOT";
    private const string ManagedRootEnd = "# END CODEXCLIPLUS MANAGED ROOT";
    private const string ManagedTablesStart = "# BEGIN CODEXCLIPLUS MANAGED TABLES";
    private const string ManagedTablesEnd = "# END CODEXCLIPLUS MANAGED TABLES";
    private const string OfficialMode = "official";
    private const string CpaMode = "cpa";
    private const string CpaProviderName = "cliproxyapi";
    private const string CpaDummyAuthJson = "{\n  \"OPENAI_API_KEY\": \"sk-dummy\"\n}\n";

    private static readonly HashSet<string> RootRoutingKeys =
    [
        "profile",
        "model_provider",
        "base_url",
        "wire_api",
        "requires_openai_auth",
        "chatgpt_base_url",
        "cli_auth_credentials_store",
    ];

    private static readonly HashSet<string> ManagedRoutingTables =
    [
        "profiles.official",
        "profiles.cpa",
        "model_providers.cpa",
        "model_providers.cliproxyapi",
    ];

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance member is part of the dependency-injected Codex configuration service."
    )]
    public string GetUserConfigDirectory()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return codexHome;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex"
        );
    }

    public string GetUserConfigPath()
    {
        return Path.Combine(GetUserConfigDirectory(), "config.toml");
    }

    public string GetUserAuthPath()
    {
        return Path.Combine(GetUserConfigDirectory(), "auth.json");
    }

    public string GetDesktopAuthBackupPath()
    {
        return Path.Combine(GetUserConfigDirectory(), "codexcliplus-auth", "official-auth.json");
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance member is part of the dependency-injected Codex configuration service."
    )]
    public string? GetProjectConfigPath(string? repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return null;
        }

        return Path.Combine(repositoryPath, ".codex", "config.toml");
    }

    public async Task<CodexStatusSnapshot> InspectAsync(
        string? repositoryPath,
        string? executablePath,
        string? version,
        string authenticationState,
        CancellationToken cancellationToken = default
    )
    {
        var configPath = GetUserConfigPath();
        var hasUserConfig = File.Exists(configPath);
        var hasProjectConfig = File.Exists(GetProjectConfigPath(repositoryPath) ?? string.Empty);
        var defaultProfile = "official";
        var effectiveSource = "official";
        string? errorMessage = null;

        if (hasUserConfig)
        {
            try
            {
                var content = await File.ReadAllTextAsync(configPath, cancellationToken);
                if (TryReadProfile(content, out var profile))
                {
                    defaultProfile = profile;
                    effectiveSource = profile;
                }
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
            }
        }

        return new CodexStatusSnapshot
        {
            IsInstalled = !string.IsNullOrWhiteSpace(executablePath),
            ExecutablePath = executablePath,
            Version = version,
            DefaultProfile = defaultProfile,
            HasUserConfig = hasUserConfig,
            HasProjectConfig = hasProjectConfig,
            AuthenticationState = authenticationState,
            EffectiveSource = effectiveSource,
            ErrorMessage = errorMessage,
        };
    }

    public async Task ApplyProfilesAsync(
        int backendPort,
        CodexSourceKind defaultSource,
        CancellationToken cancellationToken = default
    )
    {
        var configDirectory = GetUserConfigDirectory();
        var configPath = GetUserConfigPath();
        Directory.CreateDirectory(configDirectory);

        var existingContent = File.Exists(configPath)
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
            : string.Empty;

        var backupPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(existingContent))
        {
            backupPath = await BackupAsync(existingContent, configDirectory, cancellationToken);
        }

        var mergedContent = MergeManagedBlocks(existingContent, backendPort, defaultSource);
        if (!TryParseTomlTable(mergedContent, out _, out var validationError))
        {
            throw new InvalidOperationException(
                $"Codex configuration validation failed before write: {validationError}"
            );
        }

        try
        {
            await File.WriteAllTextAsync(
                configPath,
                mergedContent,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                File.Copy(backupPath, configPath, overwrite: true);
            }

            throw;
        }
    }

    public async Task ApplyDesktopModeAsync(
        int backendPort,
        CodexSourceKind defaultSource,
        CancellationToken cancellationToken = default
    )
    {
        await ApplyProfilesAsync(backendPort, defaultSource, cancellationToken);
        await ApplyAuthAsync(defaultSource, cancellationToken);
    }

    public async Task<CodexRouteState> GetCodexRouteStateAsync(
        CancellationToken cancellationToken = default
    )
    {
        var configPath = GetUserConfigPath();
        var authPath = GetUserAuthPath();
        var detection = await DetectRouteAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(detection.ErrorMessage))
        {
            return new CodexRouteState
            {
                CurrentMode = "unknown",
                TargetMode = null,
                ConfigPath = configPath,
                AuthPath = authPath,
                CanSwitch = false,
                StatusMessage = $"Codex 配置无法解析：{detection.ErrorMessage}",
            };
        }

        var currentMode = detection.Mode;
        var targetMode = currentMode == CpaMode ? OfficialMode : CpaMode;
        var canSwitch = true;
        string statusMessage;

        if (currentMode == CpaMode)
        {
            var officialAuth = await FindOfficialAuthCandidateAsync(
                includeCurrentAuth: true,
                cancellationToken
            );
            canSwitch = officialAuth is not null;
            statusMessage = canSwitch
                ? "当前使用 CPA 模式。"
                : "缺少可恢复的官方认证文件，无法切换到官方模式。";
        }
        else if (currentMode == OfficialMode)
        {
            statusMessage = detection.ConfigExists
                ? "当前使用官方模式。"
                : "未找到 Codex 配置，当前按官方模式处理。";
        }
        else
        {
            targetMode = CpaMode;
            statusMessage = "当前模式无法识别，可切换到 CPA 模式重写托管路由。";
        }

        return new CodexRouteState
        {
            CurrentMode = currentMode,
            TargetMode = targetMode,
            ConfigPath = configPath,
            AuthPath = authPath,
            CanSwitch = canSwitch,
            StatusMessage = statusMessage,
        };
    }

    public async Task<CodexRouteSwitchResult> SwitchCodexRouteAsync(
        string targetMode,
        int backendPort = AppConstants.DefaultBackendPort,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedTargetMode = NormalizeRouteMode(targetMode);
        if (normalizedTargetMode is null)
        {
            var state = await GetCodexRouteStateAsync(cancellationToken);
            return new CodexRouteSwitchResult
            {
                Succeeded = false,
                State = state,
                ErrorMessage = "目标模式无效，只能切换到官方模式或 CPA 模式。",
            };
        }

        OfficialAuthCandidate? officialAuth = null;
        if (normalizedTargetMode == OfficialMode)
        {
            officialAuth = await FindOfficialAuthCandidateAsync(
                includeCurrentAuth: true,
                cancellationToken
            );
            if (officialAuth is null)
            {
                var state = await GetCodexRouteStateAsync(cancellationToken);
                return new CodexRouteSwitchResult
                {
                    Succeeded = false,
                    State = state,
                    ErrorMessage = "找不到可恢复的官方认证文件，未修改 Codex 配置。",
                };
            }
        }

        var source = normalizedTargetMode == CpaMode
            ? CodexSourceKind.Cpa
            : CodexSourceKind.Official;
        var configDirectory = GetUserConfigDirectory();
        var configPath = GetUserConfigPath();
        var authPath = GetUserAuthPath();
        Directory.CreateDirectory(configDirectory);

        var configExisted = File.Exists(configPath);
        var originalConfig = configExisted
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
            : string.Empty;
        var authExisted = File.Exists(authPath);
        var originalAuth = authExisted
            ? await File.ReadAllTextAsync(authPath, cancellationToken)
            : string.Empty;

        var mergedContent = MergeManagedBlocks(originalConfig, backendPort, source);
        if (!TryParseTomlTable(mergedContent, out _, out var validationError))
        {
            var state = await GetCodexRouteStateAsync(cancellationToken);
            return new CodexRouteSwitchResult
            {
                Succeeded = false,
                State = state,
                ErrorMessage = $"Codex 配置校验失败，未写入：{validationError}",
            };
        }

        string? configBackupPath = null;
        string? authBackupPath = null;
        string? officialAuthBackupPath = null;

        try
        {
            if (configExisted)
            {
                configBackupPath = await BackupFileContentAsync(
                    originalConfig,
                    configDirectory,
                    "config",
                    "toml",
                    cancellationToken
                );
            }

            if (authExisted)
            {
                authBackupPath = await BackupFileContentAsync(
                    originalAuth,
                    configDirectory,
                    "auth",
                    "json",
                    cancellationToken
                );
            }

            await WriteUtf8NoBomAsync(configPath, mergedContent, cancellationToken);

            if (normalizedTargetMode == CpaMode)
            {
                officialAuthBackupPath = await BackupOfficialAuthIfNeededAsync(cancellationToken);
                await WriteUtf8NoBomAsync(authPath, CpaDummyAuthJson, cancellationToken);
            }
            else
            {
                await WriteUtf8NoBomAsync(authPath, officialAuth!.Content, cancellationToken);
            }

            var state = await GetCodexRouteStateAsync(cancellationToken);
            return new CodexRouteSwitchResult
            {
                Succeeded = true,
                State = state,
                ConfigBackupPath = configBackupPath,
                AuthBackupPath = authBackupPath,
                OfficialAuthBackupPath = officialAuthBackupPath,
            };
        }
        catch (Exception exception)
        {
            await RestoreFileAsync(configPath, configExisted, originalConfig);
            await RestoreFileAsync(authPath, authExisted, originalAuth);

            var state = await GetCodexRouteStateAsync(CancellationToken.None);
            return new CodexRouteSwitchResult
            {
                Succeeded = false,
                State = state,
                ConfigBackupPath = configBackupPath,
                AuthBackupPath = authBackupPath,
                OfficialAuthBackupPath = officialAuthBackupPath,
                ErrorMessage = $"切换失败，已回滚原文件：{exception.Message}",
            };
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance member is part of the dependency-injected Codex configuration service."
    )]
    public string BuildLaunchCommand(CodexSourceKind source, string? repositoryPath)
    {
        var builder = new StringBuilder();
        builder.Append("codex --profile ");
        builder.Append(source == CodexSourceKind.Cpa ? "cpa" : "official");

        if (!string.IsNullOrWhiteSpace(repositoryPath))
        {
            builder.Append(" -C ");
            builder.Append('"');
            builder.Append(repositoryPath);
            builder.Append('"');
        }

        return builder.ToString();
    }

    public static string GetSourceName(CodexSourceKind source)
    {
        return source == CodexSourceKind.Cpa ? CpaMode : OfficialMode;
    }

    private async Task ApplyAuthAsync(CodexSourceKind source, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetUserConfigDirectory());

        if (source == CodexSourceKind.Cpa)
        {
            await BackupOfficialAuthIfNeededAsync(cancellationToken);
            await WriteUtf8NoBomAsync(GetUserAuthPath(), CpaDummyAuthJson, cancellationToken);
            return;
        }

        var officialAuth = await FindOfficialAuthCandidateAsync(
            includeCurrentAuth: false,
            cancellationToken
        );
        if (officialAuth is not null)
        {
            await WriteUtf8NoBomAsync(GetUserAuthPath(), officialAuth.Content, cancellationToken);
        }
    }

    private static string MergeManagedBlocks(
        string content,
        int backendPort,
        CodexSourceKind defaultSource
    )
    {
        var cleaned = StripRoutingFragments(content);
        var rootBlock = BuildRootBlock(defaultSource);
        var tablesBlock = BuildTablesBlock(backendPort);

        var builder = new StringBuilder();
        builder.AppendLine(rootBlock);

        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            builder.AppendLine(cleaned.Trim());
            builder.AppendLine();
        }

        builder.AppendLine(tablesBlock);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string StripRoutingFragments(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var withoutBom = content.TrimStart('\uFEFF');
        var withoutRoot = Regex.Replace(
            withoutBom,
            $"{Regex.Escape(ManagedRootStart)}[\\s\\S]*?{Regex.Escape(ManagedRootEnd)}\\s*",
            string.Empty
        );

        var withoutManagedTables = Regex
            .Replace(
                withoutRoot,
                $"{Regex.Escape(ManagedTablesStart)}[\\s\\S]*?{Regex.Escape(ManagedTablesEnd)}\\s*",
                string.Empty
            )
            .Trim();

        var builder = new StringBuilder();
        var inRoot = true;
        var skippingManagedTable = false;
        foreach (var rawLine in withoutManagedTables.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (TryReadTomlTableHeader(line, out var tableName))
            {
                inRoot = false;
                skippingManagedTable = IsManagedRoutingTable(tableName);
                if (skippingManagedTable)
                {
                    continue;
                }
            }
            else if (skippingManagedTable)
            {
                continue;
            }

            if (inRoot && IsRootRoutingKeyLine(line))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        return Regex.Replace(builder.ToString().Trim(), @"\n{3,}", "\n\n");
    }

    private static bool IsRootRoutingKeyLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is '#' or '[')
        {
            return false;
        }

        var equalsIndex = trimmed.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex <= 0)
        {
            return false;
        }

        var key = trimmed[..equalsIndex].Trim();
        return RootRoutingKeys.Contains(key);
    }

    private static bool TryReadTomlTableHeader(string line, out string tableName)
    {
        tableName = string.Empty;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('[') || trimmed.StartsWith("[[", StringComparison.Ordinal))
        {
            return false;
        }

        var closingIndex = trimmed.IndexOf(']', StringComparison.Ordinal);
        if (closingIndex <= 1)
        {
            return false;
        }

        tableName = trimmed[1..closingIndex].Trim();
        return !string.IsNullOrWhiteSpace(tableName);
    }

    private static bool IsManagedRoutingTable(string tableName)
    {
        var normalized = tableName.Trim();
        return ManagedRoutingTables.Contains(normalized)
            || ManagedRoutingTables.Any(table =>
                normalized.StartsWith($"{table}.", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static string BuildRootBlock(CodexSourceKind defaultSource)
    {
        var profile = GetSourceName(defaultSource);
        return $"{ManagedRootStart}{Environment.NewLine}"
            + $"profile = \"{profile}\"{Environment.NewLine}"
            + $"{ManagedRootEnd}";
    }

    private static string BuildTablesBlock(int backendPort)
    {
        var cpaBaseUrl = $"http://127.0.0.1:{backendPort}";

        return $"{ManagedTablesStart}{Environment.NewLine}"
            + "[profiles.official]"
            + Environment.NewLine
            + "model_provider = \"openai\""
            + Environment.NewLine
            + "chatgpt_base_url = \"https://chatgpt.com/backend-api\""
            + Environment.NewLine
            + "cli_auth_credentials_store = \"file\""
            + Environment.NewLine
            + Environment.NewLine
            + "[profiles.cpa]"
            + Environment.NewLine
            + $"model_provider = \"{CpaProviderName}\""
            + Environment.NewLine
            + $"chatgpt_base_url = \"{cpaBaseUrl}/backend-api\""
            + Environment.NewLine
            + "cli_auth_credentials_store = \"file\""
            + Environment.NewLine
            + Environment.NewLine
            + $"[model_providers.{CpaProviderName}]"
            + Environment.NewLine
            + $"name = \"{CpaProviderName}\""
            + Environment.NewLine
            + $"base_url = \"{cpaBaseUrl}/v1\""
            + Environment.NewLine
            + "wire_api = \"responses\""
            + Environment.NewLine
            + "requires_openai_auth = true"
            + Environment.NewLine
            + $"{ManagedTablesEnd}";
    }

    private async Task<RouteDetectionResult> DetectRouteAsync(CancellationToken cancellationToken)
    {
        var configPath = GetUserConfigPath();
        var authPath = GetUserAuthPath();
        var authIsDummy =
            File.Exists(authPath)
            && IsCpaDummyAuth(await File.ReadAllTextAsync(authPath, cancellationToken));

        if (!File.Exists(configPath))
        {
            return new RouteDetectionResult(
                authIsDummy ? CpaMode : OfficialMode,
                ConfigExists: false,
                ErrorMessage: null
            );
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        if (!TryParseTomlTable(content, out var model, out var errorMessage) || model is null)
        {
            return new RouteDetectionResult("unknown", ConfigExists: true, errorMessage);
        }

        var mode = ResolveRouteMode(model, authIsDummy);
        return new RouteDetectionResult(mode, ConfigExists: true, ErrorMessage: null);
    }

    private static string ResolveRouteMode(TomlTable model, bool authIsDummy)
    {
        if (TryReadString(model, "profile", out var profile))
        {
            var profileMode = NormalizeRouteMode(profile);
            if (profileMode is not null)
            {
                return profileMode;
            }

            if (TryReadProfileProvider(model, profile, out var profileProvider))
            {
                var providerMode = ResolveProviderMode(profileProvider);
                if (providerMode is not null)
                {
                    return providerMode;
                }
            }
        }

        if (TryReadString(model, "model_provider", out var provider))
        {
            var providerMode = ResolveProviderMode(provider);
            if (providerMode is not null)
            {
                return providerMode;
            }
        }

        if (authIsDummy)
        {
            return CpaMode;
        }

        return OfficialMode;
    }

    private static string? NormalizeRouteMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized == OfficialMode || normalized == CpaMode)
        {
            return normalized;
        }

        return null;
    }

    private static string? ResolveProviderMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized == "openai" || normalized == OfficialMode)
        {
            return OfficialMode;
        }

        if (normalized == CpaMode || normalized == CpaProviderName)
        {
            return CpaMode;
        }

        return null;
    }

    private static bool TryReadProfileProvider(
        TomlTable model,
        string profile,
        out string provider
    )
    {
        provider = string.Empty;
        if (
            !model.TryGetValue("profiles", out var profilesValue)
            || profilesValue is not TomlTable profiles
            || !profiles.TryGetValue(profile, out var profileValue)
            || profileValue is not TomlTable profileTable
        )
        {
            return false;
        }

        return TryReadString(profileTable, "model_provider", out provider);
    }

    private static bool TryReadString(TomlTable model, string key, out string value)
    {
        value = string.Empty;
        if (
            model.TryGetValue(key, out var rawValue)
            && rawValue is string text
            && !string.IsNullOrWhiteSpace(text)
        )
        {
            value = text.Trim();
            return true;
        }

        return false;
    }

    private static bool TryReadProfile(string content, out string profile)
    {
        profile = "official";
        if (!TryParseTomlTable(content, out var model, out _) || model is null)
        {
            return false;
        }

        if (TryReadString(model, "profile", out var profileValue))
        {
            profile = profileValue;
            return true;
        }

        var source = ResolveRouteMode(model, authIsDummy: false);
        if (source == OfficialMode || source == CpaMode)
        {
            profile = source;
            return true;
        }

        return false;
    }

    private static bool TryParseTomlTable(
        string content,
        out TomlTable? model,
        out string errorMessage
    )
    {
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(content);
            if (model is null)
            {
                errorMessage = "TOML document did not produce a table model.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (TomlException exception)
        {
            model = null;
            errorMessage = exception.Message;
            return false;
        }
    }

    private static async Task<string> BackupAsync(
        string existingContent,
        string configDirectory,
        CancellationToken cancellationToken
    )
    {
        return await BackupFileContentAsync(
            existingContent,
            configDirectory,
            "config",
            "toml",
            cancellationToken
        );
    }

    private static async Task<string> BackupFileContentAsync(
        string content,
        string directory,
        string name,
        string extension,
        CancellationToken cancellationToken
    )
    {
        var backupPath = Path.Combine(
            directory,
            $"{name}.codexcliplus-backup-{DateTimeOffset.Now:yyyyMMddHHmmssfff}.{extension}"
        );
        await WriteUtf8NoBomAsync(backupPath, content, cancellationToken);
        return backupPath;
    }

    private async Task<string?> BackupOfficialAuthIfNeededAsync(CancellationToken cancellationToken)
    {
        var authPath = GetUserAuthPath();
        if (!File.Exists(authPath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(authPath, cancellationToken);
        if (IsCpaDummyAuth(content))
        {
            return null;
        }

        var backupPath = GetDesktopAuthBackupPath();
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        await WriteUtf8NoBomAsync(backupPath, content, cancellationToken);
        return backupPath;
    }

    private async Task<OfficialAuthCandidate?> FindOfficialAuthCandidateAsync(
        bool includeCurrentAuth,
        CancellationToken cancellationToken
    )
    {
        foreach (var candidate in GetOfficialAuthCandidates(includeCurrentAuth))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(candidate, cancellationToken);
            if (IsCpaDummyAuth(content))
            {
                continue;
            }

            return new OfficialAuthCandidate(candidate, content);
        }

        return null;
    }

    private IEnumerable<string> GetOfficialAuthCandidates(bool includeCurrentAuth)
    {
        if (includeCurrentAuth)
        {
            yield return GetUserAuthPath();
        }

        yield return GetDesktopAuthBackupPath();
        yield return Path.Combine(
            GetUserConfigDirectory(),
            "switch-presets",
            "official",
            "auth.json"
        );
        yield return Path.Combine(GetUserConfigDirectory(), "bridge-chatgpt-auth.json");
    }

    private static async Task WriteUtf8NoBomAsync(
        string path,
        string content,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
    }

    private static async Task RestoreFileAsync(string path, bool existed, string content)
    {
        if (existed)
        {
            await WriteUtf8NoBomAsync(path, content, CancellationToken.None);
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsCpaDummyAuth(string content)
    {
        return content.Contains("\"OPENAI_API_KEY\": \"sk-dummy\"", StringComparison.Ordinal);
    }

    private sealed record RouteDetectionResult(
        string Mode,
        bool ConfigExists,
        string? ErrorMessage
    );

    private sealed record OfficialAuthCandidate(string Path, string Content);
}
