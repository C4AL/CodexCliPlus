namespace CodexCliPlus.Core.Exceptions;

public sealed class ManagementApiException : Exception
{
    public ManagementApiException(
        string message,
        int? statusCode = null,
        string? errorCode = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    public int? StatusCode { get; }

    public string? ErrorCode { get; }

    public string? ResponseBody { get; }
}
