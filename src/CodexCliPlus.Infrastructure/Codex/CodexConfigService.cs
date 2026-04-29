using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
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
    private const string CpaDummyAuthJson = "{\n  \"OPENAI_API_KEY\": \"sk-dummy\"\n}\n";

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
        return source == CodexSourceKind.Cpa ? "cpa" : "official";
    }

    private async Task ApplyAuthAsync(CodexSourceKind source, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetUserConfigDirectory());

        if (source == CodexSourceKind.Cpa)
        {
            await BackupOfficialAuthIfNeededAsync(cancellationToken);
            await File.WriteAllTextAsync(
                GetUserAuthPath(),
                CpaDummyAuthJson,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );
            return;
        }

        var currentAuthPath = GetUserAuthPath();
        foreach (var candidate in GetOfficialAuthCandidates())
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

            await File.WriteAllTextAsync(
                currentAuthPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );
            return;
        }
    }

    private static string MergeManagedBlocks(
        string content,
        int backendPort,
        CodexSourceKind defaultSource
    )
    {
        var cleaned = StripManagedBlocks(content);
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

    private static string StripManagedBlocks(string content)
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

        return Regex
            .Replace(
                withoutRoot,
                $"{Regex.Escape(ManagedTablesStart)}[\\s\\S]*?{Regex.Escape(ManagedTablesEnd)}\\s*",
                string.Empty
            )
            .Trim();
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
            + "model_provider = \"cpa\""
            + Environment.NewLine
            + $"chatgpt_base_url = \"{cpaBaseUrl}/backend-api\""
            + Environment.NewLine
            + "cli_auth_credentials_store = \"file\""
            + Environment.NewLine
            + Environment.NewLine
            + "[model_providers.cpa]"
            + Environment.NewLine
            + "name = \"cpa\""
            + Environment.NewLine
            + $"base_url = \"{cpaBaseUrl}/v1\""
            + Environment.NewLine
            + "wire_api = \"responses\""
            + Environment.NewLine
            + "requires_openai_auth = true"
            + Environment.NewLine
            + $"{ManagedTablesEnd}";
    }

    private static bool TryReadProfile(string content, out string profile)
    {
        profile = "official";
        if (!TryParseTomlTable(content, out var model, out _) || model is null)
        {
            return false;
        }

        if (
            model.TryGetValue("profile", out var value)
            && value is string text
            && !string.IsNullOrWhiteSpace(text)
        )
        {
            profile = text.Trim();
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
        var backupPath = Path.Combine(
            configDirectory,
            $"config.codexcliplus-backup-{DateTimeOffset.Now:yyyyMMddHHmmss}.toml"
        );
        await File.WriteAllTextAsync(
            backupPath,
            existingContent,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
        return backupPath;
    }

    private async Task BackupOfficialAuthIfNeededAsync(CancellationToken cancellationToken)
    {
        var authPath = GetUserAuthPath();
        if (!File.Exists(authPath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(authPath, cancellationToken);
        if (IsCpaDummyAuth(content))
        {
            return;
        }

        var backupPath = GetDesktopAuthBackupPath();
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        await File.WriteAllTextAsync(
            backupPath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
    }

    private IEnumerable<string> GetOfficialAuthCandidates()
    {
        yield return GetDesktopAuthBackupPath();
        yield return Path.Combine(
            GetUserConfigDirectory(),
            "switch-presets",
            "official",
            "auth.json"
        );
        yield return Path.Combine(GetUserConfigDirectory(), "bridge-chatgpt-auth.json");
    }

    private static bool IsCpaDummyAuth(string content)
    {
        return content.Contains("\"OPENAI_API_KEY\": \"sk-dummy\"", StringComparison.Ordinal);
    }
}
