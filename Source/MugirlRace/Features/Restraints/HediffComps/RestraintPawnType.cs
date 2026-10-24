namespace Mugirl.Features.Restraints;

// HediffCompProperties_AddTrait.PawnTypeTraitEntry.pawnType 的取值。原版 XML 加载器按成员名解析（区分大小写）；
// 未填写时为 None，不匹配任何 Pawn。
public enum RestraintPawnType
{
    None,
    Colonist,
    Slave,
    Prisoner,
    Free,
    Hostile,
}
