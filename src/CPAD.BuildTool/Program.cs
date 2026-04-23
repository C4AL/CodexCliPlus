var commands = new[]
{
    "fetch-assets",
    "verify-assets",
    "publish",
    "package-portable",
    "package-dev",
    "package-installer",
    "verify-package"
};

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("CPAD.BuildTool");
    Console.WriteLine("Available commands:");
    foreach (var command in commands)
    {
        Console.WriteLine($"  {command}");
    }

    return;
}

var selectedCommand = args[0];
if (!commands.Contains(selectedCommand, StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Unknown command: {selectedCommand}");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"CPAD.BuildTool command '{selectedCommand}' is wired into the solution and will be implemented in phase 9.");
