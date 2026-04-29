using System.Net;

namespace CodexCliPlus.Core.Models.Management;

public sealed class ManagementConnectionInfo
{
    public required string BaseUrl { get; init; }

    public required string ManagementApiBaseUrl { get; init; }

    public required string ManagementKey { get; init; }
}

public sealed class ManagementServerMetadata
{
    public string? Version { get; init; }

    public string? Commit { get; init; }

    public string? BuildDate { get; init; }
}

public sealed class ManagementApiResponse<T>
{
    public required T Value { get; init; }

    public required ManagementServerMetadata Metadata { get; init; }

    public HttpStatusCode StatusCode { get; init; }
}

public sealed class ManagementOperationResult
{
    public string? Status { get; init; }

    public string? Message { get; init; }

    public bool? Success { get; init; }

    public bool? Ok { get; init; }

    public int? Added { get; init; }

    public int? Skipped { get; init; }

    public int? Uploaded { get; init; }

    public int? Deleted { get; init; }

    public int? Removed { get; init; }

    public IReadOnlyList<string> Changed { get; init; } = [];

    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<ManagementBatchFailure> Failed { get; init; } = [];
}

public sealed class ManagementBatchFailure
{
    public string Name { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;
}

public sealed class ManagementMultipartFile
{
    public required string FieldName { get; init; }

    public required string FileName { get; init; }

    public required byte[] Content { get; init; }

    public string ContentType { get; init; } = "application/octet-stream";
}

public sealed class ManagementAuthFileUpload
{
    public required string FileName { get; init; }

    public required byte[] Content { get; init; }

    public string ContentType { get; init; } = "application/json";
}

public sealed class ManagementAuthFileFieldPatch
{
    public required string Name { get; init; }

    public string? Prefix { get; init; }

    public string? ProxyUrl { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public enum ManagementProviderKind
{
    Gemini,
    Codex,
    Claude,
    Vertex,
    OpenAiCompatibility,
}

public sealed class ManagementLatestVersionInfo
{
    public string LatestVersion { get; init; } = string.Empty;
}

public sealed class ManagementOAuthStartResponse
{
    public string Url { get; init; } = string.Empty;

    public string? State { get; init; }
}

public sealed class ManagementOAuthStatus
{
    public string Status { get; init; } = string.Empty;

    public string? Error { get; init; }
}

public sealed class ManagementApiCallRequest
{
    public string Method { get; init; } = "GET";

    public string Url { get; init; } = string.Empty;

    public string? AuthIndex { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? Data { get; init; }
}

public sealed class ManagementApiCallResult
{
    public int StatusCode { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public string BodyText { get; init; } = string.Empty;
}
