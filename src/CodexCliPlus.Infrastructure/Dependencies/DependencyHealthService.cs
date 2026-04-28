using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using CodexCliPlus.Core.Abstractions.Paths;
using CodexCliPlus.Core.Abstractions.Security;
using CodexCliPlus.Core.Abstractions.Updates;
using CodexCliPlus.Core.Constants;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Exceptions;
using CodexCliPlus.Core.Models;
using CodexCliPlus.Infrastructure.Platform;

namespace CodexCliPlus.Infrastructure.Dependencies;

public sealed class DependencyHealthService
{
    private const int RequiredRuntimeMajorVersion = 10;
    private static readonly string[] RequiredResourceFiles =
    [
        "LICENSE",
        "README.md",
        "README_CN.md",
        "config.example.yaml"
    ];

    private readonly IPathService _pathService;
    private readonly DirectoryAccessService _directoryAccessService;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly Func<string> _frameworkDescriptionProvider;
    private readonly Func<string?> _expectedBackendAssetRootResolver;

    public DependencyHealthService(
        IPathService pathService,
        DirectoryAccessService directoryAccessService,
        ISecureCredentialStore credentialStore,
        IUpdateCheckService updateCheckService,
        Func<string>? frameworkDescriptionProvider = null,
        Func<string?>? expectedBackendAssetRootResolver = null)
    {
        _pathService = pathService;
        _directoryAccessService = directoryAccessService;
        _credentialStore = credentialStore;
        _updateCheckService = updateCheckService;
        _frameworkDescriptionProvider = frameworkDescriptionProvider ?? (() => RuntimeInformation.FrameworkDescription);
        _expectedBackendAssetRootResolver = expectedBackendAssetRootResolver ?? ResolveExpectedBackendAssetRoot;
    }

    public async Task<DependencyCheckResult> EvaluateAsync(
        BackendStatusSnapshot backendStatus,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<DependencyCheckIssue>();

        CheckRuntimeVersion(issues);
        CheckDirectoryAccess(issues);
        CheckBackendRuntime(issues);
        CheckInitializationState(issues);
        await CheckCredentialStoreAsync(issues, cancellationToken);
        await CheckUpdateComponentAsync(issues, cancellationToken);
        CheckPortAllocation(issues, backendStatus);
        CheckResourcePack(issues);

        if (issues.Count == 0)
        {
            return new DependencyCheckResult
            {
                IsAvailable = true,
                RequiresRepairMode = false,
                Summary = "Desktop dependencies are ready for full functionality.",
                Detail = Path.Combine(
                    _pathService.Directories.BackendDirectory,
                    BackendExecutableNames.ManagedExecutableFileName)
            };
        }

        return new DependencyCheckResult
        {
            IsAvailable = false,
            RequiresRepairMode = true,
            Summary = $"Dependency repair mode is active because {issues.Count} blocking checks failed.",
            Detail = string.Join(Environment.NewLine, issues.Select(issue => $"- {issue.Title}: {issue.Detail}")),
            Issues = issues
        };
    }

    private void CheckRuntimeVersion(List<DependencyCheckIssue> issues)
    {
        var frameworkDescription = _frameworkDescriptionProvider();
        var match = Regex.Match(frameworkDescription, "(?<major>\\d+)");
        if (!match.Success || !int.TryParse(match.Groups["major"].Value, out var majorVersion))
        {
            return;
        }

        if (majorVersion >= RequiredRuntimeMajorVersion)
        {
            return;
        }

        issues.Add(new DependencyCheckIssue
        {
            Code = "runtime-version",
            Title = "Runtime version is below the desktop requirement.",
            Detail = $"Current runtime '{frameworkDescription}' does not meet the required .NET {RequiredRuntimeMajorVersion} desktop baseline.",
            CanRepairNow = false
        });
    }

    private void CheckDirectoryAccess(List<DependencyCheckIssue> issues)
    {
        var failures = new List<string>();
        AddDirectoryFailure(failures, "Root", _pathService.Directories.RootDirectory);
        AddDirectoryFailure(failures, "Config", _pathService.Directories.ConfigDirectory);
        AddDirectoryFailure(failures, "Logs", _pathService.Directories.LogsDirectory);
        AddDirectoryFailure(failures, "Diagnostics", _pathService.Directories.DiagnosticsDirectory);
        AddDirectoryFailure(failures, "Backend", _pathService.Directories.BackendDirectory);

        if (failures.Count == 0)
        {
            return;
        }

        issues.Add(new DependencyCheckIssue
        {
            Code = "directory-access",
            Title = "Managed desktop directories are not writable.",
            Detail = string.Join(" | ", failures),
            CanRepairNow = false
        });
    }

