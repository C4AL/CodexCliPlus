using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CodexCliPlus.BuildTool;
using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.Tests.BuildTool;

public sealed class SigningTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-signing-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task AuthenticodeSigningUsesSha256TimestampAndWritesMetadata()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out");
        var artifactPath = Path.Combine(outputRoot, "packages", "CodexCliPlus.Setup.9.9.9.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllBytes(artifactPath, CreateExecutableBytes());
        var runner = new RecordingProcessRunner();
        var context = CreateContext(repositoryRoot, outputRoot, runner);
        var signingService = new AuthenticodeSigningService(
            new SigningOptions(
                SigningRequired: true,
                PfxBase64: Convert.ToBase64String([1, 2, 3, 4]),
                PfxPassword: "test-password",
                TimestampUrl: "http://timestamp.example.test",
                SigntoolPath: "signtool.exe"
            )
        );

        await signingService.SignAsync(artifactPath, context);

        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("signtool.exe", runner.Calls[0].FileName);
        Assert.Equal("sign", runner.Calls[0].Arguments[0]);
        Assert.Contains("/fd", runner.Calls[0].Arguments);
        Assert.Contains("SHA256", runner.Calls[0].Arguments);
        Assert.Contains("/td", runner.Calls[0].Arguments);
        Assert.Contains("/tr", runner.Calls[0].Arguments);
        Assert.Contains("http://timestamp.example.test", runner.Calls[0].Arguments);
        Assert.Equal("verify", runner.Calls[1].Arguments[0]);

        var pfxPath = runner.Calls[0].Arguments[runner.Calls[0].Arguments.IndexOf("/f") + 1];
        Assert.False(File.Exists(pfxPath));

        var metadata = ReadMetadata(ArtifactSignatureMetadata.GetSignaturePath(artifactPath));
        Assert.True(metadata.HasSignature);
        Assert.True(metadata.Verified);
        Assert.True(metadata.AttestationExpected);
        Assert.Equal("signed", metadata.Signing);
        Assert.Equal("authenticode", metadata.SignatureKind);
        Assert.Equal("http://timestamp.example.test", metadata.TimestampUrl);
    }

    [Fact]
    public async Task RequiredAuthenticodeSigningFailsWithoutCertificateSecrets()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-missing-secret");
        var artifactPath = Path.Combine(outputRoot, "packages", "CodexCliPlus.Setup.9.9.9.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllBytes(artifactPath, CreateExecutableBytes());
        var context = CreateContext(repositoryRoot, outputRoot, new RecordingProcessRunner());
        var signingService = new AuthenticodeSigningService(
            new SigningOptions(
                SigningRequired: true,
                PfxBase64: null,
                PfxPassword: null,
                TimestampUrl: SigningOptions.DefaultTimestampUrl,
                SigntoolPath: null
            )
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            signingService.SignAsync(artifactPath, context)
        );

        Assert.Contains("Windows Authenticode signing is required", exception.Message);
        Assert.False(File.Exists(ArtifactSignatureMetadata.GetSignaturePath(artifactPath)));
    }

    [Fact]
    public async Task PackageVerifierAcceptsRequiredSignatureMetadata()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-verify-signed");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        await CreateSignedInstallerPackageAsync(packageRoot, "Online");
        await CreateSignedInstallerPackageAsync(packageRoot, "Offline");
        CreateUpdatePackage(packageRoot);
        var context = CreateContext(repositoryRoot, outputRoot, new RecordingProcessRunner());

        var failures = new PackageVerifier(context, RequiredSigningOptions()).VerifyAll();

        Assert.Empty(failures);
    }

    [Fact]
    public void PackageVerifierRejectsUnsignedInstallerWhenSigningIsRequired()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var outputRoot = Path.Combine(_rootDirectory, "out-verify-unsigned");
        var packageRoot = Path.Combine(outputRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        CreateUnsignedInstallerPackage(packageRoot, "Online");
        CreateUnsignedInstallerPackage(packageRoot, "Offline");
        CreateUpdatePackage(packageRoot);
        var context = CreateContext(repositoryRoot, outputRoot, new RecordingProcessRunner());

        var failures = new PackageVerifier(context, RequiredSigningOptions()).VerifyAll();

        Assert.Contains(
            failures,
            failure => failure.Contains("Signature metadata missing", StringComparison.Ordinal)
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static SigningOptions RequiredSigningOptions()
    {
        return new SigningOptions(
            SigningRequired: true,
            PfxBase64: null,
            PfxPassword: null,
            TimestampUrl: SigningOptions.DefaultTimestampUrl,
            SigntoolPath: null
        );
    }

    private static BuildContext CreateContext(
        string repositoryRoot,
        string outputRoot,
        IProcessRunner runner
    )
    {
        return new BuildContext(
            new BuildOptions(
                "verify-package",
                repositoryRoot,
                outputRoot,
                "Release",
                "win-x64",
                "9.9.9"
            ),
            new BuildLogger(TextWriter.Null, TextWriter.Null),
            runner,
            new NoOpSigningService()
        );
    }

    private string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "CodexCliPlus.sln"), string.Empty);
        return repositoryRoot;
    }

    private static async Task CreateSignedInstallerPackageAsync(
        string packageRoot,
        string packageMoniker
    )
    {
        var installerName = $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.9.9.9.exe";
        var installerPath = Path.Combine(packageRoot, installerName);
        var packagePath = Path.Combine(
            packageRoot,
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.9.9.9.win-x64.zip"
        );
        File.WriteAllBytes(installerPath, CreateExecutableBytes());
        await ArtifactSignatureMetadata.WriteSignedAsync(
            installerPath,
            "authenticode",
            SigningOptions.DefaultTimestampUrl,
            CancellationToken.None
        );

        var appPath = Path.Combine(
            packageRoot,
            $"{Guid.NewGuid():N}-{AppConstants.ExecutableName}"
        );
        File.WriteAllBytes(appPath, CreateExecutableBytes());
        await ArtifactSignatureMetadata.WriteSignedAsync(
            appPath,
            "authenticode",
            SigningOptions.DefaultTimestampUrl,
            CancellationToken.None
        );

        CreateInstallerStagingZip(
            packagePath,
            installerName,
            CreateExecutableBytes(),
            File.ReadAllBytes(ArtifactSignatureMetadata.GetSignaturePath(appPath)),
            File.ReadAllBytes(ArtifactSignatureMetadata.GetSignaturePath(installerPath))
        );
        await ArtifactSignatureMetadata.WriteAttestationPlaceholderAsync(
            packagePath,
            "Non-PE release artifact; GitHub artifact attestation is expected.",
            CancellationToken.None
        );
    }

    private static void CreateUnsignedInstallerPackage(string packageRoot, string packageMoniker)
    {
        var installerName = $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.9.9.9.exe";
        File.WriteAllBytes(Path.Combine(packageRoot, installerName), CreateExecutableBytes());
        CreateInstallerStagingZip(
            Path.Combine(
                packageRoot,
                $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.9.9.9.win-x64.zip"
            ),
            installerName,
            CreateExecutableBytes()
        );
    }

    private static void CreateUpdatePackage(string packageRoot)
    {
        var packagePath = Path.Combine(packageRoot, "CodexCliPlus.Update.9.9.9.win-x64.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "update-manifest.json", Encoding.UTF8.GetBytes("{}"));
            WriteEntry(archive, "payload/CodexCliPlus.exe", CreateExecutableBytes());
        }

        ArtifactSignatureMetadata
            .WriteAttestationPlaceholderAsync(
                packagePath,
                "Non-PE release artifact; GitHub artifact attestation is expected.",
                CancellationToken.None
            )
            .GetAwaiter()
            .GetResult();
    }

    private static void CreateInstallerStagingZip(
        string packagePath,
        string installerName,
        byte[] installerBytes,
        byte[]? appSignatureBytes = null,
        byte[]? installerSignatureBytes = null
    )
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        WriteEntry(archive, "app-package/CodexCliPlus.exe", CreateExecutableBytes());
        WriteEntry(
            archive,
            "app-package/assets/webui/upstream/dist/index.html",
            Encoding.UTF8.GetBytes("<html></html>")
        );
        WriteEntry(
            archive,
            "app-package/assets/webui/upstream/dist/assets/app.js",
            Encoding.UTF8.GetBytes("console.log('ok');")
        );
        WriteEntry(
            archive,
            "app-package/assets/webui/upstream/sync.json",
            Encoding.UTF8.GetBytes("{}")
        );
        WriteEntry(archive, "mica-setup.json", Encoding.UTF8.GetBytes("{}"));
        WriteEntry(archive, "micasetup.json", Encoding.UTF8.GetBytes("{}"));
        WriteEntry(archive, $"output/{installerName}", installerBytes);
        WriteEntry(
            archive,
            $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.BootstrapperFileName}",
            CreateExecutableBytes()
        );
        if (installerName.Contains(".Offline.", StringComparison.OrdinalIgnoreCase))
        {
            WriteEntry(
                archive,
                $"app-package/{WebView2RuntimeAssets.PackagedDirectory}/{WebView2RuntimeAssets.StandaloneX64FileName}",
                CreateExecutableBytes()
            );
        }
        WriteEntry(
            archive,
            "app-package/packaging/uninstall-cleanup.json",
            Encoding.UTF8.GetBytes("{}")
        );
        WriteEntry(
            archive,
            "app-package/packaging/dependency-precheck.json",
            Encoding.UTF8.GetBytes("{}")
        );
        WriteEntry(
            archive,
            "app-package/packaging/update-policy.json",
            Encoding.UTF8.GetBytes("{}")
        );

        if (appSignatureBytes is not null)
        {
            WriteEntry(archive, "app-package/CodexCliPlus.exe.signature.json", appSignatureBytes);
        }

        if (installerSignatureBytes is not null)
        {
            WriteEntry(archive, $"output/{installerName}.signature.json", installerSignatureBytes);
        }
    }

    private static void WriteEntry(ZipArchive archive, string entryName, byte[] bytes)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static ArtifactSignatureMetadata ReadMetadata(string path)
    {
        return JsonSerializer.Deserialize<ArtifactSignatureMetadata>(
                File.ReadAllText(path, Encoding.UTF8),
                JsonDefaults.Options
            ) ?? throw new Xunit.Sdk.XunitException($"Could not read signature metadata: {path}");
    }

    private static byte[] CreateExecutableBytes()
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private sealed record ProcessCall(
        string FileName,
        List<string> Arguments,
        string WorkingDirectory
    );

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessCall> Calls { get; } = [];

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            BuildLogger logger,
            IReadOnlyDictionary<string, string?>? environment = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add(new ProcessCall(fileName, arguments.ToList(), workingDirectory));
            return Task.FromResult(0);
        }
    }
}
