using Verse.AI;

namespace Mugirl.Features.Restraints;

// 钥匙的右键菜单：可对自己、自由殖民者、殖民地囚犯和尸体中的 Pawn 开锁（目标范围沿用 1.6）。
// 类名是 XML compClass 与 DefInjected 句柄，不可改。
public class CompUsable_RestraintKey : CompUsable
{
    protected string MakeLabel(Pawn pawn, Pawn? other)
    {
        string targetLabel = other == null ? "Mugirl.SelfLabel".Translate().ToString() : DisplayName(other);
        return "Mugirl.FloatMenu.ActionOnTarget".Translate(FloatMenuOptionLabel(pawn), targetLabel).ToString();
    }

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (!pawn.CanReserve(parent))
        {
            yield return new FloatMenuOption(
                "Mugirl.FloatMenu.OptionWithReason".Translate(FloatMenuOptionLabel(pawn), "Mugirl.Reserved".Translate()),
                null,
                MenuOptionPriority.DisabledOption);
            yield break;
        }

        if (!pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
        {
            yield break;
        }

        if (pawn.IsWearingRestraint())
        {
            if (!pawn.IsHandsBlocked())
            {
                yield return KeyUnlockFloatMenu.MakeOption(this, MakeLabel(pawn, pawn), pawn, pawn, null);
            }
            else
            {
                yield return new FloatMenuOption(
                    "Mugirl.FloatMenu.OptionWithReason".Translate(MakeLabel(pawn, pawn), "Mugirl.HandsBlocked".Translate()),
                    null,
                    MenuOptionPriority.DisabledOption);
            }
        }

        Map? map = pawn.Map;
        if (!GameUtility.IsCurrentMap(map))
        {
            yield break;
        }

        foreach (Pawn other in map.mapPawns.FreeColonists)
        {
            if (other != pawn && other.IsWearingRestraint())
            {
                yield return KeyUnlockFloatMenu.MakeOption(this, MakeLabel(pawn, other), pawn, other, null);
            }
        }

        foreach (Pawn prisoner in map.mapPawns.PrisonersOfColony)
        {
            if (prisoner.IsWearingRestraint())
            {
                yield return KeyUnlockFloatMenu.MakeOption(this, MakeLabel(pawn, prisoner), pawn, prisoner, WorkTypeDefOf.Warden);
            }
        }

        List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
        for (int i = 0; i < corpses.Count; i++)
        {
            if (corpses[i] is Corpse corpse && corpse.InnerPawn.IsWearingRestraint())
            {
                yield return KeyUnlockFloatMenu.MakeOption(this, MakeLabel(pawn, corpse.InnerPawn), pawn, corpse, null);
            }
        }
    }
    // 显示名的选择和空值退化原样保留，属于钥匙菜单自身的格式规则。
    private static string DisplayName(Pawn? pawn)
    {
        if (pawn == null)
        {
            return "null";
        }

        return pawn.Name?.ToStringShort ?? pawn.Label ?? "noname";
    }
}