    private void CheckBackendRuntime(List<DependencyCheckIssue> issues)
    {
        var executablePath = Path.Combine(
            _pathService.Directories.BackendDirectory,
            BackendExecutableNames.ManagedExecutableFileName);
        if (!File.Exists(executablePath))
        {
            issues.Add(new DependencyCheckIssue
            {
                Code = "backend-runtime",
                Title = "Go backend runtime files are missing.",
                Detail = $"Expected executable was not found at '{executablePath}'.",
                CanRepairNow = true
            });
            return;
        }

        var fileInfo = new FileInfo(executablePath);
        if (fileInfo.Length <= 0)
        {
            issues.Add(new DependencyCheckIssue
            {
                Code = "backend-runtime",
                Title = "Go backend runtime files are damaged.",
                Detail = $"Managed executable '{executablePath}' is empty and cannot be launched.",
                CanRepairNow = true
            });
            return;
        }

        var expectedRoot = _expectedBackendAssetRootResolver();
        if (expectedRoot is null)
        {
            return;
        }

        var expectedExecutable = Path.Combine(
            expectedRoot,
            BackendExecutableNames.ManagedExecutableFileName);
        if (!File.Exists(expectedExecutable) || FilesMatch(executablePath, expectedExecutable))
        {
            return;
        }

        issues.Add(new DependencyCheckIssue
        {
            Code = "backend-runtime",
            Title = "Go backend runtime files failed integrity validation.",
            Detail = $"Managed executable '{executablePath}' does not match the bundled backend asset source.",
            CanRepairNow = true
        });
    }

    private void CheckInitializationState(List<DependencyCheckIssue> issues)
    {
        var settingsExists = File.Exists(_pathService.Directories.SettingsFilePath);
        var backendConfigExists = File.Exists(_pathService.Directories.BackendConfigFilePath);
        var secretFilesExist = HasSecretFiles();

        if (!backendConfigExists && !secretFilesExist)
        {
            return;
        }

        if (settingsExists && backendConfigExists)
        {
            return;
        }

        issues.Add(new DependencyCheckIssue
        {
            Code = "initialization",
            Title = "First-run initialization is incomplete.",
            Detail =
                $"Detected a partial desktop state: settings={settingsExists}, backendConfig={backendConfigExists}, secrets={secretFilesExist}.",
            CanRepairNow = false
        });
    }

    private async Task CheckCredentialStoreAsync(
        List<DependencyCheckIssue> issues,
        CancellationToken cancellationToken)
    {
        var probeReference = $"dependency-health-{Guid.NewGuid():N}";
        var probeValue = Guid.NewGuid().ToString("N");

        try
        {
            await _credentialStore.SaveSecretAsync(probeReference, probeValue, cancellationToken);
            var storedValue = await _credentialStore.LoadSecretAsync(probeReference, cancellationToken);
            if (string.Equals(storedValue, probeValue, StringComparison.Ordinal))
            {
                return;
            }

            issues.Add(new DependencyCheckIssue
            {
                Code = "credential-store",
                Title = "Credential Manager or DPAPI is unavailable.",
                Detail = "Secure credential storage returned an unexpected value during the probe round-trip.",
                CanRepairNow = false
            });
        }
        catch (SecureCredentialStoreException exception)
        {
            issues.Add(new DependencyCheckIssue
            {
                Code = "credential-store",
                Title = "Credential Manager or DPAPI is unavailable.",
                Detail = exception.Message,
                CanRepairNow = false
            });
        }
        finally
        {
            try
            {
                await _credentialStore.DeleteSecretAsync(probeReference, cancellationToken);
            }
            catch
            {
            }
        }
    }

