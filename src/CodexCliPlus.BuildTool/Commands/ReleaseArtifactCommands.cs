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

public static class ReleaseArtifactCommands
{
    public static async Task<int> WriteChecksumsAsync(BuildContext context)
    {
        var files = EnumeratePublicPackageFiles(context)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                path => ToRepositoryRelativePath(context, path),
                StringComparer.OrdinalIgnoreCase
            )
            .ToArray();

        if (files.Length == 0)
        {
            context.Logger.Error("No release artifacts found for checksum generation.");
            return 1;
        }

        var artifacts = new List<object>();
        var checksumLines = new List<string>();
        foreach (var file in files)
        {
            var sha256 = await ComputeSha256Async(file);
            var relativePath = ToRepositoryRelativePath(context, file);
            var signature = await ArtifactSignatureMetadata.ReadForArtifactAsync(file);
            var signatureMetadataPath = signature is null
                ? null
                : ToRepositoryRelativePath(context, signature.MetadataPath);
            checksumLines.Add($"{sha256}  {relativePath}");
            artifacts.Add(
                new
                {
                    path = relativePath,
                    fileName = Path.GetFileName(file),
                    size = new FileInfo(file).Length,
                    sha256,
                    purpose = GetPublicArtifactPurpose(context, file),
                    signed = signature?.Metadata.HasSignature ?? false,
                    signatureKind = signature?.Metadata.SignatureKind ?? "none",
                    signatureMetadataPath,
                    attestationExpected = signature?.Metadata.AttestationExpected ?? true,
                }
            );
        }

        Directory.CreateDirectory(context.Options.OutputRoot);
        await File.WriteAllLinesAsync(
            context.ChecksumsPath,
            checksumLines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        var manifest = new
        {
            product = AppConstants.ProductName,
            version = context.Options.Version,
            runtime = context.Options.Runtime,
            configuration = context.Options.Configuration,
            packages = context.Options.ReleasePackages.ToManifestValues(),
            generatedAtUtc = DateTimeOffset.UtcNow,
            signing = SigningOptions.FromEnvironment().SigningRequired
                ? "required"
                : "unsigned-or-optional",
            attestation = new { provider = "github-artifact-attestation", expected = true },
            artifacts,
        };
        await File.WriteAllTextAsync(
            context.ReleaseManifestPath,
            JsonSerializer.Serialize(manifest, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );

        context.Logger.Info($"checksums: {context.ChecksumsPath}");
        context.Logger.Info($"release manifest: {context.ReleaseManifestPath}");
        return 0;
    }

    public static async Task<int> ExportPublicReleaseAsync(BuildContext context)
    {
        var checksumExitCode = await WriteChecksumsAsync(context);
        if (checksumExitCode != 0)
        {
            return checksumExitCode;
        }

        SafeFileSystem.CleanDirectory(context.PublicReleaseRoot, context.Options.OutputRoot);
        Directory.CreateDirectory(context.PublicReleaseRoot);

        var files = EnumeratePublicPackageFiles(context)
            .Concat([context.ReleaseManifestPath])
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            context.Logger.Error("No public release files found for export.");
            return 1;
        }

        foreach (var file in files)
        {
            File.Copy(
                file,
                Path.Combine(context.PublicReleaseRoot, Path.GetFileName(file)),
                overwrite: true
            );
            context.Logger.Info($"public release file: {Path.GetFileName(file)}");
        }

        context.Logger.Info($"public release export: {context.PublicReleaseRoot}");
        return 0;
    }

    private static IEnumerable<string> EnumeratePublicPackageFiles(BuildContext context)
    {
        foreach (var file in EnumerateFilesIfExists(context.PackageRoot))
        {
            if (IsPublicPackageFile(context, file))
            {
                yield return file;
            }
        }
    }

    private static bool IsPublicPackageFile(BuildContext context, string path)
    {
        var fileName = Path.GetFileName(path);
        var onlineInstallerName =
            $"{AppConstants.InstallerNamePrefix}.Online.{context.Options.Version}.exe";
        var offlineInstallerName =
            $"{AppConstants.InstallerNamePrefix}.Offline.{context.Options.Version}.exe";
        var updatePackageName =
            $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip";

        return (
                context.Options.ReleasePackages.IncludesOnlineInstaller()
                && string.Equals(fileName, onlineInstallerName, StringComparison.OrdinalIgnoreCase)
            )
            || (
                context.Options.ReleasePackages.IncludesOfflineInstaller()
                && string.Equals(fileName, offlineInstallerName, StringComparison.OrdinalIgnoreCase)
            )
            || (
                context.Options.ReleasePackages.IncludesUpdatePackage()
                && string.Equals(fileName, updatePackageName, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static string GetPublicArtifactPurpose(BuildContext context, string path)
    {
        var fileName = Path.GetFileName(path);
        if (
            string.Equals(
                fileName,
                $"{AppConstants.InstallerNamePrefix}.Online.{context.Options.Version}.exe",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "在线安装器：不内置应用 payload 和 WebView2 安装器，安装时下载更新包与 WebView2 bootstrapper。";
        }

        if (
            string.Equals(
                fileName,
                $"{AppConstants.InstallerNamePrefix}.Offline.{context.Options.Version}.exe",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "离线安装器：内置 WebView2 Standalone，适合无网或缺少运行时的兜底安装。";
        }

        if (
            string.Equals(
                fileName,
                $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "桌面端增量更新包：由已安装应用的更新器使用，不面向首次安装。";
        }

        return "公开发布产物";
    }

    private static IEnumerable<string> EnumerateFilesIfExists(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            : [];
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string ToRepositoryRelativePath(BuildContext context, string path)
    {
        return Path.GetRelativePath(context.Options.RepositoryRoot, path).Replace('\\', '/');
    }
}
