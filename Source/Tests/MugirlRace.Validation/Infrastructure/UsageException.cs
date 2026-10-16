namespace MugirlRace.Validation.Infrastructure;

/// <summary>无效的命令行。</summary>
internal sealed class UsageException : Exception
{
    public UsageException(string message)
        : base(message)
    {
    }

    public UsageException()
    {
    }

    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
