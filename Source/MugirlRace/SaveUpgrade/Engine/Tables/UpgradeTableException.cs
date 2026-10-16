namespace Mugirl.SaveUpgrade;

/// <summary>升级表格登记有误（重名、链式改名、缺少步骤等）；列出全部问题。</summary>
public sealed class UpgradeTableException : Exception
{
    public UpgradeTableException()
    {
    }

    public UpgradeTableException(string message)
        : base(message)
    {
    }

    public UpgradeTableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal UpgradeTableException(IReadOnlyList<string> problems)
        : base("Save upgrade tables are invalid:\n- " + string.Join("\n- ", problems))
    {
    }
}
