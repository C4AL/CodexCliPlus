using CPAD.Core.Models.Management;

namespace CPAD.Infrastructure.Management;

internal static class ManagementResponseFactory
{
    public static ManagementApiResponse<TOut> Map<TIn, TOut>(
        ManagementApiResponse<TIn> response,
        TOut value)
    {
        return new ManagementApiResponse<TOut>
        {
            Value = value,
            Metadata = response.Metadata,
            StatusCode = response.StatusCode
        };
    }
}
