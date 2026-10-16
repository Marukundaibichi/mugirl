using Mugirl.Features.Restraints;

namespace Mugirl.Features.Restraints;

// 佩戴已破解的洗脑头盔达到指定时长后，按 Pawn 身份（殖民者/奴隶/囚犯…）给予一次特质。
public class HediffCompProperties_AddTrait : HediffCompProperties
{
    public int triggerTicks = 600;

    // 没有匹配的身份条目时使用的默认特质。
    public TraitDef? traitDef;

    public List<PawnTypeTraitEntry>? pawnTypeTraitEntries = new List<PawnTypeTraitEntry>();

    public HediffCompProperties_AddTrait()
    {
        compClass = typeof(HediffComp_AddTrait);
    }

    public class PawnTypeTraitEntry
    {
        // colonist / slave / prisoner / free / hostile，见 RestraintPawnTypeParser。
        public string? pawnType;
        public TraitDef? trait;
    }
}
