using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace CodexCliPlus.Updater;

public partial class MainWindow : ControlzEx.WindowChromeWindow
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = UpdaterOptions.Parse(Environment.GetCommandLineArgs().Skip(1));
            await ApplyUpdateAsync(options);
            SetStatus("更新完成，正在重启主程序。", complete: true);
            if (!string.IsNullOrWhiteSpace(options.RestartPath) && File.Exists(options.RestartPath))
            {
                Process.Start(new ProcessStartInfo(options.RestartPath) { UseShellExecute = true });
            }
            Close();
        }
        catch (Exception exception)
        {
            SetStatus($"更新失败：{exception.Message}", complete: true);
        }
    }

    private async Task ApplyUpdateAsync(UpdaterOptions options)
    {
        if (options.MainProcessId is { } processId)
        {
            SetStatus("正在等待主程序退出。");
            await WaitForProcessExitAsync(processId);
        }

        if (
            string.IsNullOrWhiteSpace(options.AppDirectory)
            || !Directory.Exists(options.AppDirectory)
        )
        {
            throw new InvalidOperationException("未找到应用目录。");
        }

        if (string.IsNullOrWhiteSpace(options.PackagePath) || !File.Exists(options.PackagePath))
        {
            throw new FileNotFoundException("未找到更新包。", options.PackagePath);
        }

        if (!string.IsNullOrWhiteSpace(options.PackageSha256))
        {
            SetStatus("正在校验更新包。");
            var actual = await ComputeSha256Async(options.PackagePath);
            if (!string.Equals(actual, options.PackageSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新包校验失败。");
            }
        }

        var backupDirectory = Path.Combine(
            options.AppDirectory,
            "persistence",
            "update-backup",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        );
        var extractDirectory = Path.Combine(
            Path.GetTempPath(),
            $"CodexCliPlus.Update.{Guid.NewGuid():N}"
        );

        try
        {
            SetStatus("正在解压更新包。");
            ZipFile.ExtractToDirectory(options.PackagePath, extractDirectory);
            var payloadDirectory = Path.Combine(extractDirectory, "payload");
            if (!Directory.Exists(payloadDirectory))
            {
                throw new InvalidDataException("更新包缺少 payload 目录。");
            }

            var manifestPath = Path.Combine(extractDirectory, "update-manifest.json");
            var files = await ReadAndValidateManifestAsync(manifestPath, payloadDirectory);

            Directory.CreateDirectory(backupDirectory);
            for (var index = 0; index < files.Length; index++)
            {
                var source = files[index].SourcePath;
                var relativePath = files[index].RelativePath;
                var target = Path.Combine(options.AppDirectory, relativePath);
                var backup = Path.Combine(backupDirectory, relativePath);
                EnsurePathStaysInside(options.AppDirectory, target);
                EnsurePathStaysInside(backupDirectory, backup);

                SetStatus($"正在替换文件 {index + 1}/{files.Length}。", index, files.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: true);
                }

                File.Copy(source, target, overwrite: true);
            }
        }
        catch
        {
            RestoreBackup(backupDirectory, options.AppDirectory);
            throw;
        }
        finally
        {
            if (Directory.Exists(extractDirectory))
            {
                Directory.Delete(extractDirectory, recursive: true);
            }
        }
    }

    private static async Task<ValidatedUpdateFile[]> ReadAndValidateManifestAsync(
        string manifestPath,
        string payloadDirectory
    )
    {
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException("更新包缺少清单文件。");
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var manifest =
            JsonSerializer.Deserialize<UpdatePackageManifest>(manifestJson, ManifestJsonOptions)
            ?? throw new InvalidDataException("更新清单无效。");

        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException("更新清单没有文件。");
        }

        var payloadRoot = NormalizeDirectoryPath(payloadDirectory);
        var files = new List<ValidatedUpdateFile>();
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
            {
                throw new InvalidDataException("更新清单包含空文件路径。");
            }

            var relativePath = file.Path.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException("更新清单包含绝对路径。");
            }

            var sourcePath = Path.GetFullPath(Path.Combine(payloadDirectory, relativePath));
            if (!sourcePath.StartsWith(payloadRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新清单包含越界路径。");
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("更新包缺少清单中的文件。", file.Path);
            }

            var fileInfo = new FileInfo(sourcePath);
            if (file.Size >= 0 && fileInfo.Length != file.Size)
            {
                throw new InvalidDataException($"更新文件大小不匹配：{file.Path}");
            }

            var actualHash = await ComputeSha256Async(sourcePath);
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"更新文件校验失败：{file.Path}");
            }

            files.Add(new ValidatedUpdateFile(relativePath, sourcePath));
        }

        return files.ToArray();
    }

    private static void EnsurePathStaysInside(string rootDirectory, string candidatePath)
    {
        var root = NormalizeDirectoryPath(rootDirectory);
        var candidate = Path.GetFullPath(candidatePath);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新文件路径越界。");
        }
    }

    private static string NormalizeDirectoryPath(string directory)
    {
        return Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    private static async Task WaitForProcessExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
        }
        catch (ArgumentException) { }
    }

    private static void RestoreBackup(string backupDirectory, string appDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        foreach (
            var backup in Directory.EnumerateFiles(
                backupDirectory,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            var relativePath = Path.GetRelativePath(backupDirectory, backup);
            var target = Path.Combine(appDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, overwrite: true);
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private void SetStatus(string text, int current = 0, int total = 0, bool complete = false)
    {
        StatusText.Text = text;
        Progress.IsIndeterminate = !complete && total <= 0;
        if (total > 0)
        {
            Progress.Value = Math.Clamp(current * 100d / total, 0, 100);
        }
        CloseButton.IsEnabled = complete;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

internal sealed class UpdatePackageManifest
{
    public List<UpdatePackageManifestFile> Files { get; init; } = [];
}

internal sealed class UpdatePackageManifestFile
{
    public string Path { get; init; } = string.Empty;

    public long Size { get; init; } = -1;

    public string Sha256 { get; init; } = string.Empty;
}

internal sealed record ValidatedUpdateFile(string RelativePath, string SourcePath);

internal sealed class UpdaterOptions
{
    public int? MainProcessId { get; init; }

    public string AppDirectory { get; init; } = string.Empty;

    public string PackagePath { get; init; } = string.Empty;

    public string? PackageSha256 { get; init; }

    public string? RestartPath { get; init; }

    public static UpdaterOptions Parse(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = args.ToArray();
        for (var index = 0; index < list.Length; index++)
        {
            var arg = list[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            var value =
                index + 1 < list.Length
                && !list[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? list[++index]
                    : string.Empty;
            values[key] = value;
        }

        return new UpdaterOptions
        {
            MainProcessId = int.TryParse(values.GetValueOrDefault("pid"), out var pid) ? pid : null,
            AppDirectory = values.GetValueOrDefault("app-dir") ?? string.Empty,
            PackagePath = values.GetValueOrDefault("package") ?? string.Empty,
            PackageSha256 = values.GetValueOrDefault("sha256"),
            RestartPath = values.GetValueOrDefault("restart"),
        };
    }
}
