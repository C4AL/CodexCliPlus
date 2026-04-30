namespace CodexCliPlus.Core.Models.LocalEnvironment;

public enum LocalDependencyStatus
{
    Ready,
    Warning,
    Missing,
    Error,
    OptionalUnavailable,
    Repairing,
}
