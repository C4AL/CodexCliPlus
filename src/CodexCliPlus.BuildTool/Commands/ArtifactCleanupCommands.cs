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
}
