using DesktopHost.Core.Abstractions.Logging;
using DesktopHost.Core.Enums;
using DesktopHost.Core.Models;

namespace DesktopHost.Infrastructure.Codex;

public sealed class CodexLaunchService
{
    private readonly CodexConfigService _configService;
    private readonly IAppLogger _logger;

    public CodexLaunchService(CodexConfigService configService, IAppLogger logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public string BuildCommand(CodexSourceKind source, string? repositoryPath)
    {
        return _configService.BuildLaunchCommand(source, repositoryPath);
    }

    public CodexLaunchResult LaunchInTerminal(CodexSourceKind source, string? repositoryPath)
    {
        var command = BuildCommand(source, repositoryPath);
        var escapedCommand = command.Replace("'", "''", StringComparison.Ordinal);
        var workingDirectory = string.IsNullOrWhiteSpace(repositoryPath)
            ? Environment.CurrentDirectory
            : repositoryPath;
        var escapedWorkingDirectory = workingDirectory.Replace("'", "''", StringComparison.Ordinal);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"& {{ Set-Location -LiteralPath '{escapedWorkingDirectory}'; {escapedCommand} }}\"",
                UseShellExecute = true,
                WorkingDirectory = workingDirectory
            });

            _logger.Info(
                $"Codex 启动请求：source={CodexConfigService.GetSourceName(source)}, repo={workingDirectory}, command={command}");
            return new CodexLaunchResult
            {
                IsSuccess = true,
                Command = command
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                $"Codex 启动失败：source={CodexConfigService.GetSourceName(source)}, repo={workingDirectory}, command={command}",
                exception);
            return new CodexLaunchResult
            {
                IsSuccess = false,
                Command = command,
                ErrorMessage = exception.Message
            };
        }
    }
}
