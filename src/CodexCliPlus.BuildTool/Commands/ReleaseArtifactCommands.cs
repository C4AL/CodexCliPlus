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
        var files = EnumerateReleaseFiles(context)
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
                    size = new FileInfo(file).Length,
                    sha256,
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

    private static IEnumerable<string> EnumerateReleaseFiles(BuildContext context)
    {
        foreach (var file in EnumerateFilesIfExists(context.PackageRoot))
        {
            yield return file;
        }

        foreach (
            var file in new[]
            {
                context.AssetManifestPath,
                Path.Combine(context.PublishRoot, "publish-manifest.json"),
            }
        )
        {
            if (File.Exists(file))
            {
                yield return file;
            }
        }

        var sbomRoot = Path.Combine(context.Options.RepositoryRoot, "artifacts", "sbom");
        foreach (var file in EnumerateFilesIfExists(sbomRoot))
        {
            yield return file;
        }
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
