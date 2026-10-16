namespace Mugirl.Features.Restraints;

// 电击项圈配置：控制自动电击、手动按钮和命中的 Hediff 列表。
public class CompProperties_ShockCollar : CompProperties
{
    public List<HediffDef>? hediffDefs = new List<HediffDef>();
    public List<HediffDef>? powerhediffDefs = new List<HediffDef>();
    public List<HediffDef>? nonMugirlPowerHediffDefs = new List<HediffDef>();

    // 自动电击参数：间隔、总触发概率和强力电击概率（百分比）。
    public int ticks = 60;
    public int rand = 50;
    public int powerShockChance = 30;

    // 手动按钮参数来自 XML，可直接写翻译键或图标路径。
    public int useCooldownTicks = 480;
    public string powerLabel = "Mugirl.Restraints.ShockCollar.PowerLabel";
    public string powerDesc = "Mugirl.Restraints.ShockCollar.PowerDesc";
    public string powerIconPath = "UI/Commands/DesirePower";
    public string commonLabel = "Mugirl.Restraints.ShockCollar.CommonLabel";
    public string commonDesc = "Mugirl.Restraints.ShockCollar.CommonDesc";
    public string commonIconPath = "UI/Commands/DesirePower";

    // 是否只对玩家控制的单位生效（殖民者、囚犯、奴隶），避免访客模组冲突
    public bool playerColonistOnly = true;

    [Unsaved]
    private Texture2D? powerIcon;

    [Unsaved]
    private Texture2D? commonIcon;

    public CompProperties_ShockCollar()
    {
        compClass = typeof(CompShockCollar);
    }

    internal Texture2D PowerIcon => RestraintCommandIcon.Resolve(ref powerIcon, powerIconPath);

    internal Texture2D CommonIcon => RestraintCommandIcon.Resolve(ref commonIcon, commonIconPath);
}
