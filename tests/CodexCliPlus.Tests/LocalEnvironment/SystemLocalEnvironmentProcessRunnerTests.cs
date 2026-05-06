using System.Text;
using CodexCliPlus.Infrastructure.LocalEnvironment;

namespace CodexCliPlus.Tests.LocalEnvironment;

[Trait("Category", "LocalIntegration")]
public sealed class SystemLocalEnvironmentProcessRunnerTests
{
    [Fact]
    public void DecodeProcessOutputDetectsUtf16LittleEndianWithoutBom()
    {
        const string output = "未安装适用于 Linux 的 Windows 子系统。运行 wsl.exe --install。";
        var bytes = Encoding.Unicode.GetBytes(output);

        var decoded = SystemLocalEnvironmentProcessRunner.DecodeProcessOutput(bytes);

        Assert.Equal(output, decoded);
    }

    [Fact]
    public void DecodeProcessOutputKeepsUtf8Output()
    {
        const string output = "node v22.12.0";
        var bytes = Encoding.UTF8.GetBytes(output);

        var decoded = SystemLocalEnvironmentProcessRunner.DecodeProcessOutput(bytes);

        Assert.Equal(output, decoded);
    }
}
