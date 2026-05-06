using CodexCliPlus.Infrastructure.Codex;

namespace CodexCliPlus.Tests.Codex;

[Trait("Category", "Fast")]
public sealed class CodexLocatorTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"codexcliplus-codex-locator-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task LocateAsyncReturnsFirstWhereResult()
    {
        var locator = new CodexLocator(
            _ => Task.FromResult((0, "C:\\tools\\codex.cmd\r\nC:\\other\\codex.cmd", string.Empty)),
            () => Path.Combine(_rootDirectory, "missing.cmd")
        );

        var path = await locator.LocateAsync();

        Assert.Equal("C:\\tools\\codex.cmd", path);
    }

    [Fact]
    public async Task LocateAsyncReturnsFallbackWhenWhereFailsAndNpmCommandExists()
    {
        Directory.CreateDirectory(_rootDirectory);
        var fallbackPath = Path.Combine(_rootDirectory, "codex.cmd");
        await File.WriteAllTextAsync(fallbackPath, "@echo off");

        var locator = new CodexLocator(
            _ => Task.FromResult((1, string.Empty, "INFO: Could not find files")),
            () => fallbackPath
        );

        var path = await locator.LocateAsync();

        Assert.Equal(fallbackPath, path);
    }

    [Fact]
    public async Task LocateAsyncReturnsNullWhenWhereFailsAndFallbackDoesNotExist()
    {
        var locator = new CodexLocator(
            _ => Task.FromResult((1, string.Empty, "INFO: Could not find files")),
            () => Path.Combine(_rootDirectory, "missing.cmd")
        );

        var path = await locator.LocateAsync();

        Assert.Null(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
