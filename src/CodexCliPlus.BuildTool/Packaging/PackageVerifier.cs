using System.IO.Compression;
using CodexCliPlus.Core.Constants;

namespace CodexCliPlus.BuildTool;

public sealed class PackageVerifier
{
    private readonly BuildContext context;
    private readonly ReleasePackageSelection releasePackages;

    public PackageVerifier(
        BuildContext context,
        ReleasePackageSelection releasePackages = ReleasePackageSelection.All
    )
    {
        this.context = context;
        this.releasePackages = releasePackages;
    }

    public IReadOnlyList<string> VerifyAll()
    {
        var failures = new List<string>();
        if (releasePackages.IncludesOnlineInstaller())
        {
            VerifyInstallerPackage(InstallerPackageKind.Online, failures);
        }

        if (releasePackages.IncludesOfflineInstaller())
        {
            VerifyInstallerPackage(InstallerPackageKind.Offline, failures);
        }

        if (releasePackages.IncludesUpdatePackage())
        {
            VerifyUpdatePackage(failures);
        }

        return failures;
    }

    private void VerifyInstallerPackage(InstallerPackageKind packageKind, List<string> failures)
    {
        var packageMoniker = packageKind == InstallerPackageKind.Online ? "Online" : "Offline";
        var installerName =
            $"{AppConstants.InstallerNamePrefix}.{packageMoniker}.{context.Options.Version}.exe";
        VerifyExecutable(Path.Combine(context.PackageRoot, installerName), failures);
    }

    private void VerifyUpdatePackage(List<string> failures)
    {
        var updatePackagePath = Path.Combine(
            context.PackageRoot,
            $"CodexCliPlus.Update.{context.Options.Version}.{context.Options.Runtime}.zip"
        );
        VerifyZip(updatePackagePath, "update-manifest.json", failures);
        VerifyZipExecutable(updatePackagePath, $"payload/{AppConstants.ExecutableName}", failures);
    }

    private static void VerifyZip(string path, string requiredEntry, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var found = archive.Entries.Any(entry =>
                string.Equals(
                    NormalizeEntryName(entry.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (!found)
            {
                failures.Add($"Package '{path}' is missing entry '{requiredEntry}'.");
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private static void VerifyZipExecutable(
        string path,
        string requiredEntry,
        List<string> failures
    )
    {
        if (!File.Exists(path))
        {
            failures.Add($"Package missing: {path}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(
                    NormalizeEntryName(item.FullName),
                    requiredEntry,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (entry is null)
            {
                failures.Add($"Package '{path}' is missing entry '{requiredEntry}'.");
                return;
            }

            if (entry.Length < 64)
            {
                failures.Add(
                    $"Installer executable is too small to be valid: {path}!{requiredEntry}"
                );
                return;
            }

            using var stream = entry.Open();
            var validationFailure = WindowsExecutableValidation.ValidateStream(
                stream,
                $"{path}!{requiredEntry}"
            );
            if (validationFailure is not null)
            {
                failures.Add(validationFailure);
            }
        }
        catch (InvalidDataException exception)
        {
            failures.Add($"Package '{path}' is not a readable zip archive: {exception.Message}");
        }
    }

    private static void VerifyExecutable(string path, List<string> failures)
    {
        if (!File.Exists(path))
        {
            failures.Add($"Installer executable missing: {path}");
            return;
        }

        var validationFailure = WindowsExecutableValidation.ValidateFile(path);
        if (validationFailure is not null)
        {
            failures.Add(validationFailure);
        }
    }

    private static string NormalizeEntryName(string name)
    {
        return name.Replace('\\', '/');
    }
}
