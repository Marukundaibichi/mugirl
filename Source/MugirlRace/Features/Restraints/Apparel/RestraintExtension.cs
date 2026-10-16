namespace Mugirl.Features.Restraints;

/// <summary>束具的功能配置；与 ThingDef 继承层级分离，公开 XML 别名在引用解析时提供同一配置。</summary>
public sealed class RestraintExtension : DefModExtension
{
    public HediffDef? equippedHediff;
    public bool blocksHands;
    public int unlockTicks = 100;
    public int requiredKeys = 2;
    public ThingDef? key;
    public List<ThingDef>? nextApparelDefs;
    public List<BodyPartDef>? targetBodyParts;
    public List<BodyPartGroupDef>? boundBodyPartGroups;
}
