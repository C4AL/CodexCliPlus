namespace CPAD.Core.Exceptions;

public sealed class SecureCredentialStoreException : Exception
{
    public SecureCredentialStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
