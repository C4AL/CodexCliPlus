using System.IO.Compression;
using System.Net.Http.Headers;

namespace CodexCliPlus.OfflineBuilder;

internal interface IToolArchiveDownloader
{
    Task DownloadAsync(Uri sourceUri, string targetPath, CancellationToken cancellationToken);
}

internal interface IToolArchiveExtractor
{
    void ExtractToDirectory(string archivePath, string destinationDirectory);
}

internal sealed class HttpToolArchiveDownloader : IToolArchiveDownloader
{
    public async Task DownloadAsync(
        Uri sourceUri,
        string targetPath,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("CodexCliPlus-OfflineBuilder", "1.0")
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
            throw new OfflineBuilderException(
                $"下载工具失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。{body}"
            );
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);
    }
}

internal sealed class ZipToolArchiveExtractor : IToolArchiveExtractor
{
    public void ExtractToDirectory(string archivePath, string destinationDirectory)
    {
        ZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);
    }
}
