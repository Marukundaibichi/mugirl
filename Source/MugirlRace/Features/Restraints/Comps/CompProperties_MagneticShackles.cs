using Verse.Sound;

namespace Mugirl.Features.Restraints;

// 磁力镣铐配置：未破解时按随机周期自动束缚/解除，束缚期间添加 Hediff 并禁止相应部位的 Verb。
public class CompProperties_MagneticShackles : CompProperties
{
    public IntRange cycleInterval = new IntRange(15000, 55000);
    public IntRange bindDurationTicks = new IntRange(10000, 20000);

    public List<BindHediffEntry>? bindHediffs = new List<BindHediffEntry>();

    public int useCooldownTicks = 600;

    public string activateLabel = "Mugirl.Restraints.MagneticShackles.ActivateLabel";
    public string activateDesc = "Mugirl.Restraints.MagneticShackles.ActivateDesc";
    public string activateIconPath = "UI/Commands/DesirePower";

    public string deactivateLabel = "Mugirl.Restraints.MagneticShackles.DeactivateLabel";
    public string deactivateDesc = "Mugirl.Restraints.MagneticShackles.DeactivateDesc";
    public string deactivateIconPath = "UI/Commands/DesirePower";

    // XML 仍写这两个字段，但 1.6 起没有读取方；保留以免 XML 报未知字段。
    public string labelWhenActive = "Mugirl.Restraints.MagneticShackles.LabelActive";
    public string labelWhenInactive = "Mugirl.Restraints.MagneticShackles.LabelInactive";

    public string moteTextOn = "Mugirl.Restraints.MagneticShackles.MoteOn";
    public string moteTextOff = "Mugirl.Restraints.MagneticShackles.MoteOff";
    public string messageOn = "Mugirl.Restraints.MagneticShackles.MessageOn";
    public string messageOff = "Mugirl.Restraints.MagneticShackles.MessageOff";

    public List<BodyPartGroupDef>? boundBodyPartGroupDefs = new List<BodyPartGroupDef>();

    // XML 以 defName 字符串配置激活音效；加载时解析为 activateSound，1.6 每次激活都按名字查 DefDatabase。
    public string? activateSoundDefName;

    [Unsaved]
    private SoundDef? activateSound;

    [Unsaved]
    private Texture2D? activateIcon;

    [Unsaved]
    private Texture2D? deactivateIcon;

    public CompProperties_MagneticShackles()
    {
        compClass = typeof(CompMagneticShackles);
    }

    internal SoundDef? ActivateSound => activateSound;

    internal Texture2D ActivateIcon => RestraintCommandIcon.Resolve(ref activateIcon, activateIconPath);

    internal Texture2D DeactivateIcon => RestraintCommandIcon.Resolve(ref deactivateIcon, deactivateIconPath);

    public override void ResolveReferences(ThingDef parentDef)
    {
        base.ResolveReferences(parentDef);
        activateSound = string.IsNullOrEmpty(activateSoundDefName) ? null : DefDatabase<SoundDef>.GetNamedSilentFail(activateSoundDefName);
    }

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef))
        {
            yield return error;
        }

        if (!string.IsNullOrEmpty(activateSoundDefName) && activateSound == null)
        {
            yield return "activateSoundDefName '" + activateSoundDefName + "' does not match any SoundDef.";
        }
    }

    public class BindHediffEntry
    {
        public HediffDef? hediffDef;

        // 为空表示全身 Hediff。
        public List<BodyPartDef>? bodyPartDefs;
    }
}
