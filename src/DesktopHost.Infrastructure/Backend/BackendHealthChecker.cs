using System.Net;

namespace DesktopHost.Infrastructure.Backend;

public sealed class BackendHealthChecker
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BackendHealthChecker(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> WaitUntilHealthyAsync(
        string healthUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore transient startup failures and continue polling.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }

        return false;
    }
}
