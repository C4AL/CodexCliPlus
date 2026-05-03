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

public sealed class PackageVerifier
{
    private readonly BuildContext context;
    private readonly SigningOptions signingOptions;

    public PackageVerifier(BuildContext context, SigningOptions? signingOptions = null)
    {
        this.context = context;
        this.signingOptions = signingOptions ?? SigningOptions.FromEnvironment();
    }

    public IReadOnlyList<string> VerifyAll()
    {
        var failures = new List<string>();
        VerifyInstallerPackage(InstallerPackageKind.Online, failures);
        VerifyInstallerPackage(InstallerPackageKind.Offline, failures);
        VerifyUpdatePackage(failures);

        return failures;
    }

    private void VerifyInstallerPackage(InstallerPackageKind packageKind, List<string> failures)
    {
        var packageMoniker = packageKind == InstallerPackageKind.Online ? "Online" : "Offline";
        var installerName =
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.exe";
        var installerPath = Path.Combine(context.PackageRoot, installerName);

        VerifyExecutable(installerPath, failures);
        VerifyArtifactMetadata(installerPath, expectedSignatureKind: "authenticode", failures);

        if (!signingOptions.SigningRequired)
        {
            return;
        }

        VerifySignatureMetadata(installerPath, "authenticode", expectedSigned: true, failures);
    }

