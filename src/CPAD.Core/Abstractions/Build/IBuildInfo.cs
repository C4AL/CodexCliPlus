namespace CPAD.Core.Abstractions.Build;

public interface IBuildInfo
{
    string ApplicationVersion { get; }

    string InformationalVersion { get; }
}
