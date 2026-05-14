using CodexCliPlus.Infrastructure.Utilities;

namespace CodexCliPlus.Tests.Utilities;

[Trait("Category", "LocalIntegration")]
public sealed class AtomicFileWriterTests
{
    [Fact]
    public async Task WriteUtf8NoBomTextAsyncAllowsFileNameOnlyPath()
    {
        var path = $"codexcliplus-atomic-text-{Guid.NewGuid():N}.txt";

        try
        {
            await AtomicFileWriter.WriteUtf8NoBomTextAsync(path, "hello", CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("hello", await File.ReadAllTextAsync(path));
            Assert.False(StartsWithUtf8Bom(bytes));
            Assert.Empty(Directory.EnumerateFiles(".", $"{path}.*.tmp"));
        }
        finally
        {
            DeleteIfExists(path);
            DeleteTemporaryFiles(path);
        }
    }

    [Fact]
    public async Task WriteBytesAsyncAllowsFileNameOnlyPath()
    {
        var path = $"codexcliplus-atomic-bytes-{Guid.NewGuid():N}.bin";

        try
        {
            await AtomicFileWriter.WriteBytesAsync(
                path,
                new byte[] { 1, 2, 3 },
                CancellationToken.None
            );

            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.EnumerateFiles(".", $"{path}.*.tmp"));
        }
        finally
        {
            DeleteIfExists(path);
            DeleteTemporaryFiles(path);
        }
    }

    private static bool StartsWithUtf8Bom(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteTemporaryFiles(string path)
    {
        foreach (var temporaryPath in Directory.EnumerateFiles(".", $"{path}.*.tmp"))
        {
            File.Delete(temporaryPath);
        }
    }
}
