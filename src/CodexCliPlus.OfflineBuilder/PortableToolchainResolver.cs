namespace CodexCliPlus.OfflineBuilder;

internal sealed record ToolchainResolution(
    string DotNetExecutable,
    string NodeExecutable,
    string GoExecutable,
    IReadOnlyDictionary<string, string?> Environment,
    IReadOnlyList<string> DownloadedTools
);

internal interface IPortableToolchainResolver
{
    Task<ToolchainResolution> EnsureAsync(
        OfflineBuilderOptions options,
        CancellationToken cancellationToken = default
    );
}

internal sealed class PortableToolchainResolver : IPortableToolchainResolver
{
    private readonly IOfflineBuilderProcessRunner processRunner;
    private readonly IToolArchiveDownloader downloader;
    private readonly IToolArchiveExtractor extractor;

    public PortableToolchainResolver(
        IOfflineBuilderProcessRunner processRunner,
        IToolArchiveDownloader downloader,
        IToolArchiveExtractor extractor
    )
    {
        this.processRunner = processRunner;
        this.downloader = downloader;
        this.extractor = extractor;
    }

    public async Task<ToolchainResolution> EnsureAsync(
        OfflineBuilderOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var versions = ToolchainVersions.Read(options.RepositoryRoot);
        var toolCacheRoot = Path.Combine(options.RepositoryRoot, "artifacts", "toolcache");
        Directory.CreateDirectory(toolCacheRoot);

        var dotnetTask = ResolveDotNetAsync(toolCacheRoot, versions.DotNetSdk, cancellationToken);
        var nodeTask = ResolveNodeAsync(toolCacheRoot, versions.Node, cancellationToken);
        var goTask = ResolveGoAsync(toolCacheRoot, versions.Go, cancellationToken);
        await Task.WhenAll(dotnetTask, nodeTask, goTask);

        var dotnet = await dotnetTask;
        var node = await nodeTask;
        var go = await goTask;

        var pathDirectories = new[]
        {
            dotnet.PathDirectory,
            node.PathDirectory,
            go.PathDirectory,
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        var inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var path = string.Join(Path.PathSeparator, pathDirectories.Append(inheritedPath));
        var npmCache = Path.Combine(options.OutputRoot, "cache", "npm");
        Directory.CreateDirectory(npmCache);

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = path,
            ["DOTNET_ROOT"] = dotnet.RootDirectory,
            ["GOROOT"] = go.RootDirectory,
            ["GOTOOLCHAIN"] = "local",
            ["NPM_CONFIG_CACHE"] = npmCache,
        };

        return new ToolchainResolution(
            dotnet.ExecutablePath,
            node.ExecutablePath,
            go.ExecutablePath,
            environment,
            new[] { dotnet, node, go }
                .Where(tool => tool.Downloaded)
                .Select(tool => tool.DisplayName)
                .ToArray()
        );
    }

    private Task<ResolvedTool> ResolveDotNetAsync(
        string toolCacheRoot,
        string version,
        CancellationToken cancellationToken
    )
    {
        var targetRoot = Path.Combine(toolCacheRoot, "dotnet", version, "win-x64");
        return ResolveToolAsync(
            new ToolRequirement(
                "dotnet",
                ".NET SDK",
                version,
                targetRoot,
                "dotnet.exe",
                null,
                new Uri(
                    $"https://dotnetcli.azureedge.net/dotnet/Sdk/{version}/dotnet-sdk-{version}-win-x64.zip"
                ),
                ["dotnet.exe", "dotnet"],
                static (output, requiredVersion) =>
                    string.Equals(
                        output.Trim(),
                        requiredVersion,
                        StringComparison.OrdinalIgnoreCase
                    ),
                root => root,
                root => root
            ),
            cancellationToken
        );
    }

