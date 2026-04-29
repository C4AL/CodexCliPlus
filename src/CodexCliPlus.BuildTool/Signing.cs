using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexCliPlus.BuildTool;

public sealed record SigningOptions(
    bool SigningRequired,
    string? PfxBase64,
    string? PfxPassword,
    string TimestampUrl,
    string? SigntoolPath
)
{
    public const string RequiredEnvironmentName = "CODEXCLIPLUS_SIGNING_REQUIRED";
    public const string PfxEnvironmentName = "WINDOWS_CODESIGN_PFX_BASE64";
    public const string PasswordEnvironmentName = "WINDOWS_CODESIGN_PFX_PASSWORD";
    public const string TimestampEnvironmentName = "WINDOWS_CODESIGN_TIMESTAMP_URL";
    public const string SigntoolEnvironmentName = "WINDOWS_SIGNTOOL_PATH";

    public const string DefaultTimestampUrl = "http://timestamp.digicert.com";

    public bool HasCertificate =>
        !string.IsNullOrWhiteSpace(PfxBase64) && !string.IsNullOrWhiteSpace(PfxPassword);

    public bool HasPartialCertificateConfiguration =>
        !string.IsNullOrWhiteSpace(PfxBase64) || !string.IsNullOrWhiteSpace(PfxPassword);

    public static SigningOptions FromEnvironment()
    {
        return new SigningOptions(
            ParseBoolean(Environment.GetEnvironmentVariable(RequiredEnvironmentName)),
            Normalize(Environment.GetEnvironmentVariable(PfxEnvironmentName)),
            Environment.GetEnvironmentVariable(PasswordEnvironmentName),
            Normalize(Environment.GetEnvironmentVariable(TimestampEnvironmentName))
                ?? DefaultTimestampUrl,
            Normalize(Environment.GetEnvironmentVariable(SigntoolEnvironmentName))
        );
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ParseBoolean(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}

public static class SigningServiceFactory
{
    public static ISigningService CreateFromEnvironment()
    {
        var options = SigningOptions.FromEnvironment();
        return options.SigningRequired || options.HasPartialCertificateConfiguration
            ? new AuthenticodeSigningService(options)
            : new NoOpSigningService();
    }
}

public sealed class AuthenticodeSigningService(SigningOptions options) : ISigningService
{
    public async Task SignAsync(
        string artifactPath,
        BuildContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(artifactPath))
        {
            throw new FileNotFoundException($"Artifact to sign was not found: {artifactPath}");
        }

        if (!IsWindowsExecutable(artifactPath))
        {
            await ArtifactSignatureMetadata.WriteAttestationPlaceholderAsync(
                artifactPath,
                "Non-PE release artifact; GitHub artifact attestation is expected.",
                cancellationToken
            );
            context.Logger.Info($"artifact attestation expected: {artifactPath}");
            return;
        }

        var validationFailure = WindowsExecutableValidation.ValidateFile(artifactPath);
        if (validationFailure is not null)
        {
            throw new InvalidDataException(
                $"Cannot sign invalid Windows executable: {validationFailure}"
            );
        }

        if (!options.HasCertificate)
        {
            if (options.SigningRequired)
            {
                throw new InvalidOperationException(
                    "Windows Authenticode signing is required, but WINDOWS_CODESIGN_PFX_BASE64 "
                        + "and WINDOWS_CODESIGN_PFX_PASSWORD are not both configured."
                );
            }

            await ArtifactSignatureMetadata.WriteUnsignedAsync(
                artifactPath,
                "No signing certificate configured for this build.",
                cancellationToken
            );
            return;
        }

        var signtoolPath = ResolveSigntoolPath(options);
        var pfxPath = await WriteTemporaryPfxAsync(context, options.PfxBase64!, cancellationToken);
        try
        {
            var signExitCode = await context.ProcessRunner.RunAsync(
                signtoolPath,
                [
                    "sign",
                    "/f",
                    pfxPath,
                    "/p",
                    options.PfxPassword!,
                    "/fd",
                    "SHA256",
                    "/td",
                    "SHA256",
                    "/tr",
                    options.TimestampUrl,
                    artifactPath,
                ],
                context.Options.RepositoryRoot,
                context.Logger,
                cancellationToken: cancellationToken
            );
            if (signExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"signtool sign failed for '{artifactPath}' with exit code {signExitCode}."
                );
            }

            var verifyExitCode = await context.ProcessRunner.RunAsync(
                signtoolPath,
                ["verify", "/pa", "/v", artifactPath],
                context.Options.RepositoryRoot,
                context.Logger,
                cancellationToken: cancellationToken
            );
            if (verifyExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"signtool verify failed for '{artifactPath}' with exit code {verifyExitCode}."
                );
            }

            await ArtifactSignatureMetadata.WriteSignedAsync(
                artifactPath,
                "authenticode",
                options.TimestampUrl,
                cancellationToken
            );
            context.Logger.Info($"authenticode signed: {artifactPath}");
        }
        finally
        {
            DeleteTemporaryPfx(pfxPath);
        }
    }

    private static bool IsWindowsExecutable(string artifactPath)
    {
        return string.Equals(
            Path.GetExtension(artifactPath),
            ".exe",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static async Task<string> WriteTemporaryPfxAsync(
        BuildContext context,
        string pfxBase64,
        CancellationToken cancellationToken
    )
    {
        var signingRoot = Path.Combine(context.Options.OutputRoot, "temp", "signing");
        Directory.CreateDirectory(signingRoot);
        var pfxPath = Path.Combine(signingRoot, $"codesign-{Guid.NewGuid():N}.pfx");
        var bytes = Convert.FromBase64String(pfxBase64);
        await File.WriteAllBytesAsync(pfxPath, bytes, cancellationToken);
        CryptographicOperations.ZeroMemory(bytes);
        return pfxPath;
    }

    private static void DeleteTemporaryPfx(string pfxPath)
    {
        if (!File.Exists(pfxPath))
        {
            return;
        }

        try
        {
            var length = checked((int)new FileInfo(pfxPath).Length);
            File.WriteAllBytes(pfxPath, new byte[length]);
            File.Delete(pfxPath);
        }
        catch
        {
            File.Delete(pfxPath);
        }
    }

    private static string ResolveSigntoolPath(SigningOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SigntoolPath))
        {
            return options.SigntoolPath;
        }

        var pathCandidate = FindOnPath("signtool.exe");
        if (pathCandidate is not null)
        {
            return pathCandidate;
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var windowsKitRoot = Path.Combine(programFilesX86, "Windows Kits", "10", "bin");
        if (!Directory.Exists(windowsKitRoot))
        {
            return "signtool.exe";
        }

        try
        {
            return Directory
                    .EnumerateFiles(windowsKitRoot, "signtool.exe", SearchOption.AllDirectories)
                    .Where(path =>
                        path.Contains(
                            $"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault()
                ?? "signtool.exe";
        }
        catch
        {
            return "signtool.exe";
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (
            var segment in pathValue.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var candidatePath = Path.Combine(segment.Trim('"'), fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }
}

public sealed class ArtifactSignatureMetadata
{
    public string Artifact { get; init; } = string.Empty;

    public string Signing { get; init; } = string.Empty;

    public string SignatureKind { get; init; } = string.Empty;

    [JsonPropertyName("signed")]
    public bool HasSignature { get; init; }

    public bool Verified { get; init; }

    public bool AttestationExpected { get; init; }

    public string? TimestampUrl { get; init; }

    public string? Sha256 { get; init; }

    public string? Reason { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static string GetSignaturePath(string artifactPath)
    {
        return $"{artifactPath}.signature.json";
    }

    public static string GetUnsignedPath(string artifactPath)
    {
        return $"{artifactPath}.unsigned.json";
    }

    public static async Task WriteSignedAsync(
        string artifactPath,
        string signatureKind,
        string timestampUrl,
        CancellationToken cancellationToken
    )
    {
        await WriteAsync(
            GetSignaturePath(artifactPath),
            new ArtifactSignatureMetadata
            {
                Artifact = Path.GetFileName(artifactPath),
                Signing = "signed",
                SignatureKind = signatureKind,
                HasSignature = true,
                Verified = true,
                AttestationExpected = true,
                TimestampUrl = timestampUrl,
                Sha256 = await ComputeSha256Async(artifactPath, cancellationToken),
            },
            cancellationToken
        );
    }

    public static async Task WriteAttestationPlaceholderAsync(
        string artifactPath,
        string reason,
        CancellationToken cancellationToken
    )
    {
        await WriteAsync(
            GetSignaturePath(artifactPath),
            new ArtifactSignatureMetadata
            {
                Artifact = Path.GetFileName(artifactPath),
                Signing = "not-applicable",
                SignatureKind = "github-artifact-attestation",
                HasSignature = false,
                Verified = false,
                AttestationExpected = true,
                Reason = reason,
                Sha256 = await ComputeSha256Async(artifactPath, cancellationToken),
            },
            cancellationToken
        );
    }

    public static async Task WriteUnsignedAsync(
        string artifactPath,
        string reason,
        CancellationToken cancellationToken
    )
    {
        await WriteAsync(
            GetUnsignedPath(artifactPath),
            new ArtifactSignatureMetadata
            {
                Artifact = Path.GetFileName(artifactPath),
                Signing = "skipped",
                SignatureKind = "none",
                HasSignature = false,
                Verified = false,
                AttestationExpected = false,
                Reason = reason,
                Sha256 = File.Exists(artifactPath)
                    ? await ComputeSha256Async(artifactPath, cancellationToken)
                    : null,
            },
            cancellationToken
        );
    }

    public static async Task<ArtifactSignatureMetadataFile?> ReadForArtifactAsync(
        string artifactPath,
        CancellationToken cancellationToken = default
    )
    {
        foreach (
            var metadataPath in new[]
            {
                GetSignaturePath(artifactPath),
                GetUnsignedPath(artifactPath),
            }
        )
        {
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            await using var stream = File.OpenRead(metadataPath);
            var metadata = await JsonSerializer.DeserializeAsync<ArtifactSignatureMetadata>(
                stream,
                JsonDefaults.Options,
                cancellationToken
            );
            if (metadata is not null)
            {
                return new ArtifactSignatureMetadataFile(metadata, metadataPath);
            }
        }

        return null;
    }

    private static async Task WriteAsync(
        string metadataPath,
        ArtifactSignatureMetadata metadata,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(metadata, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken
        );
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}

public sealed record ArtifactSignatureMetadataFile(
    ArtifactSignatureMetadata Metadata,
    string MetadataPath
);
