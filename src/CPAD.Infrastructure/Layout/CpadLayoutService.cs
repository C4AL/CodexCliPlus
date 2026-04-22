using CPAD.Application.Abstractions;
using CPAD.Domain;

namespace CPAD.Infrastructure.Layout;

public sealed class CpadLayoutService : ICpadLayoutService
{
    public CpadLayout Resolve()
    {
        var installRoot = ResolveInstallRoot();
        var dataDir = Path.Combine(installRoot, "data");
        var logsDir = Path.Combine(installRoot, "logs");
        var sourcesRoot = ResolveManagedSourcesRoot(installRoot);
        var officialCoreBaseline = Path.Combine(sourcesRoot, "official-backend");

        var layout = new CpadLayout
        {
            InstallRoot = installRoot,
            Directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["data"] = dataDir,
                ["codexData"] = Path.Combine(dataDir, "codex"),
                ["cpaData"] = Path.Combine(dataDir, "cpa"),
                ["codexRuntime"] = Path.Combine(installRoot, "runtime", "codex"),
                ["cpaRuntime"] = Path.Combine(installRoot, "runtime", "cpa"),
                ["plugins"] = Path.Combine(installRoot, "plugins"),
                ["logs"] = logsDir,
                ["tmp"] = Path.Combine(installRoot, "tmp"),
                ["sources"] = sourcesRoot,
                ["upstream"] = sourcesRoot,
                ["officialCoreBaseline"] = officialCoreBaseline
            },
            Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["database"] = Path.Combine(dataDir, "app.db"),
                ["serviceState"] = Path.Combine(dataDir, "service-state.json"),
                ["serviceLog"] = Path.Combine(logsDir, "service-host.log"),
                ["codexMode"] = Path.Combine(dataDir, "codex-mode.json"),
                ["cpaRuntimeState"] = Path.Combine(dataDir, "cpa-runtime.json"),
                ["cpaRuntimeLog"] = Path.Combine(logsDir, "cpa-runtime.log"),
                ["pluginCatalog"] = Path.Combine(dataDir, "plugin-catalog.json"),
                ["pluginState"] = Path.Combine(dataDir, "plugin-state.json"),
                ["updateCenterState"] = Path.Combine(dataDir, "update-center.json")
            }
        };

        EnsureDirectories(layout);
        return layout;
    }

    private static void EnsureDirectories(CpadLayout layout)
    {
        foreach (var key in new[]
                 {
                     "data",
                     "codexData",
                     "cpaData",
                     "codexRuntime",
                     "cpaRuntime",
                     "plugins",
                     "logs",
                     "tmp"
                 })
        {
            var directory = layout.Directories[key];
            Directory.CreateDirectory(directory);
        }
    }

    private static string ResolveInstallRoot()
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_INSTALL_ROOT");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        var repositoryRoot = ResolveRepositoryRoot();
        if (!string.IsNullOrWhiteSpace(repositoryRoot) && ShouldUseRepositoryInstallRoot(repositoryRoot))
        {
            return Path.Combine(repositoryRoot, "tmp", "cpad-dev-install");
        }

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var packagedRoot = ResolveInstallRootFromExecutable(executablePath);
            if (!string.IsNullOrWhiteSpace(packagedRoot))
            {
                return packagedRoot;
            }
        }

        return ResolveLegacyHomeInstallRoot();
    }

    private static string ResolveLegacyHomeInstallRoot()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDir))
        {
            return ProductConstants.InstallDirName;
        }

        return Path.Combine(homeDir, ProductConstants.InstallDirName);
    }

    private static bool ShouldUseRepositoryInstallRoot(string repositoryRoot)
    {
        var workingDirectory = Environment.CurrentDirectory;
        if (IsSameOrChildPath(workingDirectory, repositoryRoot))
        {
            return true;
        }

        var executablePath = Environment.ProcessPath;
        var executableDir = Path.GetDirectoryName(executablePath);
        return IsSameOrChildPath(executableDir, repositoryRoot);
    }

    private static string ResolveInstallRootFromExecutable(string executablePath)
    {
        var cleanedExecutable = Path.GetFullPath(executablePath);
        var baseName = Path.GetFileName(cleanedExecutable);

        if (IsKnownProductExecutable(baseName))
        {
            var executableDir = Path.GetDirectoryName(cleanedExecutable) ?? string.Empty;
            if (!Path.GetFileName(executableDir).Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                return executableDir;
            }

            var parentDir = Path.GetDirectoryName(executableDir) ?? executableDir;
            if (Path.GetFileName(parentDir).Equals("resources", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(parentDir) ?? parentDir;
            }

            return parentDir;
        }

        return string.Empty;
    }

    private static bool IsKnownProductExecutable(string fileName)
    {
        return fileName.Equals(ProductConstants.ProductName + ".exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("CPAD.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("CPAD.Desktop.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("CPAD.Service.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("cpad-service.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("codex.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveManagedSourcesRoot(string installRoot)
    {
        var repositorySourcesRoot = ResolveRepositorySourcesRoot();
        if (!string.IsNullOrWhiteSpace(repositorySourcesRoot))
        {
            return repositorySourcesRoot;
        }

        var candidates = new List<string>();
        AppendUniquePathCandidate(candidates, Path.Combine(installRoot, "reference", "upstream"));
        AppendUniquePathCandidate(candidates, Path.Combine(installRoot, ProductConstants.SourceDirName));
        AppendUniquePathCandidate(candidates, Path.Combine(installRoot, "resources", ProductConstants.SourceDirName));
        AppendUniquePathCandidate(candidates, Path.Combine(installRoot, "resources", "reference", "upstream"));
        AppendUniquePathCandidate(candidates, Path.Combine(installRoot, "upstream"));
        AppendUniquePathCandidate(candidates, Path.Combine(ResolveLegacyHomeInstallRoot(), "reference", "upstream"));
        AppendUniquePathCandidate(candidates, Path.Combine(ResolveLegacyHomeInstallRoot(), "upstream"));

        var resolved = FirstExistingDirectory(candidates);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        return candidates.Count > 0
            ? Path.GetFullPath(candidates[0])
            : Path.Combine(installRoot, ProductConstants.SourceDirName);
    }

    private static string ResolveRepositorySourcesRoot()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return string.Empty;
        }

        var candidates = new[]
        {
            Path.Combine(repositoryRoot, "reference", "upstream"),
            Path.Combine(repositoryRoot, ProductConstants.SourceDirName)
        };

        var existing = FirstExistingDirectory(candidates);
        return !string.IsNullOrWhiteSpace(existing)
            ? existing
            : candidates[0];
    }

    private static string ResolveRepositoryRoot()
    {
        var custom = Environment.GetEnvironmentVariable("CPAD_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(custom) && IsRepositoryRoot(custom))
        {
            return Path.GetFullPath(custom);
        }

        var candidates = new List<string>();
        AppendCurrentDirectoryCandidates(candidates);
        AppendExecutableDirectoryCandidates(candidates);

        return candidates.FirstOrDefault(IsRepositoryRoot) ?? string.Empty;
    }

    private static void AppendCurrentDirectoryCandidates(List<string> candidates)
    {
        var workingDirectory = Environment.CurrentDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return;
        }

        AppendDirectoryChain(candidates, workingDirectory);
    }

    private static void AppendExecutableDirectoryCandidates(List<string> candidates)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var executableDir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDir))
        {
            return;
        }

        AppendDirectoryChain(candidates, executableDir);
    }

    private static bool IsRepositoryRoot(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var cleanedCandidate = Path.GetFullPath(candidate);
        var currentLayoutFiles = new[]
        {
            Path.Combine(cleanedCandidate, "CPAD.sln"),
            Path.Combine(cleanedCandidate, "Directory.Build.props"),
            Path.Combine(cleanedCandidate, "apps", "CPAD.Service", "CPAD.Service.csproj"),
            Path.Combine(cleanedCandidate, "src", "CPAD.Infrastructure", "CPAD.Infrastructure.csproj")
        };

        if (currentLayoutFiles.All(File.Exists))
        {
            return true;
        }

        var requiredFiles = new[]
        {
            Path.Combine(cleanedCandidate, "package.json"),
            Path.Combine(cleanedCandidate, "service", "go.mod")
        };

        if (requiredFiles.Any(path => !File.Exists(path)))
        {
            return false;
        }

        var requiredDirectories = new[]
        {
            Path.Combine(cleanedCandidate, "src"),
            Path.Combine(cleanedCandidate, "service")
        };

        return requiredDirectories.All(Directory.Exists);
    }

    private static void AppendDirectoryChain(ICollection<string> candidates, string startPath)
    {
        var current = new DirectoryInfo(startPath);
        for (var depth = 0; depth < 6 && current is not null; depth++)
        {
            AppendUniquePathCandidate(candidates, current.FullName);
            current = current.Parent;
        }
    }

    private static void AppendUniquePathCandidate(ICollection<string> candidates, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var fullPath = Path.GetFullPath(candidate);
        if (candidates.Any(existing => SamePath(existing, fullPath)))
        {
            return;
        }

        candidates.Add(fullPath);
    }

    private static string FirstExistingDirectory(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return string.Empty;
    }

    private static bool SamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChildPath(string? candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var cleanedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        var cleanedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);

        return cleanedCandidate.Equals(cleanedParent, StringComparison.OrdinalIgnoreCase) ||
               cleanedCandidate.StartsWith(cleanedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
