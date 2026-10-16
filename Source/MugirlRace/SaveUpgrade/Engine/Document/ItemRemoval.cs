namespace Mugirl.SaveUpgrade;

/// <summary>按所在容器决定的列表项删除方式。</summary>
internal enum ItemRemoval
{
    /// <summary>可移除容器：直接删除。</summary>
    Removed,

    /// <summary>字典的键：键与同下标的值一起删除。</summary>
    DictionaryPair,

    /// <summary>并行列表组：各列表同下标的项一起删除。</summary>
    Parallel,

    /// <summary>其他位置（含字典的值）：置空，保留下标。</summary>
    NulledOut,
}
