namespace Mugirl.SaveUpgrade;

/// <summary>旧存档中进行中的 Mugirl JobDriver 在读档后的处理方式。</summary>
internal enum JobDriverPolicy
{
    /// <summary>仅在明确登记时，于阶段 B 结束进行中的工作并移除排队的同类工作。</summary>
    EndInFlight,

    /// <summary>由登记的步骤处理。</summary>
    Claimed,

    /// <summary>Toil 序列及保存字段含义不变，保留进行中与排队的工作。</summary>
    Preserve,
}
