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

public sealed class MicaSetupToolchain
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/lemutec/MicaSetup/releases/latest";

    private MicaSetupToolchain(
        string rootDirectory,
        string sevenZipPath,
        string makeMicaPath,
        string templatePath,
        string version
    )
    {
        RootDirectory = rootDirectory;
        SevenZipPath = sevenZipPath;
        MakeMicaPath = makeMicaPath;
        TemplatePath = templatePath;
        Version = version;
    }

    public string RootDirectory { get; }

    public string SevenZipPath { get; }

    public string MakeMicaPath { get; }

    public string TemplatePath { get; }

    public string Version { get; }

    public static async Task<MicaSetupToolchain> AcquireAsync(BuildContext context)
    {
        var repoOwnedRoot = Path.Combine(
            context.Options.RepositoryRoot,
            "build",
            "micasetup",
            "toolchain"
        );
        var repoOwnedToolchain = await TryCreateFromDirectoryAsync(
            repoOwnedRoot,
            "repo-owned",
            context.Logger
        );
        if (repoOwnedToolchain is not null)
        {
            context.Logger.Info($"MicaSetup tools repo-owned: {repoOwnedToolchain.Version}");
            return repoOwnedToolchain;
        }

        var root = Path.Combine(context.ToolsRoot, "micasetup");
        var cachedToolchain = await TryCreateFromDirectoryAsync(root, "cached", context.Logger);
        if (cachedToolchain is not null)
        {
            context.Logger.Info($"MicaSetup tools cached: {cachedToolchain.Version}");
            return cachedToolchain;
        }

        SafeFileSystem.CleanDirectory(root, context.Options.OutputRoot);
        var sevenZip = Path.Combine(root, "build", "bin", "7z.exe");
        var makeMica = Path.Combine(root, "build", "makemica.exe");
        var template = Path.Combine(root, "build", "template", "default.7z");
        var versionPath = Path.Combine(root, "micasetup-tools-version.txt");

        var release = await QueryLatestReleaseAsync();
        var asset =
            release.Assets.FirstOrDefault(item =>
                item.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                && item.Name.StartsWith("MicaSetup.Tools.", StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidDataException(
                "Latest MicaSetup release does not contain a MicaSetup.Tools nupkg asset."
            );

        var downloadPath = Path.Combine(root, "download", asset.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
        await DownloadWithRetryAsync(asset.DownloadUrl, downloadPath, context.Logger);
        ZipFile.ExtractToDirectory(downloadPath, root, overwriteFiles: true);

        if (!File.Exists(sevenZip) || !File.Exists(makeMica) || !File.Exists(template))
        {
            throw new FileNotFoundException(
                "Downloaded MicaSetup.Tools package is missing makemica.exe, 7z.exe, or template/default.7z."
            );
        }

        await File.WriteAllTextAsync(
            versionPath,
            release.TagName,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
        context.Logger.Info($"MicaSetup tools downloaded: {release.TagName}");
        var compatibleMakeMica = MakeMicaVisualStudioCompatibility.TryCreateCompatibleMakeMica(
            makeMica,
            context.Logger
        );
        return new MicaSetupToolchain(
            root,
            sevenZip,
            compatibleMakeMica,
            template,
            release.TagName
        );
    }

    private static async Task<MicaSetupToolchain?> TryCreateFromDirectoryAsync(
        string root,
        string fallbackVersion,
        BuildLogger logger
    )
    {
        var sevenZip = Path.Combine(root, "build", "bin", "7z.exe");
        var makeMica = Path.Combine(root, "build", "makemica.exe");
        var template = Path.Combine(root, "build", "template", "default.7z");
        if (!File.Exists(sevenZip) || !File.Exists(makeMica) || !File.Exists(template))
        {
            return null;
        }

        var versionPath = Path.Combine(root, "micasetup-tools-version.txt");
        var version = File.Exists(versionPath)
            ? (await File.ReadAllTextAsync(versionPath)).Trim()
            : fallbackVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = fallbackVersion;
        }

        var compatibleMakeMica = MakeMicaVisualStudioCompatibility.TryCreateCompatibleMakeMica(
            makeMica,
            logger
        );
        return new MicaSetupToolchain(root, sevenZip, compatibleMakeMica, template, version);
    }

    private static async Task<MicaSetupRelease> QueryLatestReleaseAsync()
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CodexCliPlus-BuildTool", "1.0"));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"MicaSetup latest release query failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}"
            );
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var assets = new List<MicaSetupReleaseAsset>();
        if (
            root.TryGetProperty("assets", out var assetsElement)
            && assetsElement.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = GetString(asset, "name");
                var url = GetString(asset, "browser_download_url");
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                {
                    assets.Add(new MicaSetupReleaseAsset(name, url));
                }
            }
        }

        return new MicaSetupRelease(
            GetString(root, "tag_name") ?? GetString(root, "name") ?? "latest",
            assets
        );
    }

    private static async Task DownloadWithRetryAsync(
        string url,
        string targetPath,
        BuildLogger logger
    )
    {
        using var client = new HttpClient();
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                logger.Info($"MicaSetup tools download attempt {attempt}/3");
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(targetPath, bytes);
                return;
            }
            catch (Exception exception)
            {
                lastError = exception;
                logger.Error(
                    $"MicaSetup tools download attempt {attempt}/3 failed: {exception.Message}"
                );
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                }
            }
        }

        throw new InvalidOperationException(
            $"Failed to download MicaSetup tools: {lastError?.Message}",
            lastError
        );
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record MicaSetupRelease(
        string TagName,
        IReadOnlyList<MicaSetupReleaseAsset> Assets
    );

    private sealed record MicaSetupReleaseAsset(string Name, string DownloadUrl);
}
