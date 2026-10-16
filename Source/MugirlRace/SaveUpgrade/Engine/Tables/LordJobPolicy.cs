namespace Mugirl.SaveUpgrade;

/// <summary>旧存档中 Mugirl LordJob 的状态图如何升级（状态图在 PostLoadInit 按下标恢复，必须在 DOM 中处理）。</summary>
internal enum LordJobPolicy
{
    /// <summary>删除 <c>lordToilData</c>、<c>triggerData</c>、<c>ticksInToil</c>，从第一个 LordToil 重新开始。</summary>
    ResetGraph,

    /// <summary>删除整个 Lord，并把对 <c>Lord_&lt;id&gt;</c> 的引用置空。</summary>
    RemoveLord,

    /// <summary>由登记的步骤处理。</summary>
    Claimed,

    /// <summary>状态图和保存字段含义不变，保留当前 LordToil、计时与触发器数据。</summary>
    Preserve,
}
