namespace Mugirl.Features.Restraints;

// 抑制“卸下后自动换上下一阶段束具”的嵌套计数。替换服装、开锁脱下等内部卸装不应凭空生成下一阶段束具。
// StaticCacheLifecycle: 进程级计数，只在主线程的穿戴/开锁调用期间非零；每次 Begin 都由 finally、using 作用域
// 或 Harmony Finalizer 配对 End，不跨 tick、不跨存档保留，因此无需在新游戏或读档时清理。
internal static class RestraintAutoStageContext
{
    private static int suppressNextStageDepth;

    internal static bool SuppressNextStage => suppressNextStageDepth > 0;

    internal static void BeginSuppressNextStage()
    {
        suppressNextStageDepth++;
    }

    internal static void EndSuppressNextStage()
    {
        suppressNextStageDepth = Math.Max(0, suppressNextStageDepth - 1);
    }

    // 普通代码用 using 作用域；Harmony 的 Prefix/Finalizer 跨方法配对，仍直接调用 Begin/End。
    internal static Scope Suppress()
    {
        BeginSuppressNextStage();
        return new Scope(active: true);
    }

    internal readonly ref struct Scope
    {
        private readonly bool active;

        internal Scope(bool active)
        {
            this.active = active;
        }

        public void Dispose()
        {
            if (active)
            {
                EndSuppressNextStage();
            }
        }
    }
}
