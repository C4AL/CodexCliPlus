using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CPAD.Application.Abstractions;
using CPAD.Contracts;
using CPAD.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CPAD.Infrastructure.Status;

public sealed class HostSnapshotService : IHostSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ICpadLayoutService _layoutService;

    public HostSnapshotService(ICpadLayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    public async Task<HostSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var layout = _layoutService.Resolve();

        return new HostSnapshotDto
        {
            InstallRoot = layout.InstallRoot,
            ServiceState = await ReadJsonAsync<ServiceStateDto>(layout.Files["serviceState"], cancellationToken),
            CpaRuntime = await ResolveCpaRuntimeAsync(layout, cancellationToken),
            Codex = ResolveCodex(layout),
            PluginMarket = await ResolvePluginMarketAsync(layout, cancellationToken),
            UpdateCenter = await ResolveUpdateCenterAsync(layout, cancellationToken),
            ManagerStatus = await QueryManagerStatusAsync(cancellationToken)
        };
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static CodexShimResolutionDto ResolveCodex(CpadLayout layout)
    {
        var modeState = ReadCodexModeState(layout);
        var targetCandidates = GetCodexRuntimeCandidates(layout, modeState.Mode);
        var (targetPath, targetExists) = ResolveFirstExisting(targetCandidates);
        var globalPath = ResolveGlobalExecutable("codex.cmd", "codex.exe", "codex");
        var (launchArgs, launchReady, launchMessage) = ResolveCodexLaunchPlan(layout, modeState.Mode, targetExists);

        return new CodexShimResolutionDto
        {
            Mode = modeState.Mode,
            ModeFile = layout.Files["codexMode"],
            ShimPath = Path.Combine(layout.InstallRoot, "codex.exe"),
            TargetPath = targetPath,
            TargetExists = targetExists,
            GlobalPath = globalPath,
            GlobalExists = !string.IsNullOrWhiteSpace(globalPath),
            LaunchArgs = launchArgs,
            LaunchReady = launchReady,
            LaunchMessage = launchMessage,
            Message = modeState.Message,
            UpdatedAt = modeState.UpdatedAt
        };
    }

    private static CodexModeStateModel ReadCodexModeState(CpadLayout layout)
    {
        if (File.Exists(layout.Files["codexMode"]))
        {
            var content = File.ReadAllText(layout.Files["codexMode"]);
            var state = JsonSerializer.Deserialize<CodexModeStateModel>(content, JsonOptions);
            if (state is not null)
            {
                state.Mode = NormalizeCodexMode(state.Mode);
                return state;
            }
        }

        return new CodexModeStateModel
        {
            ProductName = ProductConstants.ProductName,
            Mode = "official",
            Message = "Codex mode defaults to official until an explicit mode file is written.",
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string NormalizeCodexMode(string? value)
    {
        return string.Equals(value, "cpa", StringComparison.OrdinalIgnoreCase)
            ? "cpa"
            : "official";
    }

    private static IReadOnlyList<string> GetCodexRuntimeCandidates(CpadLayout layout, string mode)
    {
        var officialCandidates = new[]
        {
            Path.Combine(layout.Directories["codexRuntime"], "codex.exe"),
            Path.Combine(layout.Directories["codexRuntime"], "codex.cmd"),
            Path.Combine(layout.Directories["codexRuntime"], "bin", "codex.exe"),
            Path.Combine(layout.Directories["codexRuntime"], "bin", "codex.cmd")
        };

        if (mode.Equals("cpa", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = new List<string>();
            var overridePath = Environment.GetEnvironmentVariable("CPAD_CODEX_CPA_EXECUTABLE");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                candidates.Add(Path.GetFullPath(overridePath));
            }

            candidates.AddRange(officialCandidates);
            candidates.Add(Path.Combine(layout.Directories["cpaRuntime"], "codex.exe"));
            candidates.Add(Path.Combine(layout.Directories["cpaRuntime"], "codex.cmd"));
            candidates.Add(Path.Combine(layout.Directories["cpaRuntime"], "bin", "codex.exe"));
            candidates.Add(Path.Combine(layout.Directories["cpaRuntime"], "bin", "codex.cmd"));
            return candidates;
        }

        var officialOverride = Environment.GetEnvironmentVariable("CPAD_CODEX_OFFICIAL_EXECUTABLE");
        return string.IsNullOrWhiteSpace(officialOverride)
            ? officialCandidates
            : new[] { Path.GetFullPath(officialOverride) }.Concat(officialCandidates).ToArray();
    }

    private static (string targetPath, bool targetExists) ResolveFirstExisting(IEnumerable<string> candidates)
    {
        var cleanedCandidates = candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).ToList();
        foreach (var candidate in cleanedCandidates)
        {
            if (File.Exists(candidate))
            {
                return (candidate, true);
            }
        }

        return (cleanedCandidates.FirstOrDefault() ?? string.Empty, false);
    }

    private static string ResolveGlobalExecutable(params string[] fileNames)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return string.Empty;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var fileName in fileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return string.Empty;
    }

    private static (IReadOnlyList<string> args, bool ready, string message) ResolveCodexLaunchPlan(CpadLayout layout, string mode, bool targetExists)
    {
        if (!targetExists)
        {
            return (Array.Empty<string>(), false, "The selected Codex runtime target does not exist.");
        }

        if (!mode.Equals("cpa", StringComparison.OrdinalIgnoreCase))
        {
            return (Array.Empty<string>(), true, "Official mode launches the managed Codex runtime directly.");
        }

        var sourceRoot = ResolveCurrentManagedCpaSourceRoot(layout);
        if (!ManagedCpaSourceSupportsCodexRemote(sourceRoot))
        {
            return (Array.Empty<string>(), false, $"CPA mode currently points at {sourceRoot}, which no longer exposes the legacy Codex remote bridge.");
        }

        var configPath = Path.Combine(layout.Directories["cpaRuntime"], "config.yaml");
        if (!File.Exists(configPath))
        {
            return (Array.Empty<string>(), false, "CPA mode requires a managed CPA runtime config file.");
        }

        var insight = InspectCpaRuntimeConfig(configPath);
        if (!insight.CodexAppServerProxyEnabled)
        {
            return (Array.Empty<string>(), false, "CPA mode requires codex-app-server-proxy to be enabled.");
        }

        return (new[] { "--remote", insight.CodexRemoteUrl }, true, $"CPA mode will connect through {insight.CodexRemoteUrl}.");
    }

    private static async Task<CpaRuntimeStatusDto> ResolveCpaRuntimeAsync(CpadLayout layout, CancellationToken cancellationToken)
    {
        var state = await ReadJsonAsync<CpaRuntimeStateModel>(layout.Files["cpaRuntimeState"], cancellationToken);
        var sourceRoot = ResolveManagedCpaSourceRoot(layout);
        var managedBinary = Path.Combine(layout.Directories["cpaRuntime"], ProductConstants.ManagedCpaBinaryName);
        var configPath = Path.Combine(layout.Directories["cpaRuntime"], "config.yaml");
        var logPath = layout.Files["cpaRuntimeLog"];
        var phase = "not-initialized";
        var message = "CPA runtime is not under managed control yet.";
        var updatedAt = DateTimeOffset.MinValue;
        var pid = 0;
        var running = false;

        if (state is not null)
        {
            sourceRoot = MigrateManagedCpaSourceRoot(state.SourceRoot, layout);
            managedBinary = NormalizeManagedBinaryPath(state.ManagedBinary, layout);
            configPath = string.IsNullOrWhiteSpace(state.ConfigPath) ? configPath : state.ConfigPath;
            logPath = string.IsNullOrWhiteSpace(state.LogPath) ? logPath : state.LogPath;
            phase = string.IsNullOrWhiteSpace(state.Phase) ? phase : state.Phase;
            message = string.IsNullOrWhiteSpace(state.Message) ? message : state.Message;
            updatedAt = state.UpdatedAt;
            pid = state.Pid;
            running = IsProcessRunning(pid);

            if (pid > 0 && phase == "running" && !running)
            {
                phase = "stopped";
                message = $"Recorded CPA process {pid} is no longer running.";
                pid = 0;
            }
            else if (!running)
            {
                pid = 0;
            }
        }

        var status = new CpaRuntimeStatusDto
        {
            SourceRoot = sourceRoot,
            SourceExists = Directory.Exists(sourceRoot),
            BuildPackage = "./cmd/server",
            ManagedBinary = managedBinary,
            BinaryExists = File.Exists(managedBinary),
            ConfigPath = configPath,
            ConfigExists = File.Exists(configPath),
            StateFile = layout.Files["cpaRuntimeState"],
            LogPath = logPath,
            Phase = phase,
            Pid = pid,
            Running = running,
            Message = message,
            UpdatedAt = updatedAt
        };

        if (!status.ConfigExists)
        {
            return status with
            {
                HealthCheck = new CpaRuntimeHealthCheckDto
                {
                    Message = "CPA runtime config file is missing, so health probing was skipped."
                }
            };
        }

        var insight = InspectCpaRuntimeConfig(status.ConfigPath);
        var health = await ProbeCpaRuntimeHealthAsync(insight, status.Running, cancellationToken);

        return status with
        {
            ConfigInsight = insight,
            HealthCheck = health
        };
    }

    private static string ResolveManagedCpaSourceRoot(CpadLayout layout)
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_CPA_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        var candidates = new List<string>();
        if (layout.Directories.TryGetValue("officialCoreBaseline", out var officialBaseline) && !string.IsNullOrWhiteSpace(officialBaseline))
        {
            candidates.Add(officialBaseline);
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(homeDir))
        {
            candidates.Add(Path.Combine(homeDir, ProductConstants.InstallDirName, ProductConstants.SourceDirName, "official-backend"));
            candidates.Add(Path.Combine(homeDir, ProductConstants.InstallDirName, "reference", "upstream", "official-backend"));
            candidates.Add(Path.Combine(homeDir, ProductConstants.InstallDirName, "upstream", "CLIProxyAPI"));
        }

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return candidates.FirstOrDefault() ?? string.Empty;
    }

    private static string ResolveLegacyCpaOverlaySourceRoot()
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_CPA_OVERLAY_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var repositoryRoot = ResolveRepositoryRootForStatic();
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            candidates.Add(Path.Combine(repositoryRoot, "reference", "upstream", "cpa-uv-overlay"));
            candidates.Add(Path.Combine(repositoryRoot, ProductConstants.SourceDirName, "cpa-uv-overlay"));
        }

        if (!string.IsNullOrWhiteSpace(homeDir))
        {
            candidates.Add(Path.Combine(homeDir, "workspace", "CPA-UV-publish"));
        }

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return candidates.FirstOrDefault() ?? "CPA-UV-publish";
    }

    private static string ResolveCurrentManagedCpaSourceRoot(CpadLayout layout)
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_CPA_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        if (File.Exists(layout.Files["cpaRuntimeState"]))
        {
            var state = JsonSerializer.Deserialize<CpaRuntimeStateModel>(File.ReadAllText(layout.Files["cpaRuntimeState"]), JsonOptions);
            if (state is not null && !string.IsNullOrWhiteSpace(state.SourceRoot))
            {
                return NormalizeRepositoryReferencePath(state.SourceRoot);
            }
        }

        return ResolveManagedCpaSourceRoot(layout);
    }

    private static string MigrateManagedCpaSourceRoot(string? sourceRoot, CpadLayout layout)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || IsLegacyCpaOverlaySourceRoot(sourceRoot))
        {
            return ResolveManagedCpaSourceRoot(layout);
        }

        return NormalizeRepositoryReferencePath(sourceRoot);
    }

    private static bool ManagedCpaSourceSupportsCodexRemote(string sourceRoot)
    {
        return IsLegacyCpaOverlaySourceRoot(sourceRoot);
    }

    private static bool IsLegacyCpaOverlaySourceRoot(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        var cleaned = Path.GetFullPath(sourceRoot);
        if (SamePath(cleaned, ResolveLegacyCpaOverlaySourceRoot()))
        {
            return true;
        }

        var baseName = Path.GetFileName(cleaned);
        return baseName.Equals("cpa-uv-publish", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("cpa-uv-overlay", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeManagedBinaryPath(string? managedBinary, CpadLayout layout)
    {
        var defaultPath = Path.Combine(layout.Directories["cpaRuntime"], ProductConstants.ManagedCpaBinaryName);
        if (string.IsNullOrWhiteSpace(managedBinary))
        {
            return defaultPath;
        }

        var cleaned = Path.GetFullPath(managedBinary);
        return Path.GetFileName(cleaned).Equals("CPA-UV.exe", StringComparison.OrdinalIgnoreCase)
            ? defaultPath
            : cleaned;
    }

    private static bool IsProcessRunning(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static CpaRuntimeConfigInsightDto InspectCpaRuntimeConfig(string configPath)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<CpaConfigFileModel>(File.ReadAllText(configPath)) ?? new CpaConfigFileModel();
        var port = config.Port > 0 ? config.Port : ProductConstants.DefaultCpaPort;
        var probeHost = NormalizeCpaProbeHost(config.Host);
        var scheme = config.Tls.Enable ? "https" : "http";
        var wsScheme = config.Tls.Enable ? "wss" : "ws";
        var baseUrl = $"{scheme}://{probeHost}:{port}";
        var codexBin = string.IsNullOrWhiteSpace(config.CodexAppServerProxy.CodexBin) ? "codex" : config.CodexAppServerProxy.CodexBin.Trim();

        return new CpaRuntimeConfigInsightDto
        {
            Host = config.Host?.Trim() ?? string.Empty,
            Port = port,
            TlsEnabled = config.Tls.Enable,
            BaseUrl = baseUrl,
            HealthUrl = baseUrl + "/healthz",
            ManagementUrl = baseUrl + "/management.html",
            UsageUrl = baseUrl + "/backend-api/wham/usage",
            CodexRemoteUrl = $"{wsScheme}://{probeHost}:{port}",
            ManagementAllowRemote = config.RemoteManagement.AllowRemote,
            ManagementEnabled = !string.IsNullOrWhiteSpace(config.RemoteManagement.SecretKey),
            ControlPanelEnabled = !config.RemoteManagement.DisableControlPanel,
            PanelRepository = NormalizeManagedPanelRepository(config.RemoteManagement.PanelGitHubRepository),
            CodexAppServerProxyEnabled = config.CodexAppServerProxy.Enable,
            CodexAppServerRestrictToLocalhost = config.CodexAppServerProxy.RestrictToLocalhost,
            CodexAppServerCodexBin = codexBin
        };
    }

    private static string NormalizeManagedPanelRepository(string? panelRepository)
    {
        var cleaned = panelRepository?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(cleaned) || cleaned.Contains("/Blackblock-inc/CPA-UV", StringComparison.OrdinalIgnoreCase)
            ? ProductConstants.ManagedCpaPanelRepository
            : cleaned;
    }

    private static string NormalizeCpaProbeHost(string? rawHost)
    {
        var host = (rawHost ?? string.Empty).Trim().Trim('[', ']');
        return host.ToLowerInvariant() switch
        {
            "" or "0.0.0.0" or "::" or "::1" or "localhost" => "127.0.0.1",
            _ => host
        };
    }

    private static async Task<CpaRuntimeHealthCheckDto> ProbeCpaRuntimeHealthAsync(CpaRuntimeConfigInsightDto insight, bool running, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insight.HealthUrl))
        {
            return new CpaRuntimeHealthCheckDto
            {
                Message = "Health URL was not resolved from CPA config."
            };
        }

        if (!running)
        {
            return new CpaRuntimeHealthCheckDto
            {
                Message = "CPA runtime is not running, so /healthz was not probed."
            };
        }

        var handler = new HttpClientHandler();
        if (insight.TlsEnabled)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(1500)
        };

        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            using var response = await client.GetAsync(insight.HealthUrl, cancellationToken);
            var healthy = response.IsSuccessStatusCode;
            return new CpaRuntimeHealthCheckDto
            {
                Checked = true,
                Healthy = healthy,
                StatusCode = (int)response.StatusCode,
                Message = healthy ? "CPA runtime health check passed." : $"/healthz returned HTTP {(int)response.StatusCode}.",
                CheckedAt = checkedAt
            };
        }
        catch (Exception ex)
        {
            return new CpaRuntimeHealthCheckDto
            {
                Checked = true,
                Healthy = false,
                Message = $"/healthz request failed: {ex.Message}",
                CheckedAt = checkedAt
            };
        }
    }

    private static async Task<PluginMarketStatusDto> ResolvePluginMarketAsync(CpadLayout layout, CancellationToken cancellationToken)
    {
        var sourceRoot = ResolvePluginSourceRoot(layout);
        var marketplacePath = ResolvePluginMarketplacePath(layout);
        var status = new PluginMarketStatusDto
        {
            SourceRoot = sourceRoot,
            SourceExists = Directory.Exists(sourceRoot),
            MarketplacePath = marketplacePath,
            MarketplaceExists = File.Exists(marketplacePath),
            CatalogPath = layout.Files["pluginCatalog"],
            StatePath = layout.Files["pluginState"],
            PluginsDir = layout.Directories["plugins"]
        };

        var catalog = await ReadJsonAsync<PluginCatalogFileModel>(layout.Files["pluginCatalog"], cancellationToken) ??
                      await LoadFallbackPluginCatalogAsync(sourceRoot, cancellationToken);
        var state = await ReadJsonAsync<PluginStateFileModel>(layout.Files["pluginState"], cancellationToken);
        var stateMap = state?.Plugins ?? new Dictionary<string, PluginRuntimeStateModel>(StringComparer.OrdinalIgnoreCase);

        var plugins = new List<PluginStatusDto>();

        if (catalog is not null)
        {
            sourceRoot = string.IsNullOrWhiteSpace(catalog.SourceRoot)
                ? sourceRoot
                : NormalizeRepositoryReferencePath(catalog.SourceRoot);
            marketplacePath = string.IsNullOrWhiteSpace(catalog.MarketplacePath)
                ? marketplacePath
                : NormalizeRepositoryReferencePath(catalog.MarketplacePath);
            status = status with
            {
                SourceRoot = sourceRoot,
                SourceExists = Directory.Exists(sourceRoot),
                MarketplacePath = marketplacePath,
                MarketplaceExists = File.Exists(marketplacePath),
                CatalogSource = catalog.CatalogSource ?? string.Empty,
                CatalogPath = string.Equals(catalog.CatalogSource, "reference-bundle", StringComparison.OrdinalIgnoreCase)
                    ? ResolvePluginBundlePath(sourceRoot)
                    : layout.Files["pluginCatalog"],
                UpdatedAt = catalog.UpdatedAt
            };

            foreach (var entry in catalog.Plugins.OrderBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase))
            {
                stateMap.TryGetValue(entry.Id, out var runtimeState);
                var sourcePath = NormalizeRepositoryReferencePath(entry.SourcePath);
                var readmePath = NormalizeRepositoryReferencePath(entry.ReadmePath);
                var installPath = string.IsNullOrWhiteSpace(runtimeState?.InstallPath)
                    ? Path.Combine(layout.Directories["plugins"], entry.Id)
                    : runtimeState!.InstallPath;
                var installExists = Directory.Exists(installPath) || File.Exists(installPath);
                var installed = runtimeState is not null && runtimeState.Installed && installExists;
                var enabled = installed && runtimeState!.Enabled;
                var installedVersion = runtimeState?.InstalledVersion ?? string.Empty;

                plugins.Add(new PluginStatusDto
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Version = entry.Version,
                    Description = entry.Description,
                    SourceType = entry.SourceType,
                    SourcePath = sourcePath,
                    SourceExists = !string.IsNullOrWhiteSpace(sourcePath) && (Directory.Exists(sourcePath) || File.Exists(sourcePath)),
                    ReadmePath = readmePath,
                    ReadmeExists = !string.IsNullOrWhiteSpace(readmePath) && File.Exists(readmePath),
                    Category = entry.Category,
                    RepositoryUrl = entry.RepositoryUrl,
                    RepositoryRef = entry.RepositoryRef,
                    RepositorySubdir = entry.RepositorySubdir,
                    DownloadUrl = entry.DownloadUrl,
                    InstallPath = installPath,
                    InstallExists = installExists,
                    Installed = installed,
                    Enabled = enabled,
                    InstalledVersion = installedVersion,
                    NeedsUpdate = installed && ShouldComparePluginVersion(entry.Version) && !string.IsNullOrWhiteSpace(installedVersion) && !string.Equals(installedVersion, entry.Version, StringComparison.OrdinalIgnoreCase),
                    Message = runtimeState?.Message ?? string.Empty,
                    UpdatedAt = runtimeState?.UpdatedAt ?? default
                });
            }
        }

        foreach (var kvp in stateMap.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (plugins.Any(plugin => plugin.Id.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)) || !ShouldExposeStateOnlyPlugin(kvp.Value))
            {
                continue;
            }

            var installPath = string.IsNullOrWhiteSpace(kvp.Value.InstallPath)
                ? Path.Combine(layout.Directories["plugins"], kvp.Key)
                : kvp.Value.InstallPath;
            var installExists = Directory.Exists(installPath) || File.Exists(installPath);
            var installed = kvp.Value.Installed && installExists;

            plugins.Add(new PluginStatusDto
            {
                Id = kvp.Key,
                Name = kvp.Key,
                Version = kvp.Value.InstalledVersion ?? string.Empty,
                Description = "Plugin is not present in the current catalog but still has local runtime state.",
                InstallPath = installPath,
                InstallExists = installExists,
                Installed = installed,
                Enabled = installed && kvp.Value.Enabled,
                InstalledVersion = kvp.Value.InstalledVersion ?? string.Empty,
                Message = kvp.Value.Message ?? string.Empty,
                UpdatedAt = kvp.Value.UpdatedAt
            });
        }

        var updatedAt = status.UpdatedAt;
        if (state is not null && state.UpdatedAt > updatedAt)
        {
            updatedAt = state.UpdatedAt;
        }

        return status with
        {
            UpdatedAt = updatedAt,
            Plugins = plugins.OrderBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string ResolvePluginSourceRoot(CpadLayout layout)
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_PLUGIN_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        var repositoryRoot = ResolveRepositoryRootForStatic();
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            var repositoryCandidates = new[]
            {
                Path.Combine(repositoryRoot, "reference", "plugins"),
                Path.Combine(repositoryRoot, "plugins")
            };

            foreach (var candidate in repositoryCandidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return repositoryCandidates[0];
        }

        var installRoot = layout.InstallRoot;
        var candidates = new List<string>
        {
            Path.Combine(installRoot, "plugin-source"),
            Path.Combine(installRoot, "resources", "plugin-source"),
            Path.Combine(installRoot, "resources", "plugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Cli Proxy API Desktop", "plugin-source")
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "workspace", "omni-bot-plugins-oss");
    }

    private static string ResolvePluginMarketplacePath(CpadLayout layout)
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_PLUGIN_MARKETPLACE_FILE");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        var repositoryRoot = ResolveRepositoryRootForStatic();
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            candidates.Add(Path.Combine(repositoryRoot, ".agents", "plugins", "marketplace.json"));
        }

        candidates.Add(Path.Combine(layout.InstallRoot, ".agents", "plugins", "marketplace.json"));
        candidates.Add(Path.Combine(layout.InstallRoot, "resources", ".agents", "plugins", "marketplace.json"));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return candidates.FirstOrDefault() ?? Path.Combine(".agents", "plugins", "marketplace.json");
    }

    private static bool ShouldComparePluginVersion(string version)
    {
        var normalized = (version ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is not ("" or "latest" or "0.0.0");
    }

    private static bool ShouldExposeStateOnlyPlugin(PluginRuntimeStateModel state)
    {
        return state.Installed ||
               state.Enabled ||
               !string.IsNullOrWhiteSpace(state.InstallPath) ||
               !string.IsNullOrWhiteSpace(state.Message) ||
               state.UpdatedAt != default;
    }

    private static async Task<UpdateCenterStatusDto> ResolveUpdateCenterAsync(CpadLayout layout, CancellationToken cancellationToken)
    {
        var state = await ReadJsonAsync<UpdateCenterStatusDto>(layout.Files["updateCenterState"], cancellationToken) ??
                    await LoadFallbackUpdateCenterAsync(layout, cancellationToken);
        return state ?? new UpdateCenterStatusDto
        {
            ProductName = ProductConstants.ProductName,
            StateFile = layout.Files["updateCenterState"],
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<PluginCatalogFileModel?> LoadFallbackPluginCatalogAsync(string sourceRoot, CancellationToken cancellationToken)
    {
        var bundlePath = ResolvePluginBundlePath(sourceRoot);
        var bundle = await ReadJsonAsync<PluginSourceBundleModel>(bundlePath, cancellationToken);
        if (bundle is null)
        {
            return null;
        }

        var plugins = new List<PluginCatalogEntryModel>();
        foreach (var entry in bundle.Entries.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var syncedPath = ResolveBundleEntryPath(sourceRoot, entry.Directory, entry.SyncedPath);
            var manifestPath = ResolveManifestPath(syncedPath, entry.ManifestFile);
            var sourceManifest = await ReadJsonAsync<ReferenceSourceManifestModel>(manifestPath, cancellationToken);
            var pluginManifest = await ReadJsonAsync<PluginPackageManifestModel>(
                Path.Combine(syncedPath, ".codex-plugin", "plugin.json"),
                cancellationToken);
            var pluginInterface = pluginManifest?.Interface ?? new PluginPackageInterfaceModel();
            var sourceExists = Directory.Exists(syncedPath) || File.Exists(syncedPath);

            if (!sourceExists && sourceManifest is null && pluginManifest is null)
            {
                continue;
            }

            plugins.Add(new PluginCatalogEntryModel
            {
                Id = FirstNonEmpty(sourceManifest?.Id, entry.Id),
                Name = FirstNonEmpty(pluginInterface.DisplayName, sourceManifest?.Name, entry.Name, entry.Id),
                Version = pluginManifest?.Version ?? string.Empty,
                Description = FirstNonEmpty(
                    pluginInterface.ShortDescription,
                    pluginManifest?.Description,
                    "Plugin metadata was loaded from the repository reference bundle."),
                SourceType = string.IsNullOrWhiteSpace(sourceManifest?.Repository) ? "reference" : "github",
                SourcePath = syncedPath,
                ReadmePath = Path.Combine(syncedPath, "README.md"),
                Category = pluginInterface.Category,
                RepositoryUrl = FirstNonEmpty(sourceManifest?.Repository, pluginManifest?.Homepage),
                RepositoryRef = sourceManifest?.Branch ?? string.Empty,
                RepositorySubdir = string.Empty,
                DownloadUrl = string.Empty
            });
        }

        return new PluginCatalogFileModel
        {
            ProductName = ProductConstants.ProductName,
            SourceRoot = sourceRoot,
            MarketplacePath = bundlePath,
            CatalogSource = "reference-bundle",
            Plugins = plugins,
            UpdatedAt = bundle.GeneratedAt
        };
    }

    private static async Task<UpdateCenterStatusDto?> LoadFallbackUpdateCenterAsync(CpadLayout layout, CancellationToken cancellationToken)
    {
        var sourcesRoot = layout.Directories["sources"];
        var bundlePath = ResolveManagedSourceBundlePath(sourcesRoot);
        var bundle = await ReadJsonAsync<ManagedSourceBundleModel>(bundlePath, cancellationToken);
        if (bundle is null)
        {
            return null;
        }

        var sources = new List<UpdateSourceStatusDto>();
        foreach (var entry in bundle.Entries.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var syncedPath = ResolveBundleEntryPath(sourcesRoot, entry.Directory, entry.SyncedPath);
            var manifestPath = ResolveManifestPath(syncedPath, entry.ManifestFile);
            var sourceManifest = await ReadJsonAsync<ReferenceSourceManifestModel>(manifestPath, cancellationToken);
            var currentRef = sourceManifest?.Commit ?? string.Empty;
            var available = Directory.Exists(syncedPath) || File.Exists(syncedPath);

            if (!available && sourceManifest is null)
            {
                continue;
            }

            sources.Add(new UpdateSourceStatusDto
            {
                Id = FirstNonEmpty(sourceManifest?.Id, entry.Id),
                Name = FirstNonEmpty(sourceManifest?.Name, entry.Name, entry.Id),
                Kind = "reference-snapshot",
                Source = syncedPath,
                CurrentRef = currentRef,
                LatestRef = currentRef,
                Dirty = false,
                Available = available,
                Message = available
                    ? BuildReferenceBundleMessage(currentRef)
                    : "Reference snapshot listed in the repository bundle is missing on disk.",
                UpdatedAt = sourceManifest?.SyncedAt ?? bundle.GeneratedAt
            });
        }

        return new UpdateCenterStatusDto
        {
            ProductName = ProductConstants.ProductName,
            StateFile = layout.Files["updateCenterState"],
            Sources = sources,
            UpdatedAt = sources.Count == 0
                ? bundle.GeneratedAt
                : sources.Max(source => source.UpdatedAt)
        };
    }

    private static string ResolvePluginBundlePath(string sourceRoot)
    {
        return Path.Combine(sourceRoot, "cpad-plugin-source-bundle.json");
    }

    private static string ResolveManagedSourceBundlePath(string sourcesRoot)
    {
        return Path.Combine(sourcesRoot, "cpad-source-bundle.json");
    }

    private static string ResolveBundleEntryPath(string rootPath, string? directory, string? syncedPath)
    {
        var normalizedSyncedPath = NormalizeRepositoryReferencePath(syncedPath);
        if (!string.IsNullOrWhiteSpace(normalizedSyncedPath) &&
            (Directory.Exists(normalizedSyncedPath) || File.Exists(normalizedSyncedPath)))
        {
            return normalizedSyncedPath;
        }

        if (!string.IsNullOrWhiteSpace(directory))
        {
            var directoryPath = Path.Combine(rootPath, directory);
            if (Directory.Exists(directoryPath) || File.Exists(directoryPath))
            {
                return directoryPath;
            }
        }

        return !string.IsNullOrWhiteSpace(normalizedSyncedPath)
            ? normalizedSyncedPath
            : Path.Combine(rootPath, directory ?? string.Empty);
    }

    private static string ResolveManifestPath(string rootPath, string? manifestFile)
    {
        return Path.Combine(rootPath, string.IsNullOrWhiteSpace(manifestFile) ? ".cpad-source.json" : manifestFile);
    }

    private static string BuildReferenceBundleMessage(string currentRef)
    {
        return string.IsNullOrWhiteSpace(currentRef)
            ? "Reference snapshot metadata was loaded from the repository bundle."
            : $"Reference snapshot {ShortRef(currentRef)} was loaded from the repository bundle.";
    }

    private static string ShortRef(string value)
    {
        return value.Length <= 12 ? value : value[..12];
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static async Task<ManagerStatusDto> QueryManagerStatusAsync(CancellationToken cancellationToken)
    {
        var serviceName = ProductConstants.ServiceName;
        var initialStatus = new ManagerStatusDto
        {
            ServiceName = serviceName,
            State = "not-installed"
        };

        var queryOutput = await RunProcessCaptureAsync("sc.exe", $"query {serviceName}", cancellationToken);
        if (queryOutput.ExitCode != 0)
        {
            return initialStatus;
        }

        var stateLine = queryOutput.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("STATE", StringComparison.OrdinalIgnoreCase));

        var qcOutput = await RunProcessCaptureAsync("sc.exe", $"qc {serviceName}", cancellationToken);
        var startTypeLine = qcOutput.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("START_TYPE", StringComparison.OrdinalIgnoreCase));
        var binaryPathLine = qcOutput.Output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("BINARY_PATH_NAME", StringComparison.OrdinalIgnoreCase));

        return new ManagerStatusDto
        {
            ServiceName = serviceName,
            Installed = true,
            State = ParseServiceState(stateLine),
            StartType = ParseServiceStartType(startTypeLine),
            BinaryPath = ParseServiceBinaryPath(binaryPathLine)
        };
    }

    private static string ParseServiceState(string? stateLine)
    {
        if (string.IsNullOrWhiteSpace(stateLine))
        {
            return "unknown";
        }

        var normalized = stateLine.ToUpperInvariant();
        if (normalized.Contains("RUNNING"))
        {
            return "running";
        }
        if (normalized.Contains("STOPPED"))
        {
            return "stopped";
        }
        if (normalized.Contains("START_PENDING"))
        {
            return "start-pending";
        }
        if (normalized.Contains("STOP_PENDING"))
        {
            return "stop-pending";
        }

        return "unknown";
    }

    private static string ParseServiceStartType(string? startTypeLine)
    {
        if (string.IsNullOrWhiteSpace(startTypeLine))
        {
            return string.Empty;
        }

        var normalized = startTypeLine.ToUpperInvariant();
        if (normalized.Contains("AUTO_START"))
        {
            return "automatic";
        }
        if (normalized.Contains("DEMAND_START"))
        {
            return "manual";
        }
        if (normalized.Contains("DISABLED"))
        {
            return "disabled";
        }

        return string.Empty;
    }

    private static string ParseServiceBinaryPath(string? binaryPathLine)
    {
        if (string.IsNullOrWhiteSpace(binaryPathLine))
        {
            return string.Empty;
        }

        var separatorIndex = binaryPathLine.IndexOf(':');
        return separatorIndex < 0
            ? binaryPathLine.Trim()
            : binaryPathLine[(separatorIndex + 1)..].Trim();
    }

    private static async Task<ProcessResult> RunProcessCaptureAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new ProcessResult(process.ExitCode, string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
    }

    private static string ResolveRepositoryRootForStatic()
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(custom) && IsRepositoryRootStatic(custom))
        {
            return Path.GetFullPath(custom);
        }

        var candidates = new List<string>();
        AppendDirectoryChain(candidates, Environment.CurrentDirectory);

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            AppendDirectoryChain(candidates, Path.GetDirectoryName(executablePath));
        }

        return candidates.FirstOrDefault(IsRepositoryRootStatic) ?? string.Empty;
    }

    private static void AppendDirectoryChain(ICollection<string> candidates, string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return;
        }

        var current = new DirectoryInfo(startPath);
        for (var depth = 0; depth < 6 && current is not null; depth++)
        {
            var fullName = current.FullName;
            if (!candidates.Any(existing => SamePath(existing, fullName)))
            {
                candidates.Add(fullName);
            }
            current = current.Parent;
        }
    }

    private static bool IsRepositoryRootStatic(string candidate)
    {
        var cleaned = Path.GetFullPath(candidate);
        var currentLayout = File.Exists(Path.Combine(cleaned, "CPAD.sln")) &&
                            File.Exists(Path.Combine(cleaned, "Directory.Build.props")) &&
                            File.Exists(Path.Combine(cleaned, "apps", "CPAD.Service", "CPAD.Service.csproj")) &&
                            File.Exists(Path.Combine(cleaned, "src", "CPAD.Infrastructure", "CPAD.Infrastructure.csproj"));

        if (currentLayout)
        {
            return true;
        }

        return File.Exists(Path.Combine(cleaned, "package.json")) &&
               File.Exists(Path.Combine(cleaned, "service", "go.mod")) &&
               Directory.Exists(Path.Combine(cleaned, "src")) &&
               Directory.Exists(Path.Combine(cleaned, "service"));
    }

    private static string NormalizeRepositoryReferencePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var cleanedPath = Path.GetFullPath(path);
        var repositoryRoot = ResolveRepositoryRootForStatic();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return cleanedPath;
        }

        var sourcesRoot = Path.Combine(repositoryRoot, ProductConstants.SourceDirName);
        if (IsSameOrChildPath(cleanedPath, sourcesRoot))
        {
            var relativePath = Path.GetRelativePath(sourcesRoot, cleanedPath);
            return Path.GetFullPath(Path.Combine(repositoryRoot, "reference", "upstream", relativePath));
        }

        var pluginsRoot = Path.Combine(repositoryRoot, "plugins");
        if (IsSameOrChildPath(cleanedPath, pluginsRoot))
        {
            var relativePath = Path.GetRelativePath(pluginsRoot, cleanedPath);
            return Path.GetFullPath(Path.Combine(repositoryRoot, "reference", "plugins", relativePath));
        }

        return cleanedPath;
    }

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        var cleanedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        var cleanedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);

        return cleanedCandidate.Equals(cleanedParent, StringComparison.OrdinalIgnoreCase) ||
               cleanedCandidate.StartsWith(cleanedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed record CodexModeStateModel
    {
        public string ProductName { get; set; } = string.Empty;
        public string Mode { get; set; } = "official";
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed record CpaRuntimeStateModel
    {
        public string ProductName { get; set; } = string.Empty;
        public string SourceRoot { get; set; } = string.Empty;
        public string ManagedBinary { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;
        public string LogPath { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public int Pid { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed record PluginCatalogFileModel
    {
        public string ProductName { get; set; } = string.Empty;
        public string SourceRoot { get; set; } = string.Empty;
        public string MarketplacePath { get; set; } = string.Empty;
        public string CatalogSource { get; set; } = string.Empty;
        public List<PluginCatalogEntryModel> Plugins { get; set; } = [];
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed record PluginCatalogEntryModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string ReadmePath { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string RepositoryUrl { get; set; } = string.Empty;
        public string RepositoryRef { get; set; } = string.Empty;
        public string RepositorySubdir { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
    }

    private sealed record PluginStateFileModel
    {
        public string ProductName { get; set; } = string.Empty;
        public Dictionary<string, PluginRuntimeStateModel> Plugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed record PluginRuntimeStateModel
    {
        public string Id { get; set; } = string.Empty;
        public bool Installed { get; set; }
        public bool Enabled { get; set; }
        public string InstalledVersion { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed record PluginSourceBundleModel
    {
        public string Kind { get; set; } = string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;
        public List<ReferenceBundleEntryModel> Entries { get; set; } = [];
        public DateTimeOffset GeneratedAt { get; set; }
    }

    private sealed record ManagedSourceBundleModel
    {
        public string Kind { get; set; } = string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;
        public List<ReferenceBundleEntryModel> Entries { get; set; } = [];
        public DateTimeOffset GeneratedAt { get; set; }
    }

    private sealed record ReferenceBundleEntryModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;
        public string SyncedPath { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string ManifestFile { get; set; } = string.Empty;
    }

    private sealed record ReferenceSourceManifestModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string Commit { get; set; } = string.Empty;
        public DateTimeOffset SyncedAt { get; set; }
    }

    private sealed record PluginPackageManifestModel
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Homepage { get; set; } = string.Empty;
        public PluginPackageInterfaceModel Interface { get; set; } = new();
    }

    private sealed record PluginPackageInterfaceModel
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    private sealed record CpaConfigFileModel
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public TlsModel Tls { get; set; } = new();
        public RemoteManagementModel RemoteManagement { get; set; } = new();
        public CodexAppServerProxyModel CodexAppServerProxy { get; set; } = new();
    }

    private sealed record TlsModel
    {
        public bool Enable { get; set; }
    }

    private sealed record RemoteManagementModel
    {
        [YamlMember(Alias = "allow-remote")]
        public bool AllowRemote { get; set; }

        [YamlMember(Alias = "secret-key")]
        public string SecretKey { get; set; } = string.Empty;

        [YamlMember(Alias = "disable-control-panel")]
        public bool DisableControlPanel { get; set; }

        [YamlMember(Alias = "panel-github-repository")]
        public string PanelGitHubRepository { get; set; } = string.Empty;
    }

    private sealed record CodexAppServerProxyModel
    {
        public bool Enable { get; set; }

        [YamlMember(Alias = "restrict-to-localhost")]
        public bool RestrictToLocalhost { get; set; }

        [YamlMember(Alias = "codex-bin")]
        public string CodexBin { get; set; } = string.Empty;
    }
}
