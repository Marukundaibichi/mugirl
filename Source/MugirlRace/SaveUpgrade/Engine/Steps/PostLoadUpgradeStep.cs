namespace Mugirl.SaveUpgrade;

/// <summary>读档后的语义步骤，在阶段 A、B 或 C 对游戏对象运行（见 <see cref="UpgradePhase"/>）；只在旧档升级未失败时运行。</summary>
internal abstract class PostLoadUpgradeStep : UpgradeStep
{
    protected PostLoadUpgradeStep(string id, UpgradePhase phase, string reason, params string[] consumes)
        : base(id, phase, reason, consumes)
    {
        if (phase == UpgradePhase.Dom)
        {
            throw new ArgumentException("A post-load step must run in phase A, B or C: " + id, nameof(phase));
        }
    }

    /// <summary>执行本步骤，返回实际改动的对象数（写入报告）。</summary>
    internal abstract int Run(PostLoadContext context);
}
