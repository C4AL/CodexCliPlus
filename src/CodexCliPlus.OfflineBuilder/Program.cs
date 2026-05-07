using System.Text;

namespace CodexCliPlus.OfflineBuilder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (args.Length > 0 && IsHelp(args[0]))
        {
            WriteHelp(Console.Out);
            return 0;
        }

        if (!OfflineBuilderOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            WriteHelp(Console.Error);
            return 2;
        }

        try
        {
            var processRunner = new OfflineBuilderProcessRunner();
            var toolchain = new PortableToolchainResolver(
                processRunner,
                new HttpToolArchiveDownloader(),
                new ZipToolArchiveExtractor()
            );
            var service = new OfflinePackageBuildService(processRunner, toolchain);
            await service.BuildAsync(options);
            return 0;
        }
        catch (OfflineBuilderException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"构建离线安装包失败：{exception.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string arg)
    {
        return arg is "-h" or "--help" or "help" or "/?";
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("构建离线安装包");
        writer.WriteLine("用法：构建离线安装包.exe [选项]");
        writer.WriteLine("选项：");
        writer.WriteLine("  --version <version>                         默认：1.0.0");
        writer.WriteLine("  --runtime <win-x64>                         默认：win-x64");
        writer.WriteLine("  --output <path>                             默认：artifacts/buildtool");
        writer.WriteLine("  --desktop <path>                            默认：当前用户桌面");
        writer.WriteLine("  --force-rebuild <none|webui|publish|installer|all>  默认：none");
    }
}
