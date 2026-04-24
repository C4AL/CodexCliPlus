using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using CPAD.Core.Abstractions.Management;
using CPAD.Core.Exceptions;
using CPAD.Core.Models.Management;

namespace CPAD.Infrastructure.Management;

public sealed class ManagementApiClient : IManagementApiClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly IManagementConnectionProvider _connectionProvider;
    private readonly IHttpClientFactory _httpClientFactory;

    public ManagementApiClient(
        IManagementConnectionProvider connectionProvider,
        IHttpClientFactory httpClientFactory)
    {
        _connectionProvider = connectionProvider;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ManagementApiResponse<string>> SendManagementAsync(
        HttpMethod method,
        string path,
        string? body = null,
        string contentType = "application/json",
        string? accept = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        return await SendAsync(
            connection.ManagementApiBaseUrl,
            method,
            path,
            bearerToken: connection.ManagementKey,
            headers: null,
            body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            accept,
            timeout,
            cancellationToken);
    }

    public async Task<ManagementApiResponse<string>> SendManagementMultipartAsync(
        HttpMethod method,
        string path,
        IReadOnlyList<ManagementMultipartFile> files,
        IReadOnlyDictionary<string, string>? fields = null,
        string? accept = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        return await SendAsync(
            connection.ManagementApiBaseUrl,
            method,
            path,
            bearerToken: connection.ManagementKey,
            headers: null,
            CreateMultipartContent(files, fields),
            accept,
            timeout,
            cancellationToken);
    }

    public async Task<ManagementApiResponse<string>> GetBackendAsync(
        string path,
        IReadOnlyDictionary<string, string>? headers = null,
        string? accept = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        return await SendAsync(
            connection.BaseUrl,
            HttpMethod.Get,
            path,
            bearerToken: null,
            headers,
            content: null,
            accept,
            timeout,
            cancellationToken);
    }

    private async Task<ManagementApiResponse<string>> SendAsync(
        string baseUrl,
        HttpMethod method,
        string path,
        string? bearerToken,
        IReadOnlyDictionary<string, string>? headers,
        HttpContent? content,
        string? accept,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var request = CreateRequest(baseUrl, method, path, bearerToken, headers, content, accept);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout ?? DefaultTimeout);
            var client = _httpClientFactory.CreateClient();

            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
                var payload = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (IsTransientStatusCode(response.StatusCode) && attempt < 2)
                {
                    lastError = CreateException(response.StatusCode, payload);
                    await Task.Delay(RetryDelay, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw CreateException(response.StatusCode, payload);
                }

                return new ManagementApiResponse<string>
                {
                    Value = payload,
                    Metadata = ReadMetadata(response),
                    StatusCode = response.StatusCode
                };
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
                lastError = exception;
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt < 2)
            {
                lastError = exception;
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ManagementApiException(
                    "The management API request timed out.",
                    responseBody: content is StringContent ? await content.ReadAsStringAsync(cancellationToken) : null,
                    innerException: exception);
            }
            catch (HttpRequestException exception)
            {
                throw new ManagementApiException(
                    "The management API request failed.",
                    responseBody: content is StringContent ? await content.ReadAsStringAsync(cancellationToken) : null,
                    innerException: exception);
            }
        }

        throw new ManagementApiException(
            lastError?.Message ?? "The management API request failed after retry.",
            innerException: lastError);
    }

    private static HttpRequestMessage CreateRequest(
        string baseUrl,
        HttpMethod method,
        string path,
        string? bearerToken,
        IReadOnlyDictionary<string, string>? headers,
        HttpContent? content,
        string? accept)
    {
        var request = new HttpRequestMessage(method, BuildUri(baseUrl, path));

        if (!string.IsNullOrWhiteSpace(accept))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        if (!string.IsNullOrWhiteSpace(bearerToken) && !HasHeader(headers, "Authorization"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (content is not null)
        {
            request.Content = CloneContent(content);
        }

        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                if (!request.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
                {
                    request.Content ??= new StringContent(string.Empty, Encoding.UTF8, "application/json");
                    request.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                }
            }
        }

        return request;
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        var normalizedBase = baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/";
        return new Uri(new Uri(normalizedBase, UriKind.Absolute), path.TrimStart('/'));
    }

    private static bool HasHeader(IReadOnlyDictionary<string, string>? headers, string name)
    {
        return headers?.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    }

    private static ManagementServerMetadata ReadMetadata(HttpResponseMessage response)
    {
        return new ManagementServerMetadata
        {
            Version = ReadHeaderValue(response, "X-CPA-VERSION"),
            Commit = ReadHeaderValue(response, "X-CPA-COMMIT"),
            BuildDate = ReadHeaderValue(response, "X-CPA-BUILD-DATE")
        };
    }

    private static string? ReadHeaderValue(HttpResponseMessage response, string key)
    {
        return response.Headers.TryGetValues(key, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static MultipartFormDataContent CreateMultipartContent(
        IReadOnlyList<ManagementMultipartFile> files,
        IReadOnlyDictionary<string, string>? fields)
    {
        var content = new MultipartFormDataContent();

        if (fields is not null)
        {
            foreach (var field in fields)
            {
                content.Add(new StringContent(field.Value, Encoding.UTF8), field.Key);
            }
        }

        foreach (var file in files)
        {
            var fileContent = new ByteArrayContent(file.Content);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
            content.Add(fileContent, file.FieldName, file.FileName);
        }

        return content;
    }

    private static HttpContent CloneContent(HttpContent content)
    {
        return content switch
        {
            StringContent stringContent => CloneStringContent(stringContent),
            MultipartFormDataContent multipartContent => CloneMultipartContent(multipartContent),
            ByteArrayContent byteArrayContent => CloneByteArrayContent(byteArrayContent),
            _ => throw new NotSupportedException($"Unsupported content type: {content.GetType().Name}")
        };
    }

    private static StringContent CloneStringContent(StringContent content)
    {
        var payload = content.ReadAsStringAsync().GetAwaiter().GetResult();
        var mediaType = content.Headers.ContentType?.MediaType ?? "application/json";
        var encodingName = content.Headers.ContentType?.CharSet;
        var encoding = string.IsNullOrWhiteSpace(encodingName) ? Encoding.UTF8 : Encoding.GetEncoding(encodingName);
        var clone = new StringContent(payload, encoding, mediaType);
        CopyContentHeaders(content, clone, skipContentType: true);
        return clone;
    }

    private static MultipartFormDataContent CloneMultipartContent(MultipartFormDataContent content)
    {
        var boundary = content.Headers.ContentType?.Parameters
            .FirstOrDefault(parameter => string.Equals(parameter.Name, "boundary", StringComparison.OrdinalIgnoreCase))
            ?.Value?.Trim('"');
        var clone = string.IsNullOrWhiteSpace(boundary)
            ? new MultipartFormDataContent()
            : new MultipartFormDataContent(boundary);

        foreach (var part in content)
        {
            var partClone = CloneContent(part);
            var disposition = part.Headers.ContentDisposition;
            if (disposition is not null)
            {
                partClone.Headers.ContentDisposition = new ContentDispositionHeaderValue(disposition.DispositionType)
                {
                    Name = disposition.Name,
                    FileName = disposition.FileName,
                    FileNameStar = disposition.FileNameStar
                };
            }

            clone.Add(partClone);
        }

        CopyContentHeaders(content, clone, skipContentType: true);
        return clone;
    }

    private static ByteArrayContent CloneByteArrayContent(ByteArrayContent content)
    {
        var payload = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        var clone = new ByteArrayContent(payload);
        CopyContentHeaders(content, clone);
        return clone;
    }

    private static void CopyContentHeaders(HttpContent source, HttpContent destination, bool skipContentType = false)
    {
        foreach (var header in source.Headers)
        {
            if (skipContentType &&
                string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            destination.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static ManagementApiException CreateException(HttpStatusCode statusCode, string payload)
    {
        var status = (int)statusCode;
        var message = $"Management API request failed with HTTP {status}.";
        string? errorCode = null;

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                errorCode = ManagementJson.GetString(root, "error");
                var errorMessage = ManagementJson.GetString(root, "message");
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    message = errorMessage;
                }
                else if (!string.IsNullOrWhiteSpace(errorCode))
                {
                    message = errorCode;
                }
            }
            catch (JsonException)
            {
                message = payload.Trim();
            }
        }

        return new ManagementApiException(message, status, errorCode, payload);
    }
}
