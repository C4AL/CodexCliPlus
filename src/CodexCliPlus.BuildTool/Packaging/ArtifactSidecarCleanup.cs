namespace CodexCliPlus.BuildTool;

public static class ArtifactSidecarCleanup
{
    private static readonly string[] LegacySuffixes =
    [
        ".signature" + ".json",
        ".unsigned" + ".json",
    ];

    public static void DeleteLegacySidecars(string artifactPath)
    {
        foreach (var suffix in LegacySuffixes)
        {
            DeleteIfExists(artifactPath + suffix);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