    private Task<ResolvedTool> ResolveNodeAsync(
        string toolCacheRoot,
        string version,
        CancellationToken cancellationToken
    )
    {
        var targetRoot = Path.Combine(toolCacheRoot, "node", version, "win-x64");
        return ResolveToolAsync(
            new ToolRequirement(
                "node",
                "Node.js",
                version,
                targetRoot,
                "node.exe",
                $"node-v{version}-win-x64",
                new Uri($"https://nodejs.org/dist/v{version}/node-v{version}-win-x64.zip"),
                ["node.exe", "node"],
                static (output, requiredVersion) =>
                    string.Equals(
                        output.Trim().TrimStart('v'),
                        requiredVersion,
                        StringComparison.OrdinalIgnoreCase
                    ),
                root => root,
                root => root
            ),
            cancellationToken
        );
    }

    private async Task<ResolvedTool> ResolveGoAsync(
        string toolCacheRoot,
        string version,
        CancellationToken cancellationToken
    )
    {
        var targetRoot = Path.Combine(toolCacheRoot, "go", version, "windows-amd64", "go");
        var resolved = await ResolveToolAsync(
            new ToolRequirement(
                "go",
                "Go",
                version,
                targetRoot,
                Path.Combine("bin", "go.exe"),
                "go",
                new Uri($"https://go.dev/dl/go{version}.windows-amd64.zip"),
                ["go.exe", "go"],
                static (output, requiredVersion) =>
                    output.Contains($"go{requiredVersion} ", StringComparison.OrdinalIgnoreCase)
                    || output
                        .TrimEnd()
                        .EndsWith($"go{requiredVersion}", StringComparison.OrdinalIgnoreCase),
                root => Path.Combine(root, "bin"),
                root => root
            ),
            cancellationToken
        );

        if (!resolved.FromCache)
        {
            var goRoot = await QueryGoRootAsync(resolved.ExecutablePath, cancellationToken);
            return resolved with
            {
                RootDirectory = string.IsNullOrWhiteSpace(goRoot) ? resolved.RootDirectory : goRoot,
                PathDirectory = string.IsNullOrWhiteSpace(goRoot)
                    ? resolved.PathDirectory
                    : Path.Combine(goRoot, "bin"),
            };
        }

        return resolved;
    }

    private async Task<ResolvedTool> ResolveToolAsync(
        ToolRequirement requirement,
        CancellationToken cancellationToken
    )
    {
        var cachedExecutable = Path.Combine(
            requirement.TargetRoot,
            requirement.ExecutableRelativePath
        );
        if (
            await ValidateToolAsync(
                cachedExecutable,
                requirement.Version,
                requirement.VersionValidator,
                cancellationToken
            )
        )
        {
            return CreateResolvedTool(
                requirement,
                cachedExecutable,
                fromCache: true,
                downloaded: false
            );
        }

        var systemExecutable = await ResolveSystemToolAsync(requirement, cancellationToken);
        if (systemExecutable is not null)
        {
            return CreateResolvedTool(
                requirement,
                systemExecutable,
                fromCache: false,
                downloaded: false
            );
        }

        await DownloadAndExtractAsync(requirement, cancellationToken);
        if (
            !await ValidateToolAsync(
                cachedExecutable,
                requirement.Version,
                requirement.VersionValidator,
                cancellationToken
            )
        )
        {
            throw new OfflineBuilderException(
                $"工具缓存校验失败：{requirement.DisplayName} {requirement.Version}。"
            );
        }

        return CreateResolvedTool(requirement, cachedExecutable, fromCache: true, downloaded: true);
    }

