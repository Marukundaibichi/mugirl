namespace MugirlRace.Validation.Infrastructure;

/// <summary>环境无法验证：缺少或无法读取必需的文件、目录。</summary>
internal sealed class EnvironmentException : Exception
{
    public EnvironmentException(string message)
        : base(message)
    {
    }

    public EnvironmentException()
    {
    }

    public EnvironmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
