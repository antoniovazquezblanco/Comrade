namespace COMrade.Core.Updates;

/// <summary>Checking for updates failed; the message says why, for the settings page.</summary>
public sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(string message)
        : base(message)
    {
    }

    public UpdateCheckException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