    private async Task<string?> ResolveSystemToolAsync(
        ToolRequirement requirement,
        CancellationToken cancellationToken
    )
    {
        foreach (var executableName in requirement.SystemExecutableNames)
        {
            foreach (var directory in EnumeratePathDirectories())
            {
                var candidate = Path.Combine(directory, executableName);
                if (
                    await ValidateToolAsync(
                        candidate,
                        requirement.Version,
                        requirement.VersionValidator,
                        cancellationToken
                    )
                )
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private async Task<bool> ValidateToolAsync(
        string executablePath,
        string version,
        Func<string, string, bool> validator,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(executablePath))
        {
            return false;
        }

        try
        {
            var arguments = Path.GetFileNameWithoutExtension(executablePath)
                .Equals("go", StringComparison.OrdinalIgnoreCase)
                ? new[] { "version" }
                : new[] { "--version" };
            var result = await processRunner.RunAsync(
                executablePath,
                arguments,
                Directory.GetCurrentDirectory(),
                cancellationToken: cancellationToken
            );
            return result.ExitCode == 0 && validator(result.StandardOutput, version);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> QueryGoRootAsync(
        string executablePath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var result = await processRunner.RunAsync(
                executablePath,
                ["env", "GOROOT"],
                Directory.GetCurrentDirectory(),
                cancellationToken: cancellationToken
            );
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task DownloadAndExtractAsync(
        ToolRequirement requirement,
        CancellationToken cancellationToken
    )
    {
        var parentRoot = Directory.GetParent(requirement.TargetRoot)!.FullName;
        Directory.CreateDirectory(parentRoot);
        var downloadPath = Path.Combine(
            parentRoot,
            $"{requirement.Id}-{requirement.Version}.zip.download"
        );
        var extractRoot = Path.Combine(parentRoot, $"{requirement.Id}-{Guid.NewGuid():N}.extract");

        try
        {
            if (File.Exists(downloadPath))
            {
                File.Delete(downloadPath);
            }

            await downloader.DownloadAsync(
                requirement.DownloadUri,
                downloadPath,
                cancellationToken
            );
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }

            Directory.CreateDirectory(extractRoot);
            extractor.ExtractToDirectory(downloadPath, extractRoot);

            var extractedRoot = requirement.ArchiveRootDirectory is null
                ? extractRoot
                : Path.Combine(extractRoot, requirement.ArchiveRootDirectory);
            if (!Directory.Exists(extractedRoot))
            {
                throw new OfflineBuilderException(
                    $"工具压缩包结构不符合预期：{requirement.DisplayName}。"
                );
            }

            if (Directory.Exists(requirement.TargetRoot))
            {
                Directory.Delete(requirement.TargetRoot, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(requirement.TargetRoot)!);
            Directory.Move(extractedRoot, requirement.TargetRoot);
        }
        finally
        {
            if (File.Exists(downloadPath))
            {
                File.Delete(downloadPath);
            }

            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }
        }
    }

    private static ResolvedTool CreateResolvedTool(
        ToolRequirement requirement,
        string executablePath,
        bool fromCache,
        bool downloaded
    )
    {
        var root = fromCache
            ? requirement.RootDirectorySelector(requirement.TargetRoot)
            : InferRootDirectory(executablePath);
        return new ResolvedTool(
            requirement.Id,
            requirement.DisplayName,
            executablePath,
            root,
            fromCache
                ? requirement.PathDirectorySelector(requirement.TargetRoot)
                : Path.GetDirectoryName(executablePath)!,
            fromCache,
            downloaded
        );
    }

    private static string InferRootDirectory(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath)!;
        if (Path.GetFileName(directory).Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(directory)?.FullName ?? directory;
        }

        return directory;
    }

    private static IEnumerable<string> EnumeratePathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (
            var directory in path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var normalized = directory.Trim('"');
            if (Directory.Exists(normalized))
            {
                yield return normalized;
            }
        }
    }

    private sealed record ToolRequirement(
        string Id,
        string DisplayName,
        string Version,
        string TargetRoot,
        string ExecutableRelativePath,
        string? ArchiveRootDirectory,
        Uri DownloadUri,
        IReadOnlyList<string> SystemExecutableNames,
        Func<string, string, bool> VersionValidator,
        Func<string, string> PathDirectorySelector,
        Func<string, string> RootDirectorySelector
    );

    private sealed record ResolvedTool(
        string Id,
        string DisplayName,
        string ExecutablePath,
        string RootDirectory,
        string PathDirectory,
        bool FromCache,
        bool Downloaded
    );
}
