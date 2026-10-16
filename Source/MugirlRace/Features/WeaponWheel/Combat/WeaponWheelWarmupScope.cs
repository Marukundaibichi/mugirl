namespace Mugirl.Features.WeaponWheel;

// 轮射换到下一把枪后同步调用 TryStartCastOn 时，让该 Verb 的 WarmupTime 读作 0（Verb.WarmupTime 补丁查询）。
// 作用域不嵌套不同 Verb（换枪只在单个 Pawn 的 tick 内同步发生）；嵌套时内层结束后恢复外层。
internal readonly struct WeaponWheelWarmupScope : IDisposable
{
    // StaticCacheLifecycle: 只在同步调用期间持有一个 Verb，Dispose 后立即恢复，不跨 tick、存档或游戏。
    // 单个引用字段让其他线程读取 WarmupTime 时也不会碰到集合的并发修改。
    private static Verb? skippingVerb;

    private readonly Verb? previous;

    internal WeaponWheelWarmupScope(Verb? verb)
    {
        previous = skippingVerb;
        if (verb != null)
        {
            skippingVerb = verb;
        }
    }

    internal static bool IsSkipping(Verb? verb)
    {
        return verb != null && ReferenceEquals(verb, skippingVerb);
    }

    public void Dispose()
    {
        skippingVerb = previous;
    }
}
