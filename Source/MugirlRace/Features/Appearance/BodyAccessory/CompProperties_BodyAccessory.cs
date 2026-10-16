namespace Mugirl.Features.Appearance;

/// <summary>
/// 身体附件的临时状态配置。基础变体仍由 HAR 保存；Lovin 印记只在渲染时覆盖，
/// 到期后无需改写梳妆台选择即可恢复原外观。
/// </summary>
public sealed class CompProperties_BodyAccessory : CompProperties
{
    public float lovinBiteChance = 0.25f;
    public float lovinHandprintChance = 0.25f;
    public IntRange lovinMarkDurationTicks = new(60000, 180000);

    public CompProperties_BodyAccessory()
    {
        compClass = typeof(CompBodyAccessory);
    }
}
