namespace CodexCliPlus.OfflineBuilder;

internal enum OfflineForceRebuild
{
    None,
    WebUi,
    Publish,
    Installer,
    All,
}

internal sealed record OfflineBuilderOptions(
    string RepositoryRoot,
    string Version,
    string Runtime,
    string OutputRoot,
    string DesktopRoot,
    OfflineForceRebuild ForceRebuild
)
{
    public const string DefaultVersion = "1.0.0";
    public const string DefaultRuntime = "win-x64";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out OfflineBuilderOptions options,
        out string? error
    )
    {
        return TryParse(args, FindRepositoryRoot(), out options, out error);
    }

    public static bool TryParse(
        IReadOnlyList<string> args,
        string repositoryRoot,
        out OfflineBuilderOptions options,
        out string? error
    )
    {
        error = null;
        var version = DefaultVersion;
        var runtime = DefaultRuntime;
        string? outputRoot = null;
        string? desktopRoot = null;
        var forceRebuild = OfflineForceRebuild.None;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                options = Create(
                    repositoryRoot,
                    version,
                    runtime,
                    outputRoot,
                    desktopRoot,
                    forceRebuild
                );
                error = $"无法识别的参数：{arg}";
                return false;
            }

            if (index + 1 >= args.Count)
            {
                options = Create(
                    repositoryRoot,
                    version,
                    runtime,
                    outputRoot,
                    desktopRoot,
                    forceRebuild
                );
                error = $"选项缺少取值：{arg}";
                return false;
            }

            var value = args[++index];
            switch (arg)
            {
                case "--version":
                    version = value;
                    break;
                case "--runtime":
                    runtime = value;
                    break;
                case "--output":
                    outputRoot = value;
                    break;
                case "--desktop":
                    desktopRoot = value;
                    break;
                case "--force-rebuild":
                    if (!TryParseForceRebuild(value, out forceRebuild))
                    {
                        options = Create(
                            repositoryRoot,
                            version,
                            runtime,
                            outputRoot,
                            desktopRoot,
                            forceRebuild
                        );
                        error =
                            "无效的强制重建阶段："
                            + value
                            + "。可用值：none、webui、publish、installer、all。";
                        return false;
                    }

                    break;
                default:
                    options = Create(
                        repositoryRoot,
                        version,
                        runtime,
                        outputRoot,
                        desktopRoot,
                        forceRebuild
                    );
                    error = $"未知选项：{arg}";
                    return false;
            }
        }

        options = Create(repositoryRoot, version, runtime, outputRoot, desktopRoot, forceRebuild);
        return true;
    }

    public static string ToBuildToolValue(OfflineForceRebuild forceRebuild)
    {
        return forceRebuild switch
        {
            OfflineForceRebuild.WebUi => "webui",
            OfflineForceRebuild.Publish => "publish",
            OfflineForceRebuild.Installer => "installer",
            OfflineForceRebuild.All => "all",
            _ => "none",
        };
    }

    private static OfflineBuilderOptions Create(
        string repositoryRoot,
        string version,
        string runtime,
        string? outputRoot,
        string? desktopRoot,
        OfflineForceRebuild forceRebuild
    )
    {
        var fullRepositoryRoot = Path.GetFullPath(repositoryRoot);
        return new OfflineBuilderOptions(
            fullRepositoryRoot,
            string.IsNullOrWhiteSpace(version) ? DefaultVersion : version.Trim(),
            string.IsNullOrWhiteSpace(runtime) ? DefaultRuntime : runtime.Trim(),
            ResolvePath(
                fullRepositoryRoot,
                string.IsNullOrWhiteSpace(outputRoot)
                    ? Path.Combine("artifacts", "buildtool")
                    : outputRoot.Trim()
            ),
            ResolvePath(
                fullRepositoryRoot,
                string.IsNullOrWhiteSpace(desktopRoot)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : desktopRoot.Trim()
            ),
            forceRebuild
        );
    }

    private static string ResolvePath(string repositoryRoot, string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, repositoryRoot);
    }

    private static bool TryParseForceRebuild(string value, out OfflineForceRebuild forceRebuild)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "none":
                forceRebuild = OfflineForceRebuild.None;
                return true;
            case "webui":
            case "web-ui":
                forceRebuild = OfflineForceRebuild.WebUi;
                return true;
            case "publish":
                forceRebuild = OfflineForceRebuild.Publish;
                return true;
            case "installer":
                forceRebuild = OfflineForceRebuild.Installer;
                return true;
            case "all":
                forceRebuild = OfflineForceRebuild.All;
                return true;
            default:
                forceRebuild = OfflineForceRebuild.None;
                return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        foreach (
            var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
        )
        {
            var current = new DirectoryInfo(startPath);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "CodexCliPlus.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new OfflineBuilderException(
            "未找到 CodexCliPlus 仓库根目录。请把构建器放在仓库根目录后再运行。"
        );
    }
}
