using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CPAD.Services;

public sealed class WebUiAssetLocator
{
    private const string BundledRoot = "assets\\webui";
    private const string ResourceRoot = "resources\\webui";
    private const string DistRelativePath = "upstream\\dist";
    private const string EntryFileName = "index.html";
    private const string SyncRelativePath = "upstream\\sync.json";

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance member is resolved through dependency injection.")]
    public WebUiBundleInfo GetRequiredBundle()
    {
        if (TryResolveFromBaseDirectory(out var bundled))
        {
            return bundled;
        }

        if (TryResolveFromRepository(out var repositoryBundle))
        {
            return repositoryBundle;
        }

        throw new FileNotFoundException("The vendored WebUI bundle is missing.");
    }

    private static bool TryResolveFromBaseDirectory(out WebUiBundleInfo bundle)
    {
        return TryResolve(Path.Combine(AppContext.BaseDirectory, BundledRoot), out bundle);
    }

    private static bool TryResolveFromRepository(out WebUiBundleInfo bundle)
    {
        var repositoryRoot = TryFindRepositoryRoot();
        if (repositoryRoot is null)
        {
            bundle = default!;
            return false;
        }

        return TryResolve(Path.Combine(repositoryRoot, ResourceRoot), out bundle);
    }

    private static bool TryResolve(string rootPath, out WebUiBundleInfo bundle)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        var distDirectory = Path.Combine(fullRoot, DistRelativePath);
        var entryPath = Path.Combine(distDirectory, EntryFileName);
        var metadataPath = Path.Combine(fullRoot, SyncRelativePath);

        if (!Directory.Exists(distDirectory) || !File.Exists(entryPath) || !File.Exists(metadataPath))
        {
            bundle = default!;
            return false;
        }

        bundle = new WebUiBundleInfo(fullRoot, distDirectory, entryPath, metadataPath);
        return true;
    }

    private static string? TryFindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "CliProxyApiDesktop.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}

public sealed record WebUiBundleInfo(
    string RootDirectory,
    string DistDirectory,
    string EntryPath,
    string MetadataPath);
