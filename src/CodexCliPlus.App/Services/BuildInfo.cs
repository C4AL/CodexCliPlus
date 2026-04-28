using System.Reflection;

using CodexCliPlus.Core.Abstractions.Build;

namespace CodexCliPlus.Services;

public sealed class BuildInfo : IBuildInfo
{
    private readonly Assembly _assembly = typeof(BuildInfo).Assembly;

    public string ApplicationVersion =>
        _assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public string InformationalVersion =>
        _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? ApplicationVersion;
}
