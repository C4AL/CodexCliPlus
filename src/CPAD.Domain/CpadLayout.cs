namespace CPAD.Domain;

public sealed class CpadLayout
{
    public required string InstallRoot { get; init; }

    public required IReadOnlyDictionary<string, string> Directories { get; init; }

    public required IReadOnlyDictionary<string, string> Files { get; init; }
}