    private static void VerifyZipAbsent(string path, string forbiddenEntry, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var found = archive.Entries.Any(entry =>
                string.Equals(
                    NormalizeEntryName(entry.FullName),
                    forbiddenEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (found)
            {
                failures.Add($"Package '{path}' should not contain entry '{forbiddenEntry}'.");
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private static void VerifyZip(string path, string requiredEntry, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var found = archive.Entries.Any(entry =>
                string.Equals(
                    NormalizeEntryName(entry.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (!found)
            {
                failures.Add($"Package '{path}' is missing entry '{requiredEntry}'.");
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private static void VerifyZipPrefix(string path, string requiredPrefix, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var found = archive.Entries.Any(entry =>
                NormalizeEntryName(entry.FullName)
                    .StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase)
            );
            if (!found)
            {
                failures.Add($"Package '{path}' is missing entries under '{requiredPrefix}'.");
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private void VerifyUpdatePackage(List<string> failures)
    {
        var updatePackagePath = Path.Combine(
            context.PackageRoot,
            $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        VerifyZip(updatePackagePath, "update-manifest.json", failures);
        VerifyZipExecutable(updatePackagePath, $"payload/{AppConstants.ExecutableName}", failures);
        VerifyArtifactMetadata(
            updatePackagePath,
            expectedSignatureKind: "github-artifact-attestation",
            failures
        );

        if (signingOptions.SigningRequired)
        {
            VerifySignatureMetadata(
                updatePackagePath,
                "github-artifact-attestation",
                expectedSigned: false,
                failures
            );
        }
    }

    private static void VerifyArtifactMetadata(
        string artifactPath,
        string expectedSignatureKind,
        List<string> failures
    )
    {
        var signaturePath = ArtifactSignatureMetadata.GetSignaturePath(artifactPath);
        var unsignedPath = ArtifactSignatureMetadata.GetUnsignedPath(artifactPath);
        if (File.Exists(signaturePath))
        {
            VerifySignatureMetadataFile(
                signaturePath,
                expectedSignatureKind,
                requireSignedState: false,
                failures
            );
            return;
        }

        if (File.Exists(unsignedPath))
        {
            VerifySignatureMetadataFile(
                unsignedPath,
                expectedSignatureKind: "none",
                requireSignedState: false,
                failures
            );
            return;
        }

        failures.Add($"Signature sidecar missing: {signaturePath} or {unsignedPath}");
    }

    private static void VerifyZipExecutable(
        string path,
        string requiredEntry,
        List<string> failures
    )
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(
                    NormalizeEntryName(item.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (entry is null)
            {
                failures.Add($"Package '{path}' is missing entry '{requiredEntry}'.");
                return;
            }

            if (entry.Length < 64)
            {
                failures.Add(
                    $"Installer executable is too small to be valid: {path}!{requiredEntry}"
                );
                return;
            }

            using var stream = entry.Open();
            var validationFailure = WindowsExecutableValidation.ValidateStream(
                stream,
                $"{path}!{requiredEntry}"
            );
            if (validationFailure is not null)
            {
                failures.Add(validationFailure);
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private static void VerifyExecutable(string path, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Installer executable missing: {path}");
            return;
        }

        var validationFailure = WindowsExecutableValidation.ValidateFile(path);
        if (validationFailure is not null)
        {
            failures.Add(validationFailure);
        }
    }

    private static void VerifySignatureMetadata(
        string artifactPath,
        string expectedSignatureKind,
        bool expectedSigned,
        List<string> failures
    )
    {
        var metadataPath = ArtifactSignatureMetadata.GetSignaturePath(artifactPath);
        if (!File.Exists(metadataPath))
        {
            failures.Add($"Signature metadata missing: {metadataPath}");
            return;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ArtifactSignatureMetadata>(
                File.ReadAllText(metadataPath, Encoding.UTF8),
                JsonDefaults.Options
            );
            VerifySignatureMetadata(
                metadata,
                metadataPath,
                expectedSignatureKind,
                expectedSigned,
                failures
            );
        }
        catch (JsonException exception)
        {
            failures.Add(
                $"Signature metadata is not valid JSON: {metadataPath}: {exception.Message}"
            );
        }
    }

    private static void VerifySignatureMetadataFile(
        string metadataPath,
        string expectedSignatureKind,
        bool requireSignedState,
        List<string> failures
    )
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<ArtifactSignatureMetadata>(
                File.ReadAllText(metadataPath, Encoding.UTF8),
                JsonDefaults.Options
            );
            if (metadata is null)
            {
                failures.Add($"Signature metadata is empty: {metadataPath}");
                return;
            }

            if (!string.Equals(metadata.SignatureKind, expectedSignatureKind, StringComparison.Ordinal))
            {
                failures.Add(
                    $"Signature metadata kind mismatch: {metadataPath}; expected {expectedSignatureKind}, got {metadata.SignatureKind}."
                );
            }

            if (
                requireSignedState
                && (!metadata.HasSignature || !metadata.Verified || metadata.Signing != "signed")
            )
            {
                failures.Add($"Artifact is not recorded as signed and verified: {metadataPath}");
            }
        }
        catch (JsonException exception)
        {
            failures.Add(
                $"Signature metadata is not valid JSON: {metadataPath}: {exception.Message}"
            );
        }
    }

    private static void VerifyZipSignatureMetadata(
        string packagePath,
        string requiredEntry,
        string expectedSignatureKind,
        bool expectedSigned,
        List<string> failures
    )
    {
        if (!File.Exists(packagePath))
        {
            failures.Add($"Package missing: {packagePath}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(
                    NormalizeEntryName(item.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (entry is null)
            {
                failures.Add($"Package '{packagePath}' is missing entry '{requiredEntry}'.");
                return;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var metadata = JsonSerializer.Deserialize<ArtifactSignatureMetadata>(
                reader.ReadToEnd(),
                JsonDefaults.Options
            );
            VerifySignatureMetadata(
                metadata,
                $"{packagePath}!{requiredEntry}",
                expectedSignatureKind,
                expectedSigned,
                failures
            );
        }
        catch (InvalidDataException exception)
        {
            failures.Add(
                $"Package '{packagePath}' is not a readable zip archive: {exception.Message}"
            );
        }
        catch (JsonException exception)
        {
            failures.Add(
                $"Signature metadata in package is not valid JSON: {packagePath}!{requiredEntry}: {exception.Message}"
            );
        }
    }

    private static void VerifySignatureMetadata(
        ArtifactSignatureMetadata? metadata,
        string displayPath,
        string expectedSignatureKind,
        bool expectedSigned,
        List<string> failures
    )
    {
        if (metadata is null)
        {
            failures.Add($"Signature metadata is empty: {displayPath}");
            return;
        }

        if (!string.Equals(metadata.SignatureKind, expectedSignatureKind, StringComparison.Ordinal))
        {
            failures.Add(
                $"Signature metadata kind mismatch: {displayPath}; expected {expectedSignatureKind}, got {metadata.SignatureKind}."
            );
        }

        if (!metadata.AttestationExpected)
        {
            failures.Add(
                $"Signature metadata does not require artifact attestation: {displayPath}"
            );
        }

        if (
            expectedSigned
            && (!metadata.HasSignature || !metadata.Verified || metadata.Signing != "signed")
        )
        {
            failures.Add($"Artifact is not recorded as signed and verified: {displayPath}");
        }

        if (!expectedSigned && metadata.Signing != "not-applicable")
        {
            failures.Add(
                $"Artifact attestation metadata has unexpected signing state: {displayPath}"
            );
        }
    }

    private static string NormalizeEntryName(string name)
    {
        return name.Replace('\\', '/');
    }
}
