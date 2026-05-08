using System.Text;

namespace CodexCliPlus.Infrastructure.Utilities;

internal static class AtomicFileWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    public static async Task WriteUtf8NoBomTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken
    )
    {
        await WriteAsync(
            path,
            async (stream, token) =>
            {
                using (
                    var writer = new StreamWriter(
                        stream,
                        Utf8NoBom,
                        bufferSize: 1024,
                        leaveOpen: true
                    )
                )
                {
                    await writer.WriteAsync(content.AsMemory(), token);
                    await writer.FlushAsync(token);
                }
            },
            cancellationToken
        );
    }

    public static async Task WriteBytesAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken
    )
    {
        await WriteAsync(
            path,
            async (stream, token) => await stream.WriteAsync(content, token),
            cancellationToken
        );
    }

    private static async Task WriteAsync(
        string path,
        Func<FileStream, CancellationToken, Task> writeContentAsync,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var stream = new FileStream(
                    tempPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                    }
                )
            )
            {
                await writeContentAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
