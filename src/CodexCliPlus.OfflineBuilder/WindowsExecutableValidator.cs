namespace CodexCliPlus.OfflineBuilder;

internal static class WindowsExecutableValidator
{
    public static string? ValidateFile(string path)
    {
        if (!File.Exists(path))
        {
            return $"文件不存在：{path}";
        }

        var info = new FileInfo(path);
        if (info.Length < 64)
        {
            return $"文件过小，无法作为有效 Windows 可执行文件：{path}";
        }

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[2];
        if (stream.Read(header) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
        {
            return $"文件缺少 Windows PE 头：{path}";
        }

        return null;
    }
}
