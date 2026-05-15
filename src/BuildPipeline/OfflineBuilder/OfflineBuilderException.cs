namespace CodexCliPlus.OfflineBuilder;

internal sealed class OfflineBuilderException : Exception
{
    public OfflineBuilderException(string message)
        : base(message) { }

    public OfflineBuilderException(string message, Exception innerException)
        : base(message, innerException) { }
}
