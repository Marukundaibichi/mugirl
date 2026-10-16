namespace Mugirl.Features.Restraints;

// 脑控头盔配置：周期性 Hediff 转换和主动按钮参数。
public class CompProperties_BrainwashHelmet : CompProperties
{
    public int useCooldownTicks = 480;

    public string activateLabel = "Mugirl.Restraints.BrainwashHelmet.ActivateLabel";
    public string activateDesc = "Mugirl.Restraints.BrainwashHelmet.ActivateDesc";
    public string activateIconPath = "UI/Commands/DesirePower";

    // 每轮转换随机抽取一个周期，避免所有装备同 tick 触发。
    public IntRange ticksToChange = new IntRange(20000, 60000);

    // 佩戴者拥有该 Hediff 时暂停周期转换并清零计时。
    public HediffDef? blockHediff;

    // 转换列表按顺序检查，命中后移除检查 Hediff 并添加目标 Hediff。
    public List<HediffConvertEntry>? convertList = new List<HediffConvertEntry>();

    // 没有匹配的转换时添加的 Hediff。
    public HediffDef? defaultHediff;

    [Unsaved]
    private Texture2D? activateIcon;

    public CompProperties_BrainwashHelmet()
    {
        compClass = typeof(CompBrainwashHelmet);
    }

    internal Texture2D ActivateIcon => RestraintCommandIcon.Resolve(ref activateIcon, activateIconPath);

    public class HediffConvertEntry
    {
        public HediffDef? hediffToCheck;
        public HediffDef? hediffToAdd;
    }
}
