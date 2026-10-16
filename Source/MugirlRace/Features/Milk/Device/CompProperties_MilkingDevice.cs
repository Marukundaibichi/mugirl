namespace Mugirl.Features.Milk;

// 榨乳器组件配置：XML 可调参数，以及 Def 加载后一次性解析出的 ThingDef 与图标；运行状态放在 CompMilkingDevice 中。
public class CompProperties_MilkingDevice : CompProperties
{
    public int maxCharges = 3;

    public string releaseLabel = "Mugirl.MilkingDevice.ReleaseLabel";

    public string releaseDesc = "Mugirl.MilkingDevice.ReleaseDesc";

    public string releaseIconPath = "UI/Mugirl_Charge";

    public string inspectLabel = "Mugirl.MilkingDevice.InspectLabel";

    public string noChargeText = "Mugirl.MilkingDevice.NoChargeText";

    public string storedMessageKey = "Mugirl.MilkingDevice.StoredMessage";

    public string releaseMessageKey = "Mugirl.MilkingDevice.ReleaseMessage";

    public string storedMoteText = "Mugirl.MilkingDevice.StoredMoteText";

    public string autoReleaseEnabledLabel = "Mugirl.MilkingDevice.AutoReleaseEnabled";

    public string autoReleaseDisabledLabel = "Mugirl.MilkingDevice.AutoReleaseDisabled";

    public string autoReleaseToggleDesc = "Mugirl.MilkingDevice.AutoReleaseDesc";

    public string powerReleaseEnabledLabel = "Mugirl.MilkingDevice.PowerReleaseEnabled";

    public string powerReleaseDisabledLabel = "Mugirl.MilkingDevice.PowerReleaseDisabled";

    public string powerReleaseToggleDesc = "";

    public string releaseThingDefName = "Mugirl_Milk";

    public bool spawnFilthOnRelease;

    public string filthDefName = "Mugirl_Filth_MilkStain";

    public IntRange filthCountRange = new(1, 2);

    public bool spawnFilthInFacingDirection;

    // 强力释放专用污物：1.7/Defs 的两件榨乳器都把 powerFilthDefName 写为空，表示强力释放沿用普通污物。
    // 字段保留供 XML 加载（删除会让现有 XML 报错），运行时不读取。
    public string powerFilthDefName = "Mugirl_Filth_ShockFluid";

    public IntRange powerFilthCountRange = new(1, 1);

    public List<SoundDef>? releaseSounds;

    public List<SoundDef>? powerReleaseSounds;

    public int powerReleaseStunTicks = 180;

    private ThingDef? releaseThingDef;

    private ThingDef? filthThingDef;

    private Texture2D? releaseIcon;

    public CompProperties_MilkingDevice()
    {
        compClass = typeof(CompMilkingDevice);
    }

    // 释放产物；releaseThingDefName 为空或找不到时为 null。
    internal ThingDef? ReleaseThingDef => releaseThingDef;

    // 释放污物；filthDefName 为空或找不到时为 null。
    internal ThingDef? FilthThingDef => filthThingDef;

    // 释放按钮图标。Gizmo 每帧重建，图标在主线程首次使用时加载一次；路径为空或缺图时用原版图标。
    internal Texture2D ReleaseIcon => releaseIcon ??= LoadReleaseIcon();

    public override void ResolveReferences(ThingDef parentDef)
    {
        base.ResolveReferences(parentDef);
        releaseThingDef = ResolveThingDef(releaseThingDefName);
        filthThingDef = ResolveThingDef(filthDefName);
    }

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef))
        {
            yield return error;
        }

        if (!releaseThingDefName.NullOrEmpty() && releaseThingDef == null)
        {
            yield return "releaseThingDefName '" + releaseThingDefName + "' does not match any ThingDef.";
        }

        if (spawnFilthOnRelease && !filthDefName.NullOrEmpty() && filthThingDef == null)
        {
            yield return "filthDefName '" + filthDefName + "' does not match any ThingDef.";
        }
    }

    private static ThingDef? ResolveThingDef(string? defName)
    {
        return defName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName);
    }

    private Texture2D LoadReleaseIcon()
    {
        Texture2D? icon = releaseIconPath.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(releaseIconPath, false);
        return icon ?? TexCommand.DesirePower;
    }
}
