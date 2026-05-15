namespace CodexCliPlus.BuildTool;

public static class ProcessExecutableResolver
{
    public static string ResolveNpmExecutable()
    {
        return ResolveNpmExecutable(Environment.GetEnvironmentVariable("PATH"));
    }

    public static string ResolveNpmExecutable(string? pathValue)
    {
        var candidateNames = OperatingSystem.IsWindows()
            ? new[] { "npm.cmd", "npm.exe", "npm" }
            : new[] { "npm" };

        foreach (var candidateName in candidateNames)
        {
            var resolvedPath = TryResolveFromPath(candidateName, pathValue);
            if (resolvedPath is not null)
            {
                return resolvedPath;
            }
        }

        return candidateNames[0];
    }

    private static string? TryResolveFromPath(string fileName, string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (
            var segment in pathValue.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var candidatePath = Path.Combine(segment.Trim('"'), fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }
}
