using Mugirl.Features.Restraints;

namespace Mugirl;

// 奴隶服装 ThingDef 扩展，保存锁定、Hediff 和身体部位限制等 XML 配置。
// 字段名是 XML 契约：soul_type、gives_*、blocks_* 等目前没有 C# 读取方，但 1.7/Defs 仍在写，删除会产生 XML 报错。
public class SlaveApparelDef : ThingDef
{
    public Type? soul_type;
    public HediffDef? equipped_hediff;
    public bool gives_bound_moodlet;
    public bool gives_gagged_moodlet;

    // 身体功能开关；blocks_hands 由钥匙菜单读取，其余只作 XML 元数据。
    public bool blocks_hands;
    public bool blocks_oral;
    public bool blocks_penis;
    public bool blocks_vagina;
    public bool blocks_anus;
    public bool blocks_breasts;

    public int unlockTick = 100;
    public int needkeynumber = 2;
    public ThingDef? keytype;

    public List<ThingDef>? NextSlaveApparelDefs;
    public List<BodyPartDef>? HediffTargetBodyPartDefs;
    public List<BodyPartGroupDef>? BoundBodyPartGroupDefs;

    [Unsaved]
    private RestraintExtension? resolvedRestraint;

    public override void ResolveReferences()
    {
        base.ResolveReferences();
        RestraintExtension? extension = GetModExtension<RestraintExtension>();
        if (extension is null)
        {
            modExtensions ??= [];
            extension = resolvedRestraint = new RestraintExtension();
            modExtensions.Add(extension);
        }

        // 重复解析（包括 Def 重载）刷新本别名生成的配置；显式提供的新 extension 保持其自身配置。
        if (ReferenceEquals(extension, resolvedRestraint))
        {
            extension.equippedHediff = equipped_hediff;
            extension.blocksHands = blocks_hands;
            extension.unlockTicks = unlockTick;
            extension.requiredKeys = needkeynumber;
            extension.key = keytype;
            extension.nextApparelDefs = NextSlaveApparelDefs;
            extension.targetBodyParts = HediffTargetBodyPartDefs;
            extension.boundBodyPartGroups = BoundBodyPartGroupDefs;
        }
    }
}
