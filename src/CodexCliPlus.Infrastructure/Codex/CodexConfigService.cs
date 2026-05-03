using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
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
    private const string OfficialTargetId = "official";
    private const string ManagedCpaTargetId = "codexcliplus-cpa";
    private const string ThirdPartyTargetPrefix = "third-party-cpa:";
    private const string ManagedOfficialProfileName = "codexcliplus-official";
    private const string ManagedCpaProfileName = "codexcliplus-cpa";
    private const string ManagedCpaProviderName = "codexcliplus-cpa";
    private const string ManagedExternalProfilePrefix = "codexcliplus-external-cpa-";
    private const string ManagedExternalProviderPrefix = "codexcliplus-external-cpa-";
    private const string LegacyCpaProviderName = "cliproxyapi";
    private const string CpaDummyAuthJson = "{\n  \"OPENAI_API_KEY\": \"sk-dummy\"\n}\n";
    private static readonly TimeSpan RouteProbeTimeout = TimeSpan.FromMilliseconds(450);

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
        return await GetCodexRouteStateAsync(AppConstants.DefaultBackendPort, cancellationToken);
    }

    public async Task<CodexRouteState> GetCodexRouteStateAsync(
        int backendPort,
        CancellationToken cancellationToken = default
    )
    {
        var configPath = GetUserConfigPath();
        var authPath = GetUserAuthPath();
        var detection = await DetectRouteAsync(backendPort, cancellationToken);
        var officialAuth = await FindOfficialAuthCandidateAsync(
            includeCurrentAuth: true,
            cancellationToken
        );
        var targets = await BuildRouteTargetsAsync(
            detection,
            backendPort,
            officialAuth is not null,
            cancellationToken
        );

        if (!string.IsNullOrWhiteSpace(detection.ErrorMessage))
        {
            return new CodexRouteState
            {
                CurrentMode = "unknown",
                TargetMode = null,
                CurrentTargetId = null,
                CurrentLabel = "检测失败",
                Targets = targets,
                ConfigPath = configPath,
                AuthPath = authPath,
                CanSwitch = false,
                StatusMessage = $"Codex 配置无法解析：{detection.ErrorMessage}",
            };
        }

        var currentMode = detection.Mode;
        var targetMode = currentMode == CpaMode ? OfficialMode : CpaMode;
        var currentTarget = targets.FirstOrDefault(target => target.IsCurrent);
        var legacyTargetId = targetMode == OfficialMode ? OfficialTargetId : ManagedCpaTargetId;
        var canSwitch = targets.Any(target =>
            string.Equals(target.Id, legacyTargetId, StringComparison.OrdinalIgnoreCase)
            && !target.IsCurrent
            && target.CanSwitch
        );
        string statusMessage;

        if (currentMode == CpaMode && detection.IsManagedCpa)
        {
            statusMessage = officialAuth is not null
                ? "当前使用 CodexCliPlus CPA。"
                : "缺少可恢复的官方认证文件，无法切换到官方模式。";
        }
        else if (currentMode == CpaMode)
        {
            statusMessage = officialAuth is not null
                ? "当前使用第三方 CPA。"
                : "当前使用第三方 CPA；缺少可恢复的官方认证文件，无法切换到官方模式。";
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
            statusMessage = "当前模式无法识别，可切换到 CodexCliPlus CPA。";
        }

        return new CodexRouteState
        {
            CurrentMode = currentMode,
            TargetMode = targetMode,
            CurrentTargetId = currentTarget?.Id,
            CurrentLabel = currentTarget?.Label ?? GetModeLabel(currentMode),
            Targets = targets,
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
        var selectedTarget = await ResolveSwitchTargetAsync(
            targetMode,
            backendPort,
            cancellationToken
        );
        if (selectedTarget is null)
        {
            var state = await GetCodexRouteStateAsync(backendPort, cancellationToken);
            return new CodexRouteSwitchResult
            {
                Succeeded = false,
                State = state,
                ErrorMessage = "目标路由无效，只能切换到官方模式或可用的 CPA 路由。",
            };
        }

        OfficialAuthCandidate? officialAuth = null;
        if (selectedTarget.Mode == OfficialMode)
        {
            officialAuth = await FindOfficialAuthCandidateAsync(
                includeCurrentAuth: true,
                cancellationToken
            );
            if (officialAuth is null)
            {
                var state = await GetCodexRouteStateAsync(backendPort, cancellationToken);
                return new CodexRouteSwitchResult
                {
                    Succeeded = false,
                    State = state,
                    ErrorMessage = "找不到可恢复的官方认证文件，未修改 Codex 配置。",
                };
            }
        }

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

        var mergedContent = MergeManagedBlocks(originalConfig, backendPort, selectedTarget);
        if (!TryParseTomlTable(mergedContent, out _, out var validationError))
        {
            var state = await GetCodexRouteStateAsync(backendPort, cancellationToken);
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

            if (selectedTarget.Mode == CpaMode)
            {
                officialAuthBackupPath = await BackupOfficialAuthIfNeededAsync(cancellationToken);
                await WriteUtf8NoBomAsync(authPath, CpaDummyAuthJson, cancellationToken);
            }
            else
            {
                await WriteUtf8NoBomAsync(authPath, officialAuth!.Content, cancellationToken);
            }

            var state = await GetCodexRouteStateAsync(backendPort, cancellationToken);
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

            var state = await GetCodexRouteStateAsync(backendPort, CancellationToken.None);
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
        return MergeManagedBlocks(
            content,
            backendPort,
            defaultSource == CodexSourceKind.Cpa
                ? CreateManagedCpaTarget(backendPort)
                : CreateOfficialTarget(isCurrent: false, officialAuthAvailable: true)
        );
    }

    private static string MergeManagedBlocks(
        string content,
        int backendPort,
        CodexRouteTarget selectedTarget
    )
    {
        var cleaned = StripRoutingFragments(content);
        var rootBlock = BuildRootBlock(selectedTarget);
        var tablesBlock = BuildTablesBlock(backendPort, selectedTarget);

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
        foreach (var rawLine in withoutManagedTables.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (TryReadTomlTableHeader(line, out var tableName))
            {
                inRoot = false;
                _ = tableName;
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

    private static string BuildRootBlock(CodexRouteTarget selectedTarget)
    {
        var builder = new StringBuilder();
        builder.AppendLine(ManagedRootStart);

        if (selectedTarget.Mode == OfficialMode)
        {
            builder.AppendLine("profile = \"" + ManagedOfficialProfileName + "\"");
        }
        else if (!string.IsNullOrWhiteSpace(selectedTarget.ProfileName))
        {
            builder.AppendLine(
                "profile = \"" + EscapeTomlString(selectedTarget.ProfileName) + "\""
            );
        }
        else if (!string.IsNullOrWhiteSpace(selectedTarget.ProviderName))
        {
            builder.AppendLine(
                "model_provider = \"" + EscapeTomlString(selectedTarget.ProviderName) + "\""
            );
            if (!string.IsNullOrWhiteSpace(selectedTarget.BaseUrl))
            {
                builder.AppendLine(
                    "base_url = \"" + EscapeTomlString(selectedTarget.BaseUrl) + "\""
                );
                builder.AppendLine("wire_api = \"responses\"");
            }
        }
        else
        {
            builder.AppendLine("profile = \"" + ManagedCpaProfileName + "\"");
        }

        builder.Append(ManagedRootEnd);
        return builder.ToString();
    }

    private static string BuildTablesBlock(int backendPort, CodexRouteTarget selectedTarget)
    {
        var cpaBaseUrl = $"http://127.0.0.1:{backendPort}";
        var builder = new StringBuilder();
        builder.AppendLine(ManagedTablesStart);
        builder.AppendLine("[profiles." + ManagedOfficialProfileName + "]");
        builder.AppendLine("model_provider = \"openai\"");
        builder.AppendLine("chatgpt_base_url = \"https://chatgpt.com/backend-api\"");
        builder.AppendLine("cli_auth_credentials_store = \"file\"");
        builder.AppendLine();
        builder.AppendLine("[profiles." + ManagedCpaProfileName + "]");
        builder.AppendLine("model_provider = \"" + ManagedCpaProviderName + "\"");
        builder.AppendLine("chatgpt_base_url = \"" + cpaBaseUrl + "/backend-api\"");
        builder.AppendLine("cli_auth_credentials_store = \"file\"");
        builder.AppendLine();
        builder.AppendLine("[model_providers." + ManagedCpaProviderName + "]");
        builder.AppendLine("name = \"" + ManagedCpaProviderName + "\"");
        builder.AppendLine("base_url = \"" + cpaBaseUrl + "/v1\"");
        builder.AppendLine("wire_api = \"responses\"");
        builder.AppendLine("requires_openai_auth = true");

        if (
            selectedTarget.Kind == "third-party-cpa"
            && IsManagedExternalProvider(selectedTarget.ProviderName)
            && !string.IsNullOrWhiteSpace(selectedTarget.BaseUrl)
            && !string.IsNullOrWhiteSpace(selectedTarget.ProfileName)
        )
        {
            var providerName = EscapeTomlString(selectedTarget.ProviderName!);
            var profileName = EscapeTomlString(selectedTarget.ProfileName!);
            var baseUrl = EscapeTomlString(selectedTarget.BaseUrl!);
            var backendBaseUrl = NormalizeBackendBaseUrl(selectedTarget.BaseUrl!);

            builder.AppendLine();
            builder.AppendLine("[profiles." + profileName + "]");
            builder.AppendLine("model_provider = \"" + providerName + "\"");
            if (!string.IsNullOrWhiteSpace(backendBaseUrl))
            {
                builder.AppendLine(
                    "chatgpt_base_url = \""
                        + EscapeTomlString(backendBaseUrl + "/backend-api")
                        + "\""
                );
            }
            builder.AppendLine("cli_auth_credentials_store = \"file\"");
            builder.AppendLine();
            builder.AppendLine("[model_providers." + providerName + "]");
            builder.AppendLine("name = \"" + providerName + "\"");
            builder.AppendLine("base_url = \"" + baseUrl + "\"");
            builder.AppendLine("wire_api = \"responses\"");
            builder.AppendLine("requires_openai_auth = true");
        }

        builder.Append(ManagedTablesEnd);
        return builder.ToString();
    }

    private static async Task<IReadOnlyList<CodexRouteTarget>> BuildRouteTargetsAsync(
        RouteDetectionResult detection,
        int backendPort,
        bool officialAuthAvailable,
        CancellationToken cancellationToken
    )
    {
        var targets = new Dictionary<string, CodexRouteTarget>(StringComparer.OrdinalIgnoreCase);
        AddRouteTarget(
            targets,
            CreateOfficialTarget(
                detection.CurrentTargetId == OfficialTargetId,
                officialAuthAvailable
            )
        );
        AddRouteTarget(
            targets,
            CreateManagedCpaTarget(backendPort, detection.CurrentTargetId == ManagedCpaTargetId)
        );

        if (detection.Model is not null)
        {
            AddConfiguredRouteTargets(targets, detection.Model, backendPort);
        }

        if (
            detection.Mode == CpaMode
            && !detection.IsManagedCpa
            && !string.IsNullOrWhiteSpace(detection.BaseUrl)
        )
        {
            AddRouteTarget(
                targets,
                CreateThirdPartyTarget(
                    detection.BaseUrl,
                    detection.ProviderName,
                    detection.ProfileName,
                    isCurrent: true
                )
            );
        }

        await AddLoopbackProbeTargetsAsync(targets, backendPort, cancellationToken);

        return targets
            .Values.Select(target =>
                target with
                {
                    IsCurrent = string.Equals(
                        target.Id,
                        detection.CurrentTargetId,
                        StringComparison.OrdinalIgnoreCase
                    ),
                }
            )
            .OrderBy(target => GetTargetOrder(target))
            .ThenBy(target => target.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<CodexRouteTarget?> ResolveSwitchTargetAsync(
        string? targetId,
        int backendPort,
        CancellationToken cancellationToken
    )
    {
        var normalizedTargetId = targetId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTargetId))
        {
            return null;
        }

        if (NormalizeRouteMode(normalizedTargetId) == OfficialMode)
        {
            normalizedTargetId = OfficialTargetId;
        }
        else if (NormalizeRouteMode(normalizedTargetId) == CpaMode)
        {
            normalizedTargetId = ManagedCpaTargetId;
        }

        var state = await GetCodexRouteStateAsync(backendPort, cancellationToken);
        var selectedTarget = state.Targets.FirstOrDefault(target =>
            string.Equals(target.Id, normalizedTargetId, StringComparison.OrdinalIgnoreCase)
        );
        if (selectedTarget is not null)
        {
            return selectedTarget;
        }

        if (
            normalizedTargetId.StartsWith(ThirdPartyTargetPrefix, StringComparison.OrdinalIgnoreCase)
        )
        {
            var baseUrl = NormalizeCpaBaseUrl(normalizedTargetId[ThirdPartyTargetPrefix.Length..]);
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                return CreateThirdPartyTarget(
                    baseUrl,
                    BuildManagedExternalProviderName(baseUrl),
                    BuildManagedExternalProfileName(baseUrl),
                    isCurrent: false
                );
            }
        }

        return null;
    }

    private static void AddConfiguredRouteTargets(
        IDictionary<string, CodexRouteTarget> targets,
        TomlTable model,
        int backendPort
    )
    {
        if (TryGetTable(model, "model_providers", out var providers))
        {
            foreach (var entry in providers)
            {
                if (
                    entry.Value is not TomlTable providerTable
                    || !TryReadString(providerTable, "base_url", out var providerBaseUrl)
                    || IsManagedCpaProvider(entry.Key)
                )
                {
                    continue;
                }

                AddThirdPartyTarget(
                    targets,
                    providerBaseUrl,
                    entry.Key,
                    FindProfileForProvider(model, entry.Key)
                );
            }
        }

        if (TryGetTable(model, "profiles", out var profiles))
        {
            foreach (var entry in profiles)
            {
                if (entry.Value is not TomlTable profileTable)
                {
                    continue;
                }

                var profileName = entry.Key;
                if (NormalizeRouteMode(profileName) == OfficialMode || IsManagedCpaProfile(profileName))
                {
                    continue;
                }

                TryReadString(profileTable, "model_provider", out var profileProvider);
                if (IsManagedCpaProvider(profileProvider))
                {
                    continue;
                }

                if (!TryReadString(profileTable, "base_url", out var profileBaseUrl))
                {
                    if (
                        string.IsNullOrWhiteSpace(profileProvider)
                        || !TryReadProviderBaseUrl(model, profileProvider, out profileBaseUrl)
                    )
                    {
                        continue;
                    }
                }

                AddThirdPartyTarget(targets, profileBaseUrl, profileProvider, profileName);
            }
        }

        if (TryReadString(model, "base_url", out var rootBaseUrl))
        {
            TryReadString(model, "model_provider", out var rootProvider);
            if (!IsManagedCpaProvider(rootProvider))
            {
                AddThirdPartyTarget(targets, rootBaseUrl, rootProvider, profileName: null);
            }
        }

        _ = backendPort;
    }

    private static async Task AddLoopbackProbeTargetsAsync(
        IDictionary<string, CodexRouteTarget> targets,
        int backendPort,
        CancellationToken cancellationToken
    )
    {
        var knownPorts = targets
            .Values.Select(target => target.Port)
            .Where(port => port is not null)
            .Select(port => port!.Value)
            .Append(backendPort)
            .ToHashSet();
        var ports = GetLoopbackListeningPorts()
            .Where(port => !knownPorts.Contains(port))
            .OrderBy(port => port)
            .Take(48)
            .ToArray();
        if (ports.Length == 0)
        {
            return;
        }

        using var httpClient = new HttpClient { Timeout = RouteProbeTimeout };
        var probeTasks = ports
            .Select(async port =>
            {
                var detected = await ProbeLoopbackCpaAsync(httpClient, port, cancellationToken);
                return (Port: port, Detected: detected);
            })
            .ToArray();

        foreach (var result in await Task.WhenAll(probeTasks))
        {
            if (!result.Detected)
            {
                continue;
            }

            var baseUrl = $"http://127.0.0.1:{result.Port}/v1";
            AddRouteTarget(
                targets,
                CreateThirdPartyTarget(
                    baseUrl,
                    BuildManagedExternalProviderName(baseUrl),
                    BuildManagedExternalProfileName(baseUrl),
                    isCurrent: false
                )
            );
        }
    }

    private static void AddThirdPartyTarget(
        IDictionary<string, CodexRouteTarget> targets,
        string baseUrl,
        string? providerName,
        string? profileName
    )
    {
        AddRouteTarget(
            targets,
            CreateThirdPartyTarget(baseUrl, providerName, profileName, isCurrent: false)
        );
    }

    private static void AddRouteTarget(
        IDictionary<string, CodexRouteTarget> targets,
        CodexRouteTarget target
    )
    {
        if (string.IsNullOrWhiteSpace(target.Id))
        {
            return;
        }

        if (!targets.TryGetValue(target.Id, out var existing))
        {
            targets[target.Id] = target;
            return;
        }

        targets[target.Id] = existing with
        {
            ProfileName = string.IsNullOrWhiteSpace(existing.ProfileName)
                ? target.ProfileName
                : existing.ProfileName,
            ProviderName = string.IsNullOrWhiteSpace(existing.ProviderName)
                ? target.ProviderName
                : existing.ProviderName,
            Port = existing.Port ?? target.Port,
            StatusMessage = existing.StatusMessage ?? target.StatusMessage,
            CanSwitch = existing.CanSwitch || target.CanSwitch,
        };
    }

    private static CodexRouteTarget CreateOfficialTarget(
        bool isCurrent,
        bool officialAuthAvailable
    )
    {
        return new CodexRouteTarget
        {
            Id = OfficialTargetId,
            Mode = OfficialMode,
            Kind = "official",
            Label = "官方 Codex",
            ProfileName = ManagedOfficialProfileName,
            IsCurrent = isCurrent,
            CanSwitch = isCurrent || officialAuthAvailable,
            StatusMessage = officialAuthAvailable || isCurrent
                ? "切换到官方 Codex。"
                : "缺少可恢复的官方认证文件，无法切换到官方模式。",
        };
    }

    private static CodexRouteTarget CreateManagedCpaTarget(int backendPort, bool isCurrent = false)
    {
        var baseUrl = $"http://127.0.0.1:{backendPort}/v1";
        return new CodexRouteTarget
        {
            Id = ManagedCpaTargetId,
            Mode = CpaMode,
            Kind = "managed-cpa",
            Label = FormatManagedCpaLabel(backendPort),
            BaseUrl = baseUrl,
            Port = backendPort,
            ProfileName = ManagedCpaProfileName,
            ProviderName = ManagedCpaProviderName,
            IsCurrent = isCurrent,
            CanSwitch = true,
            StatusMessage = "切换到 CodexCliPlus 托管 CPA。",
        };
    }

    private static CodexRouteTarget CreateThirdPartyTarget(
        string baseUrl,
        string? providerName,
        string? profileName,
        bool isCurrent
    )
    {
        var normalizedBaseUrl = NormalizeCpaBaseUrl(baseUrl);
        return new CodexRouteTarget
        {
            Id = BuildThirdPartyTargetId(normalizedBaseUrl),
            Mode = CpaMode,
            Kind = "third-party-cpa",
            Label = FormatThirdPartyCpaLabel(normalizedBaseUrl),
            BaseUrl = normalizedBaseUrl,
            Port = TryReadPort(normalizedBaseUrl),
            ProfileName = string.IsNullOrWhiteSpace(profileName) ? null : profileName.Trim(),
            ProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName.Trim(),
            IsCurrent = isCurrent,
            CanSwitch = true,
            StatusMessage = "切换到当前配置中的第三方 CPA。",
        };
    }

    private static int GetTargetOrder(CodexRouteTarget target)
    {
        return target.Id switch
        {
            OfficialTargetId => 0,
            ManagedCpaTargetId => 1,
            _ when target.IsCurrent => 2,
            _ => 3,
        };
    }

    private static int[] GetLoopbackListeningPorts()
    {
        try
        {
            return IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(endpoint => IsLoopbackAddress(endpoint.Address))
                .Select(endpoint => endpoint.Port)
                .Where(port => port is > 0 and <= 65535)
                .Distinct()
                .ToArray();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private static bool IsLoopbackAddress(IPAddress address)
    {
        return IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any);
    }

    private static async Task<bool> ProbeLoopbackCpaAsync(
        HttpClient httpClient,
        int port,
        CancellationToken cancellationToken
    )
    {
        var baseUrl = $"http://127.0.0.1:{port}";
        var modelsStatus = await ProbeStatusAsync(
            httpClient,
            $"{baseUrl}/v1/models",
            cancellationToken
        );
        if (modelsStatus is 200 or 401 or 403)
        {
            return true;
        }

        var healthStatus = await ProbeStatusAsync(httpClient, $"{baseUrl}/healthz", cancellationToken);
        return healthStatus is >= 200 and < 300;
    }

    private static async Task<int?> ProbeStatusAsync(
        HttpClient httpClient,
        string url,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var response = await httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            return (int)response.StatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static bool TryGetTable(TomlTable model, string key, out TomlTable table)
    {
        if (model.TryGetValue(key, out var value) && value is TomlTable childTable)
        {
            table = childTable;
            return true;
        }

        table = [];
        return false;
    }

    private static string? FindProfileForProvider(TomlTable model, string providerName)
    {
        if (!TryGetTable(model, "profiles", out var profiles))
        {
            return null;
        }

        foreach (var entry in profiles)
        {
            if (
                entry.Value is TomlTable profileTable
                && TryReadString(profileTable, "model_provider", out var profileProvider)
                && string.Equals(profileProvider, providerName, StringComparison.OrdinalIgnoreCase)
            )
            {
                return entry.Key;
            }
        }

        return null;
    }

    private static string NormalizeCpaBaseUrl(string? baseUrl)
    {
        var trimmed = baseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        return string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/"
            ? $"{origin}/v1"
            : $"{origin}{uri.AbsolutePath.TrimEnd('/')}";
    }

    private static string NormalizeBackendBaseUrl(string baseUrl)
    {
        var normalizedBaseUrl = NormalizeCpaBaseUrl(baseUrl);
        if (Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.Equals("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }
        }

        return normalizedBaseUrl.TrimEnd('/');
    }

    private static string BuildThirdPartyTargetId(string baseUrl)
    {
        return ThirdPartyTargetPrefix + NormalizeCpaBaseUrl(baseUrl);
    }

    private static string BuildManagedExternalProviderName(string baseUrl)
    {
        return ManagedExternalProviderPrefix + BuildExternalTargetSuffix(baseUrl);
    }

    private static string BuildManagedExternalProfileName(string baseUrl)
    {
        return ManagedExternalProfilePrefix + BuildExternalTargetSuffix(baseUrl);
    }

    private static string BuildExternalTargetSuffix(string baseUrl)
    {
        var port = TryReadPort(baseUrl);
        if (port is not null)
        {
            return port.Value.ToString(CultureInfo.InvariantCulture);
        }

        var sanitized = Regex
            .Replace(baseUrl.ToLowerInvariant(), "[^a-z0-9]+", "-")
            .Trim('-');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "custom";
        }

        return sanitized.Length <= 48 ? sanitized : sanitized[..48].Trim('-');
    }

    private static bool IsManagedExternalProvider(string? providerName)
    {
        return providerName?.StartsWith(
                ManagedExternalProviderPrefix,
                StringComparison.OrdinalIgnoreCase
            ) == true;
    }

    private static bool IsManagedCpaProvider(string? providerName)
    {
        return string.Equals(providerName, ManagedCpaProviderName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManagedCpaProfile(string? profileName)
    {
        return string.Equals(profileName, ManagedCpaProfileName, StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryReadPort(string? baseUrl)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && uri.Port > 0
            ? uri.Port
            : null;
    }

    private static string FormatManagedCpaLabel(int backendPort)
    {
        return $"CodexCliPlus CPA 127.0.0.1:{backendPort}";
    }

    private static string FormatThirdPartyCpaLabel(string? baseUrl)
    {
        var endpoint = FormatEndpointLabel(baseUrl);
        return string.IsNullOrWhiteSpace(endpoint) ? "第三方 CPA 当前配置" : $"第三方 CPA {endpoint}";
    }

    private static string FormatEndpointLabel(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return baseUrl?.Trim() ?? string.Empty;
        }

        return uri.IsLoopback && uri.Port > 0
            ? $"127.0.0.1:{uri.Port}"
            : uri.GetLeftPart(UriPartial.Authority);
    }

    private static string GetModeLabel(string mode)
    {
        return mode switch
        {
            OfficialMode => "官方 Codex",
            CpaMode => "CPA 模式",
            _ => "未知模式",
        };
    }

    private static string EscapeTomlString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private async Task<RouteDetectionResult> DetectRouteAsync(
        int backendPort,
        CancellationToken cancellationToken
    )
    {
        var configPath = GetUserConfigPath();
        var authPath = GetUserAuthPath();
        var authIsDummy =
            File.Exists(authPath)
            && IsCpaDummyAuth(await File.ReadAllTextAsync(authPath, cancellationToken));

        if (!File.Exists(configPath))
        {
            return new RouteDetectionResult
            {
                Mode = authIsDummy ? CpaMode : OfficialMode,
                ConfigExists = false,
                CurrentTargetId = authIsDummy ? ManagedCpaTargetId : OfficialTargetId,
                CurrentLabel = authIsDummy
                    ? FormatManagedCpaLabel(backendPort)
                    : "官方 Codex",
                IsManagedCpa = authIsDummy,
            };
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken);
        if (!TryParseTomlTable(content, out var model, out var errorMessage) || model is null)
        {
            return new RouteDetectionResult
            {
                Mode = "unknown",
                ConfigExists = true,
                ErrorMessage = errorMessage,
                CurrentLabel = "检测失败",
            };
        }

        return ResolveCurrentRoute(model, authIsDummy, backendPort) with { ConfigExists = true };
    }

    private static RouteDetectionResult ResolveCurrentRoute(
        TomlTable model,
        bool authIsDummy,
        int backendPort
    )
    {
        string? profileName = null;
        string? providerName = null;
        string? baseUrl = null;
        TomlTable? profileTable = null;

        if (TryReadString(model, "profile", out var profile))
        {
            profileName = profile;
            profileTable = TryGetChildTable(model, "profiles", profileName);
            if (profileTable is not null)
            {
                if (TryReadString(profileTable, "model_provider", out var profileProvider))
                {
                    providerName = profileProvider;
                }

                if (TryReadString(profileTable, "base_url", out var profileBaseUrl))
                {
                    baseUrl = profileBaseUrl;
                }
            }

            var profileMode = NormalizeRouteMode(profileName);
            if (profileMode == OfficialMode)
            {
                return CreateOfficialDetection();
            }
        }

        if (string.IsNullOrWhiteSpace(providerName) && TryReadString(model, "model_provider", out var provider))
        {
            providerName = provider;
        }

        if (string.IsNullOrWhiteSpace(baseUrl) && TryReadString(model, "base_url", out var rootBaseUrl))
        {
            baseUrl = rootBaseUrl;
        }

        if (
            !string.IsNullOrWhiteSpace(providerName)
            && TryReadProviderBaseUrl(model, providerName, out var providerBaseUrl)
        )
        {
            baseUrl = providerBaseUrl;
        }

        var normalizedBaseUrl = NormalizeCpaBaseUrl(baseUrl);
        var providerMode = ResolveProviderMode(providerName);
        if (providerMode == OfficialMode && string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            return CreateOfficialDetection();
        }

        var isManagedCpa =
            StringComparer.OrdinalIgnoreCase.Equals(profileName, ManagedCpaProfileName)
            || StringComparer.OrdinalIgnoreCase.Equals(providerName, ManagedCpaProviderName);
        if (
            isManagedCpa
            || providerMode == CpaMode
            || !string.IsNullOrWhiteSpace(normalizedBaseUrl)
            || authIsDummy
        )
        {
            var currentTargetId = isManagedCpa
                ? ManagedCpaTargetId
                : !string.IsNullOrWhiteSpace(normalizedBaseUrl)
                    ? BuildThirdPartyTargetId(normalizedBaseUrl)
                    : ManagedCpaTargetId;

            return new RouteDetectionResult
            {
                Mode = CpaMode,
                Model = model,
                CurrentTargetId = currentTargetId,
                CurrentLabel = isManagedCpa
                    ? FormatManagedCpaLabel(backendPort)
                    : FormatThirdPartyCpaLabel(normalizedBaseUrl),
                IsManagedCpa = isManagedCpa,
                BaseUrl = normalizedBaseUrl,
                Port = TryReadPort(normalizedBaseUrl),
                ProviderName = providerName,
                ProfileName = profileName,
            };
        }

        return CreateOfficialDetection();

        RouteDetectionResult CreateOfficialDetection()
        {
            return new RouteDetectionResult
            {
                Mode = OfficialMode,
                Model = model,
                CurrentTargetId = OfficialTargetId,
                CurrentLabel = "官方 Codex",
                ProviderName = providerName,
                ProfileName = profileName,
            };
        }
    }

    private static string? NormalizeRouteMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized == OfficialMode || normalized == ManagedOfficialProfileName)
        {
            return OfficialMode;
        }

        if (normalized == CpaMode || normalized == ManagedCpaProfileName)
        {
            return CpaMode;
        }

        return null;
    }

    private static string? ResolveProviderMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized == "openai" || normalized == OfficialMode)
        {
            return OfficialMode;
        }

        if (
            normalized == CpaMode
            || normalized == ManagedCpaProviderName
            || normalized == LegacyCpaProviderName
            || normalized.StartsWith(ManagedExternalProviderPrefix, StringComparison.Ordinal)
        )
        {
            return CpaMode;
        }

        return null;
    }

    private static TomlTable? TryGetChildTable(
        TomlTable model,
        string parentKey,
        string childKey
    )
    {
        if (
            !model.TryGetValue(parentKey, out var parentValue)
            || parentValue is not TomlTable parentTable
            || !parentTable.TryGetValue(childKey, out var childValue)
            || childValue is not TomlTable childTable
        )
        {
            return null;
        }

        return childTable;
    }

    private static bool TryReadProviderBaseUrl(
        TomlTable model,
        string providerName,
        out string baseUrl
    )
    {
        baseUrl = string.Empty;
        var providerTable = TryGetChildTable(model, "model_providers", providerName);
        return providerTable is not null && TryReadString(providerTable, "base_url", out baseUrl);
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
            profile = NormalizeRouteMode(profileValue) ?? profileValue;
            return true;
        }

        var source = ResolveCurrentRoute(
            model,
            authIsDummy: false,
            AppConstants.DefaultBackendPort
        ).Mode;
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

    private sealed record RouteDetectionResult
    {
        public string Mode { get; init; } = "unknown";

        public bool ConfigExists { get; init; }

        public string? ErrorMessage { get; init; }

        public TomlTable? Model { get; init; }

        public string? CurrentTargetId { get; init; }

        public string CurrentLabel { get; init; } = "未知模式";

        public bool IsManagedCpa { get; init; }

        public string? BaseUrl { get; init; }

        public int? Port { get; init; }

        public string? ProviderName { get; init; }

        public string? ProfileName { get; init; }
    }

    private sealed record OfficialAuthCandidate(string Path, string Content);
}
