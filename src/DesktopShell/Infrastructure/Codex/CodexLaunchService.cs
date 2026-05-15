using System.Diagnostics;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Enums;
using CodexCliPlus.Core.Models;

namespace CodexCliPlus.Infrastructure.Codex;

public sealed class CodexLaunchService
{
    private readonly CodexConfigService _configService;
    private readonly IAppLogger _logger;
    private readonly Action<ProcessStartInfo> _startProcess;

    public CodexLaunchService(CodexConfigService configService, IAppLogger logger)
        : this(configService, logger, startInfo => Process.Start(startInfo))
    {
    }

    internal CodexLaunchService(
        CodexConfigService configService,
        IAppLogger logger,
        Action<ProcessStartInfo> startProcess
    )
    {
        _configService = configService;
        _logger = logger;
        _startProcess = startProcess;
    }

    public string BuildCommand(CodexSourceKind source, string? repositoryPath)
    {
        return _configService.BuildLaunchCommand(source, repositoryPath);
    }

    public CodexLaunchResult LaunchInTerminal(CodexSourceKind source, string? repositoryPath)
    {
        var command = BuildCommand(source, repositoryPath);
        var workingDirectory = string.IsNullOrWhiteSpace(repositoryPath)
            ? Environment.CurrentDirectory
            : repositoryPath;
        var escapedWorkingDirectory = workingDirectory.Replace("'", "''", StringComparison.Ordinal);

        try
        {
            _startProcess(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoExit -Command \"& {{ Set-Location -LiteralPath '{escapedWorkingDirectory}'; {command} }}\"",
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory,
                }
            );

            _logger.Info(
                $"Codex 启动成功，source={CodexConfigService.GetSourceName(source)}, repo={workingDirectory}, command={command}"
            );
            return new CodexLaunchResult { IsSuccess = true, Command = command };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                $"Codex 启动失败，source={CodexConfigService.GetSourceName(source)}, repo={workingDirectory}, command={command}",
                exception
            );
            return new CodexLaunchResult
            {
                IsSuccess = false,
                Command = command,
                ErrorMessage = exception.Message,
            };
        }
    }
}
