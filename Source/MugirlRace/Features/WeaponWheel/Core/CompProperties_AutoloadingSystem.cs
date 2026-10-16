namespace Mugirl.Features.WeaponWheel;

// 自驱装弹系统服装的组件配置，由 XML 指定。
public sealed class CompProperties_AutoloadingSystem : CompProperties
{
    // 穿戴时火力满载轮完最后一格后直接从第 1 格重新开始，不进入循环冷却。
    public bool skipCycleReload = true;

    public CompProperties_AutoloadingSystem()
    {
        compClass = typeof(CompAutoloadingSystem);
    }
}
