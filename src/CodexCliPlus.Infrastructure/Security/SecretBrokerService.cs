using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Models.Security;

namespace CodexCliPlus.Infrastructure.Security;

public sealed class SecretBrokerService : IDisposable
{
    public const string BrokerUrlEnvironmentVariable = "CCP_SECRET_BROKER_URL";
    public const string BrokerTokenEnvironmentVariable = "CCP_SECRET_BROKER_TOKEN";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecretVault _secretVault;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HttpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _disposed;

    public SecretBrokerService(ISecretVault secretVault, IAppLogger logger)
    {
        _secretVault = secretVault;
        _logger = logger;
    }

    public SecretBrokerSession? CurrentSession { get; private set; }

    public async Task<SecretBrokerSession> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (CurrentSession is not null)
            {
                return CurrentSession;
            }

            var port = ReserveLoopbackPort();
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var baseUrl = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}");
            var listener = new HttpListener();
            listener.Prefixes.Add($"{baseUrl}/");
            listener.Start();

            _listener = listener;
            _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listenerTask = Task.Run(
                () => RunListenerAsync(listener, token, _listenerCts.Token),
                CancellationToken.None
            );
            CurrentSession = new SecretBrokerSession(baseUrl, token);
            _logger.Info("Secret Broker started on loopback.");
            return CurrentSession;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var listener = _listener;
            var cts = _listenerCts;
            var task = _listenerTask;

            CurrentSession = null;
            _listener = null;
            _listenerCts = null;
            _listenerTask = null;

            if (listener is null)
            {
                return;
            }

            try
            {
                cts?.Cancel();
                listener.Stop();
                listener.Close();
            }
            catch (ObjectDisposedException) { }

            if (task is not null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
            }

            cts?.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Task.Run(() => StopAsync()).GetAwaiter().GetResult();
        _gate.Dispose();
    }

    private async Task RunListenerAsync(
        HttpListener listener,
        string token,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(
                () => HandleRequestAsync(context, token, cancellationToken),
                cancellationToken
            );
        }
    }

    private async Task HandleRequestAsync(
        HttpListenerContext context,
        string token,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!IsLoopbackRequest(context.Request))
            {
                await WriteStatusAsync(
                    context.Response,
                    HttpStatusCode.Forbidden,
                    cancellationToken
                );
                return;
            }

            var authorization = context.Request.Headers["Authorization"] ?? string.Empty;
            if (!string.Equals(authorization, $"Bearer {token}", StringComparison.Ordinal))
            {
                await WriteStatusAsync(
                    context.Response,
                    HttpStatusCode.Unauthorized,
                    cancellationToken
                );
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (
                string.Equals(path.TrimEnd('/'), "/v1/secrets", StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    context.Request.HttpMethod,
                    "POST",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                await HandleSaveSecretAsync(context, cancellationToken);
                return;
            }

            const string prefix = "/v1/secrets/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await WriteStatusAsync(
                    context.Response,
                    HttpStatusCode.NotFound,
                    cancellationToken
                );
                return;
            }

            if (
                !string.Equals(
                    context.Request.HttpMethod,
                    "GET",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                await WriteStatusAsync(
                    context.Response,
                    HttpStatusCode.MethodNotAllowed,
                    cancellationToken
                );
                return;
            }

            var secretId = Uri.UnescapeDataString(path[prefix.Length..]);
            var value = await _secretVault.RevealSecretAsync(secretId, cancellationToken);
            if (value is null)
            {
                await WriteStatusAsync(
                    context.Response,
                    HttpStatusCode.NotFound,
                    cancellationToken
                );
                return;
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(new { value }, JsonOptions);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Secret Broker request failed: {exception.Message}");
            if (context.Response.OutputStream.CanWrite)
            {
                await WriteStatusAsync(
                    context.Response,
                    HttpStatusCode.InternalServerError,
                    CancellationToken.None
                );
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task HandleSaveSecretAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken
    )
    {
        SecretBrokerSaveRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<SecretBrokerSaveRequest>(
                context.Request.InputStream,
                JsonOptions,
                cancellationToken
            );
        }
        catch (JsonException)
        {
            await WriteStatusAsync(context.Response, HttpStatusCode.BadRequest, cancellationToken);
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Value))
        {
            await WriteStatusAsync(context.Response, HttpStatusCode.BadRequest, cancellationToken);
            return;
        }

        var kind = SecretKind.Unknown;
        if (!string.IsNullOrWhiteSpace(request.Kind))
        {
            Enum.TryParse(request.Kind, ignoreCase: true, out kind);
        }

        var record = await _secretVault.SaveSecretAsync(
            kind,
            request.Value,
            string.IsNullOrWhiteSpace(request.Source) ? "secret-broker" : request.Source,
            request.Metadata,
            string.IsNullOrWhiteSpace(request.SecretId) ? null : request.SecretId,
            cancellationToken
        );
        var uri = new SecretRef(record.SecretId).Uri;
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new { uri, secretId = record.SecretId },
            JsonOptions
        );
        context.Response.StatusCode = (int)HttpStatusCode.Created;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = payload.Length;
        await context.Response.OutputStream.WriteAsync(payload, cancellationToken);
    }

    private static async Task WriteStatusAsync(
        HttpListenerResponse response,
        HttpStatusCode status,
        CancellationToken cancellationToken
    )
    {
        var payload = Encoding.UTF8.GetBytes(status.ToString());
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload, cancellationToken);
    }

    private static bool IsLoopbackRequest(HttpListenerRequest request)
    {
        var remoteAddress = request.RemoteEndPoint?.Address;
        return remoteAddress is null || IPAddress.IsLoopback(remoteAddress);
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed record SecretBrokerSession(string BaseUrl, string Token);

internal sealed class SecretBrokerSaveRequest
{
    public string Value { get; init; } = string.Empty;

    public string? Kind { get; init; }

    public string? Source { get; init; }

    public string? SecretId { get; init; }

    public Dictionary<string, string>? Metadata { get; init; }
}
