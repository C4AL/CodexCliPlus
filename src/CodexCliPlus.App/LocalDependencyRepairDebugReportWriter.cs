using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CodexCliPlus.Core.Models.LocalEnvironment;

namespace CodexCliPlus;

internal static class LocalDependencyRepairDebugReportWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string WriteToDesktop(
        string desktopDirectory,
        string applicationVersion,
        string informationalVersion,
        string? requestId,
        LocalDependencyRepairResult result,
        LocalDependencySnapshot? snapshot,
        LocalDependencyRepairProgress? lastProgress,
        string? applicationLogPath,
        DateTimeOffset generatedAt
    )
    {
        if (string.IsNullOrWhiteSpace(desktopDirectory))
        {
            throw new InvalidOperationException("桌面目录不可用。");
        }

        var normalizedDesktopDirectory = Path.GetFullPath(desktopDirectory);
        Directory.CreateDirectory(normalizedDesktopDirectory);
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"CodexCliPlus-本地环境修复报告-{generatedAt.LocalDateTime:yyyyMMdd-HHmmss-fff}.txt"
        );
        var filePath = Path.Combine(normalizedDesktopDirectory, fileName);
        File.WriteAllText(
            filePath,
            BuildReport(
                applicationVersion,
                informationalVersion,
                requestId,
                result,
                snapshot,
                lastProgress,
                applicationLogPath,
                generatedAt
            ),
            Utf8NoBom
        );
        return filePath;
    }

    private static string BuildReport(
        string applicationVersion,
        string informationalVersion,
        string? requestId,
        LocalDependencyRepairResult result,
        LocalDependencySnapshot? snapshot,
        LocalDependencyRepairProgress? lastProgress,
        string? applicationLogPath,
        DateTimeOffset generatedAt
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("CodexCliPlus 本地环境修复失败调试报告");
        builder.AppendLine("请将本文件完整提供给 Codex，用于继续诊断和生成下一步修复方案。");

        AppendSection(builder, "基础信息");
        AppendValue(builder, "生成时间", generatedAt.ToString("O", CultureInfo.InvariantCulture));
        AppendValue(builder, "桌面版本", Normalize(applicationVersion));
        AppendValue(builder, "信息版本", Normalize(informationalVersion));
        AppendValue(builder, "请求 ID", Normalize(requestId));
        AppendValue(builder, "动作 ID", Normalize(result.ActionId));
        AppendValue(builder, "操作系统", Environment.OSVersion.VersionString);
        AppendValue(builder, "进程架构", RuntimeInformation.ProcessArchitecture.ToString());
        AppendValue(builder, ".NET 运行时", Environment.Version.ToString());
        AppendValue(builder, "应用日志", Normalize(applicationLogPath));

        AppendSection(builder, "修复结果");
        AppendValue(builder, "是否成功", result.Succeeded ? "是" : "否");
        AppendValue(builder, "摘要", Normalize(result.Summary));
        AppendValue(builder, "详情", Normalize(result.Detail));
        AppendValue(builder, "退出码", result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "无");
        AppendValue(builder, "修复日志", Normalize(result.LogPath));

        AppendSection(builder, "最后进度");
        if (lastProgress is null)
        {
            builder.AppendLine("未收到修复进度。");
        }
        else
        {
            AppendValue(builder, "阶段", Normalize(lastProgress.Phase));
            AppendValue(builder, "消息", Normalize(lastProgress.Message));
            AppendValue(builder, "详情", Normalize(lastProgress.Detail));
            AppendValue(builder, "命令", Normalize(lastProgress.CommandLine));
            AppendValue(
                builder,
                "更新时间",
                lastProgress.UpdatedAt == default
                    ? "无"
                    : lastProgress.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)
            );
            AppendValue(
                builder,
                "退出码",
                lastProgress.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "无"
            );
            AppendLines(builder, "最近输出", lastProgress.RecentOutput);
        }

        AppendSection(builder, "修复后检测");
        if (snapshot is null)
        {
            builder.AppendLine("未取得修复后本地环境快照。");
        }
        else
        {
            AppendValue(
                builder,
                "检测时间",
                snapshot.CheckedAt.ToString("O", CultureInfo.InvariantCulture)
            );
            AppendValue(
                builder,
                "就绪分",
                snapshot.ReadinessScore.ToString(CultureInfo.InvariantCulture)
            );
            AppendValue(builder, "摘要", Normalize(snapshot.Summary));
            AppendDependencyItems(builder, snapshot.Items);
            AppendRepairCapabilities(builder, snapshot.RepairCapabilities);
        }

        AppendSection(builder, "PATH 线索");
        AppendPath(builder, "当前进程 PATH", SafeGetEnvironmentVariable("PATH"));
        AppendPath(builder, "用户 PATH", SafeGetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
        AppendPath(builder, "系统 PATH", SafeGetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));

        AppendSection(builder, "修复指南");
        builder.AppendLine("1. 先查看“修复结果”和“最后进度”，定位失败命令、退出码和最近输出。");
        builder.AppendLine("2. 如“修复日志”存在，请结合该日志末尾内容判断是下载、权限、PATH、npm、winget 还是 PowerShell 问题。");
        builder.AppendLine("3. 如果 PATH 已被修复，CodexCliPlus 可直接重新检测；外部已打开的终端需要重新打开后才会读取新的 PATH。");
        builder.AppendLine("4. 如果 Node.js 或 npm 仍缺失，优先确认官方安装包是否完成安装，再检查用户 PATH 中的 nodejs 和 npm 目录。");
        builder.AppendLine("5. 如果 Codex CLI 安装失败，优先确认 npm 可用、网络可访问 npm registry，并检查管理员授权是否被取消。");
        builder.AppendLine("6. 如果 winget 或 PowerShell 不可用，先修复对应系统组件，再重新执行本地环境修复。");

        return builder.ToString();
    }

    private static void AppendDependencyItems(
        StringBuilder builder,
        IReadOnlyList<LocalDependencyItem> items
    )
    {
        builder.AppendLine("依赖项目：");
        if (items.Count == 0)
        {
            builder.AppendLine("- 无");
            return;
        }

        foreach (var item in items)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {Normalize(item.Name)} ({Normalize(item.Id)}): {item.Status} / {item.Severity}"
            );
            AppendValue(builder, "  版本", Normalize(item.Version));
            AppendValue(builder, "  路径", Normalize(item.Path));
            AppendValue(builder, "  详情", Normalize(item.Detail));
            AppendValue(builder, "  建议", Normalize(item.Recommendation));
            AppendValue(builder, "  修复动作", Normalize(item.RepairActionId));
        }
    }

    private static void AppendRepairCapabilities(
        StringBuilder builder,
        IReadOnlyList<LocalDependencyRepairCapability> capabilities
    )
    {
        builder.AppendLine("修复能力：");
        if (capabilities.Count == 0)
        {
            builder.AppendLine("- 无");
            return;
        }

        foreach (var capability in capabilities)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {Normalize(capability.Name)} ({Normalize(capability.ActionId)}): {(capability.IsAvailable ? "可用" : "不可用")}"
            );
            AppendValue(builder, "  需要管理员", capability.RequiresElevation ? "是" : "否");
            AppendValue(builder, "  可选项", capability.IsOptional ? "是" : "否");
            AppendValue(builder, "  详情", Normalize(capability.Detail));
        }
    }

    private static void AppendPath(StringBuilder builder, string label, string? value)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"{label}：");
        var entries = string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
        {
            builder.AppendLine("- 无");
            return;
        }

        foreach (var entry in entries)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {entry.Trim()}");
        }
    }

    private static void AppendLines(
        StringBuilder builder,
        string label,
        IReadOnlyList<string> lines
    )
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"{label}：");
        if (lines.Count == 0)
        {
            builder.AppendLine("- 无");
            return;
        }

        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }
    }

    private static void AppendSection(StringBuilder builder, string title)
    {
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"[{title}]");
    }

    private static void AppendValue(StringBuilder builder, string label, string value)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"{label}：{value}");
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "无" : value.Trim();
    }

    private static string? SafeGetEnvironmentVariable(string variable)
    {
        try
        {
            return Environment.GetEnvironmentVariable(variable);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetEnvironmentVariable(
        string variable,
        EnvironmentVariableTarget target
    )
    {
        try
        {
            return Environment.GetEnvironmentVariable(variable, target);
        }
        catch
        {
            return null;
        }
    }
}
