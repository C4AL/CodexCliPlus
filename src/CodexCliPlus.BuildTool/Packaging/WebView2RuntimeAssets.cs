using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace CodexCliPlus.BuildTool;

public sealed class WebView2RuntimeAssets
{
    public const string PackagedDirectory = "packaging/dependencies/webview2";
    public const string BootstrapperFileName = "MicrosoftEdgeWebview2Setup.exe";
    public const string StandaloneX64FileName = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe";

    public const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    public const string StandaloneX64Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124701";

    private static readonly WebView2RuntimeAssetDescriptor[] Descriptors =
    [
        new(BootstrapperFileName, BootstrapperUrl, "/silent /install"),
        new(StandaloneX64FileName, StandaloneX64Url, "/silent /install"),
    ];

    public IReadOnlyList<WebView2RuntimeAsset> Assets { get; private init; } = [];

    public WebView2RuntimeAsset Bootstrapper =>
        Assets.Single(asset => asset.FileName == BootstrapperFileName);

    public WebView2RuntimeAsset StandaloneX64 =>
        Assets.Single(asset => asset.FileName == StandaloneX64FileName);

    public WebView2RuntimeAsset? OptionalStandaloneX64 =>
        Assets.FirstOrDefault(asset => asset.FileName == StandaloneX64FileName);

    public static async Task<WebView2RuntimeAssets> StageAsync(
        BuildContext context,
        string appPackageRoot,
        InstallerPackageKind packageKind,
        CancellationToken cancellationToken = default
    )
    {
        if (packageKind == InstallerPackageKind.Online)
        {
            return new WebView2RuntimeAssets
            {
                Assets =
                [
                    new WebView2RuntimeAsset(
                        BootstrapperFileName,
                        BootstrapperUrl,
                        ToPackagePath(PackagedDirectory, BootstrapperFileName),
                        "/silent /install",
                        0,
                        string.Empty,
                        Bundled: false
                    ),
                ],
            };
        }

        var cachedRoot = Path.Combine(context.CacheRoot, "webview2");
        var stagedRoot = Path.Combine(
            appPackageRoot,
            PackagedDirectory.Replace('/', Path.DirectorySeparatorChar)
        );
        Directory.CreateDirectory(cachedRoot);
        Directory.CreateDirectory(stagedRoot);

        var assets = new List<WebView2RuntimeAsset>();
        var descriptors =
            packageKind == InstallerPackageKind.Online
                ? Descriptors.Where(descriptor => descriptor.FileName == BootstrapperFileName)
                : Descriptors;
        foreach (var descriptor in descriptors)
        {
            var cachedPath = Path.Combine(cachedRoot, descriptor.FileName);
            if (WindowsExecutableValidation.ValidateFile(cachedPath) is not null)
            {
                await DownloadWithRetryAsync(
                    descriptor,
                    cachedPath,
                    context.Logger,
                    cancellationToken
                );
            }

            var validationFailure = WindowsExecutableValidation.ValidateFile(cachedPath);
            if (validationFailure is not null)
            {
                throw new InvalidDataException(
                    $"WebView2 runtime installer cache is invalid: {validationFailure}"
                );
            }

            var stagedPath = Path.Combine(stagedRoot, descriptor.FileName);
            File.Copy(cachedPath, stagedPath, overwrite: true);
            assets.Add(
                new WebView2RuntimeAsset(
                    descriptor.FileName,
                    descriptor.SourceUrl,
                    ToPackagePath(PackagedDirectory, descriptor.FileName),
                    descriptor.SilentArguments,
                    new FileInfo(stagedPath).Length,
                    await ComputeSha256Async(stagedPath, cancellationToken),
                    Bundled: true
                )
            );
        }

        return new WebView2RuntimeAssets { Assets = assets };
    }

    private static async Task DownloadWithRetryAsync(
        WebView2RuntimeAssetDescriptor descriptor,
        string targetPath,
        BuildLogger logger,
        CancellationToken cancellationToken
    )
    {
        var tempPath = targetPath + ".download";
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                logger.Info($"WebView2 runtime download {descriptor.FileName} attempt {attempt}/3");
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                using var request = new HttpRequestMessage(HttpMethod.Get, descriptor.SourceUrl);
                request.Headers.UserAgent.Add(
                    new ProductInfoHeaderValue("CodexCliPlus-BuildTool", "1.0")
                );

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
                var body = response.IsSuccessStatusCode
                    ? null
                    : await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}"
                    );
                }

                await using (
                    var source = await response.Content.ReadAsStreamAsync(cancellationToken)
                )
                await using (var target = File.Create(tempPath))
                {
                    await source.CopyToAsync(target, cancellationToken);
                }

                var validationFailure = WindowsExecutableValidation.ValidateFile(tempPath);
                if (validationFailure is not null)
                {
                    throw new InvalidDataException(validationFailure);
                }

                File.Move(tempPath, targetPath, overwrite: true);
                logger.Info($"WebView2 runtime cached: {descriptor.FileName}");
                return;
            }
            catch (Exception exception) when (attempt < 3)
            {
                lastError = exception;
                logger.Error(
                    $"WebView2 runtime download {descriptor.FileName} attempt {attempt}/3 failed: {exception.Message}"
                );
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        throw new InvalidOperationException(
            $"Failed to download WebView2 runtime installer {descriptor.FileName}: {lastError?.Message}",
            lastError
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

    private static string ToPackagePath(string directory, string fileName)
    {
        return $"{directory.TrimEnd('/')}/{fileName}";
    }

    private sealed record WebView2RuntimeAssetDescriptor(
        string FileName,
        string SourceUrl,
        string SilentArguments
    );
}

public sealed record WebView2RuntimeAsset(
    string FileName,
    string SourceUrl,
    string PackagedPath,
    string SilentArguments,
    long Size,
    string Sha256,
    bool Bundled
);
