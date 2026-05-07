namespace CodexCliPlus.Core.Models;

public sealed record CodexUserFileValidation
{
    public bool IsValid { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed record CodexUserFileSnapshot
{
    public string FileId { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public bool Exists { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public long SizeBytes { get; init; }

    public CodexUserFileValidation Validation { get; init; } = new();
}

public sealed record CodexUserFileBackupResult
{
    public string FileId { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string BackupPath { get; init; } = string.Empty;

    public CodexUserFileSnapshot Snapshot { get; init; } = new();
}

public sealed record CodexUserFileSaveResult
{
    public string FileId { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string? BackupPath { get; init; }

    public CodexUserFileSnapshot Snapshot { get; init; } = new();
}
