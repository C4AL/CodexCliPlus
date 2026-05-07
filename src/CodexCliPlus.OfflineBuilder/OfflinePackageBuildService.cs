using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.OfflineBuilder;

internal sealed class OfflinePackageBuildService
{
    private readonly IOfflineBuilderProcessRunner processRunner;
    private readonly IPortableToolchainResolver toolchainResolver;

    public OfflinePackageBuildService(
        IOfflineBuilderProcessRunner processRunner,
        IPortableToolchainResolver toolchainResolver
    )
    {
        this.processRunner = processRunner;
        this.toolchainResolver = toolchainResolver;
    }

    public async Task<string> BuildAsync(
        OfflineBuilderOptions options,
        CancellationToken cancellationToken = default
    )
    {
        EnsureRepositoryRoot(options.RepositoryRoot);
        Directory.CreateDirectory(options.OutputRoot);
        Directory.CreateDirectory(options.DesktopRoot);

        Console.WriteLine("正在检查本地便携构建工具链...");
        var toolchain = await toolchainResolver.EnsureAsync(options, cancellationToken);
        if (toolchain.DownloadedTools.Count > 0)
        {
            Console.WriteLine("已补齐工具：" + string.Join("、", toolchain.DownloadedTools));
        }

        await RunRequiredAsync(
            toolchain.DotNetExecutable,
            ["restore", "CodexCliPlus.sln", "--locked-mode"],
            options.RepositoryRoot,
            toolchain.Environment,
            "还原 .NET 依赖",
            cancellationToken
        );

        await RunBuildToolAsync(
            options,
            toolchain,
            [
                "clean-artifacts",
                "--repo-root",
                options.RepositoryRoot,
                "--output",
                options.OutputRoot,
                "--runtime",
                options.Runtime,
                "--version",
                options.Version,
            ],
            "清理 BuildTool 可交付产物",
            cancellationToken
        );

        await RunBuildToolAsync(
            options,
            toolchain,
            [
                "build-release",
                "--repo-root",
                options.RepositoryRoot,
                "--output",
                options.OutputRoot,
                "--runtime",
                options.Runtime,
                "--version",
                options.Version,
                "--packages",
                "offline",
                "--compression",
                "optimal",
                "--force-rebuild",
                OfflineBuilderOptions.ToBuildToolValue(options.ForceRebuild),
            ],
            "构建离线安装包",
            cancellationToken
        );

        var installerPath = Path.Combine(
            options.OutputRoot,
            "packages",
            $"{AppConstants.InstallerNamePrefix}.Offline.{options.Version}.exe"
        );
        var validationFailure = WindowsExecutableValidator.ValidateFile(installerPath);
        if (validationFailure is not null)
        {
            throw new OfflineBuilderException($"离线安装包校验失败：{validationFailure}");
        }

        var desktopInstallerPath = Path.Combine(
            options.DesktopRoot,
            Path.GetFileName(installerPath)
        );
        if (!PathsEqual(installerPath, desktopInstallerPath))
        {
            File.Move(installerPath, desktopInstallerPath, overwrite: true);
        }

        Console.WriteLine($"离线安装包已生成：{desktopInstallerPath}");
        return desktopInstallerPath;
    }

    private async Task RunBuildToolAsync(
        OfflineBuilderOptions options,
        ToolchainResolution toolchain,
        IReadOnlyList<string> buildToolArguments,
        string description,
        CancellationToken cancellationToken
    )
    {
        var buildToolProject = Path.Combine(
            options.RepositoryRoot,
            "src",
            "CodexCliPlus.BuildTool",
            "CodexCliPlus.BuildTool.csproj"
        );
        await RunRequiredAsync(
            toolchain.DotNetExecutable,
            [
                "run",
                "--project",
                buildToolProject,
                "--configuration",
                "Release",
                "--no-restore",
                "--",
                .. buildToolArguments,
            ],
            options.RepositoryRoot,
            toolchain.Environment,
            description,
            cancellationToken
        );
    }

    private async Task RunRequiredAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string description,
        CancellationToken cancellationToken
    )
    {
        Console.WriteLine(description + "...");
        var result = await processRunner.RunAsync(
            fileName,
            arguments,
            workingDirectory,
            environment,
            cancellationToken
        );
        if (result.ExitCode != 0)
        {
            throw new OfflineBuilderException($"{description}失败，退出码：{result.ExitCode}。");
        }
    }

    private static void EnsureRepositoryRoot(string repositoryRoot)
    {
        if (!File.Exists(Path.Combine(repositoryRoot, "CodexCliPlus.sln")))
        {
            throw new OfflineBuilderException($"仓库根目录无效：{repositoryRoot}");
        }

        var buildToolProject = Path.Combine(
            repositoryRoot,
            "src",
            "CodexCliPlus.BuildTool",
            "CodexCliPlus.BuildTool.csproj"
        );
        if (!File.Exists(buildToolProject))
        {
            throw new OfflineBuilderException($"未找到 BuildTool 项目：{buildToolProject}");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase
        );
    }
}
