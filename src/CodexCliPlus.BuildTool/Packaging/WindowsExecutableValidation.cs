using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Constants;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CodexCliPlus.BuildTool;

public static class WindowsExecutableValidation
{
    public static string? ValidateFile(string path)
    {
        if (!File.Exists(path))
        {
            return $"Installer executable missing: {path}";
        }

        var info = new FileInfo(path);
        if (info.Length < 64)
        {
            return $"Installer executable is too small to be valid: {path}";
        }

        using var stream = File.OpenRead(path);
        return ValidateStream(stream, path);
    }

    public static string? ValidateStream(Stream stream, string displayPath)
    {
        Span<byte> header = stackalloc byte[2];
        if (stream.Read(header) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
        {
            return $"Installer executable does not have a Windows PE header: {displayPath}";
        }

        return null;
    }
}