    private async Task CheckUpdateComponentAsync(
        List<DependencyCheckIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _updateCheckService.CheckAsync("0.0.0", UpdateChannel.Beta, cancellationToken);
            if (result.IsCheckSuccessful &&
                result.IsChannelReserved &&
                !string.IsNullOrWhiteSpace(result.ApiUrl) &&
                !string.IsNullOrWhiteSpace(result.ReleasePageUrl))
            {
                return;
            }

            issues.Add(new DependencyCheckIssue
            {
                Code = "update-component",
                Title = "Update component metadata is missing or damaged.",
                Detail =
                    $"Reserved update probe returned status '{result.Status}' with repository '{result.Repository}' and release page '{result.ReleasePageUrl ?? "(missing)"}'.",
                CanRepairNow = false
            });
        }
        catch (Exception exception)
        {
            issues.Add(new DependencyCheckIssue
            {
                Code = "update-component",
                Title = "Update component metadata is missing or damaged.",
                Detail = exception.Message,
                CanRepairNow = false
            });
        }
    }

    private static void CheckPortAllocation(
        List<DependencyCheckIssue> issues,
        BackendStatusSnapshot backendStatus)
    {
        if (backendStatus.State != BackendStateKind.Error)
        {
            return;
        }

        var detail = backendStatus.LastError ?? backendStatus.Message;
        if (!detail.Contains("No available loopback port was found", StringComparison.OrdinalIgnoreCase) &&
            !detail.Contains("CodexCliPlus backend port", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        issues.Add(new DependencyCheckIssue
        {
            Code = "port-allocation",
            Title = "Critical backend port allocation failed.",
            Detail = detail,
            CanRepairNow = false
        });
    }

    private void CheckResourcePack(List<DependencyCheckIssue> issues)
    {
        var expectedRoot = _expectedBackendAssetRootResolver();
        var missingFiles = RequiredResourceFiles
            .Where(fileName => !File.Exists(Path.Combine(_pathService.Directories.BackendDirectory, fileName)))
            .ToArray();

        if (missingFiles.Length > 0)
        {
            issues.Add(new DependencyCheckIssue
            {
                Code = "resource-pack",
                Title = "Required backend resource pack is missing.",
                Detail = $"Missing files: {string.Join(", ", missingFiles)}.",
                CanRepairNow = true
            });
            return;
        }

        if (expectedRoot is null)
        {
            return;
        }

        var mismatchedFiles = RequiredResourceFiles
            .Where(fileName =>
            {
                var managedPath = Path.Combine(_pathService.Directories.BackendDirectory, fileName);
                var expectedPath = Path.Combine(expectedRoot, fileName);
                return File.Exists(expectedPath) && !FilesMatch(managedPath, expectedPath);
            })
            .ToArray();

        if (mismatchedFiles.Length == 0)
        {
            return;
        }

        issues.Add(new DependencyCheckIssue
        {
            Code = "resource-pack",
            Title = "Required backend resource pack version does not match the bundled source.",
            Detail = $"Mismatched files: {string.Join(", ", mismatchedFiles)}.",
            CanRepairNow = true
        });
    }

    private void AddDirectoryFailure(List<string> failures, string label, string path)
    {
        var error = _directoryAccessService.GetWriteAccessError(path);
        if (!string.IsNullOrWhiteSpace(error))
        {
            failures.Add($"{label}: {error}");
        }
    }

    private bool HasSecretFiles()
    {
        var secretDirectory = Path.Combine(_pathService.Directories.ConfigDirectory, AppConstants.SecretsDirectoryName);
        return Directory.Exists(secretDirectory) && Directory.EnumerateFiles(secretDirectory, "*.bin").Any();
    }

    private static bool FilesMatch(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        return string.Equals(ComputeHash(leftPath), ComputeHash(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string? ResolveExpectedBackendAssetRoot()
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "assets", "backend", "windows-x64");
        if (File.Exists(Path.Combine(bundledRoot, BackendExecutableNames.ManagedExecutableFileName)))
        {
            return bundledRoot;
        }

        var repositoryRoot = TryFindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return null;
        }

        var repositoryAssets = Path.Combine(repositoryRoot, AppConstants.ResourcesDirectoryName, "backend", "windows-x64");
        return File.Exists(Path.Combine(repositoryAssets, BackendExecutableNames.ManagedExecutableFileName))
            ? repositoryAssets
            : null;
    }

    private static string? TryFindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "CodexCliPlus.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
    }
}
