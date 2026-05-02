using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Constants;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CodexCliPlus.BuildTool;

public static class ArtifactCleanupCommands
{
    public static int CleanAsync(BuildContext context)
    {
        foreach (
            var directory in new[]
            {
                context.PublishRoot,
                context.PackageRoot,
                context.PublicReleaseRoot,
                context.InstallerRoot,
                Path.Combine(context.Options.OutputRoot, "temp"),
            }
        )
        {
            SafeFileSystem.CleanDirectory(directory, context.Options.OutputRoot);
        }

        foreach (
            var file in new[]
            {
                context.ChecksumsPath,
                context.ReleaseManifestPath,
                Path.Combine(context.PublishRoot, "publish-manifest.json"),
            }
        )
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        context.Logger.Info("cleaned BuildTool publish, package, installer, and temp artifacts");
        return 0;
    }

    public static void ApplyArtifactRetention(BuildContext context)
    {
        if (context.Options.ArtifactRetention == 0)
        {
            return;
        }

        var repositoryArtifactsRoot = Path.Combine(context.Options.RepositoryRoot, "artifacts");
        var currentOutputRoot = Path.GetFullPath(context.Options.OutputRoot);
        if (!SafeFileSystem.IsBuildToolOutputRoot(currentOutputRoot, repositoryArtifactsRoot))
        {
            return;
        }

        if (!Directory.Exists(repositoryArtifactsRoot))
        {
            return;
        }

        var olderOutputRoots = Directory
            .EnumerateDirectories(
                repositoryArtifactsRoot,
                "buildtool*",
                SearchOption.TopDirectoryOnly
            )
            .Select(Path.GetFullPath)
            .Where(path => !SafeFileSystem.PathsEqual(path, currentOutputRoot))
            .Where(path => SafeFileSystem.IsBuildToolOutputRoot(path, repositoryArtifactsRoot))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToArray();
        var siblingRootsToKeep = Math.Max(context.Options.ArtifactRetention - 1, 0);

        foreach (var oldOutputRoot in olderOutputRoots.Skip(siblingRootsToKeep))
        {
            SafeFileSystem.DeleteBuildToolOutputRoot(
                oldOutputRoot,
                context.Options.RepositoryRoot,
                currentOutputRoot
            );
            context.Logger.Info($"removed old BuildTool output root: {oldOutputRoot}");
        }
    }
}
