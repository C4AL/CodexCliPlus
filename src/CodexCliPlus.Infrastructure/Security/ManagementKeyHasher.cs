namespace CodexCliPlus.Infrastructure.Security;

internal static class ManagementKeyHasher
{
    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return BCrypt.Net.BCrypt.HashPassword(secret);
    }
}
