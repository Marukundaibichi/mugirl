namespace Mugirl.SaveUpgrade;

/// <summary>已发布驱动的保存协议：旧两段步行流程不写 count；单段起飞在初始化时先写 count=1。</summary>
internal static class LanceJobContinuation
{
    internal enum Action { KeepCurrent, ResumeWalking }
    internal readonly record struct Snapshot(int Current, int Next, int Count, bool Ended, bool HasThingTarget, bool HasCellTarget = false);

    internal static Action Resolve(Snapshot state)
    {
        if (state.Ended)
        {
            throw new InvalidOperationException("An ended lance driver cannot establish a live continuation.");
        }
        if (state.Current == -1 && state.Next is -1 or 0 && (state.Count == 1 || (state.Count == -1 && state.HasCellTarget)))
        {
            // count=1 是现代起飞标记；纯地格目标也只存在于现代直线冲锋。
            // 未开始且 count=-1/Thing 目标同时符合两种布局，不能猜下一步是步行还是起飞。
            return Action.KeepCurrent;
        }
        if (state.Count == 1 && state.Current == 0 && state.Next is >= -1 and <= 1)
        {
            return Action.KeepCurrent;
        }
        if (state.Count == -1 && state.Current is 0 or 1 && state.Next is >= -1 and <= 2 && state.HasThingTarget)
        {
            return Action.ResumeWalking;
        }
        throw new InvalidOperationException("Unproven lance continuation: saved launch marker, toil bounds and target do not identify a published layout.");
    }
}
