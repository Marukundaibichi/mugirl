using System.Globalization;
using Verse.AI;

namespace Mugirl.Features.Restraints;

// 强行开锁：按束具所用钥匙划分档位，比较施术者力量与档位要求。
internal static class ForceUnlockRestraintUtility
{
    internal const int WorkTicks = 3600;
    internal const int WoundCheckIntervalTicks = 600;
    internal const float WoundChancePerInterval = 0.2f;

    // 档位由束具的 keytype 决定（中世纪钥匙 = 粗糙，工业钥匙 = 精细）；1.6 按 defName 字符串比较，现改为 DefOf 引用比较。
    internal static ForceUnlockRestraintTier GetTier(Apparel? apparel)
    {
        ThingDef? keyType = (apparel as RestraintApparel)?.Restraint?.key;
        if (keyType == null)
        {
            return ForceUnlockRestraintTier.None;
        }

        if (keyType == RestraintsDefOf.Mugirl_SlaveApparelKey_Medieval)
        {
            return ForceUnlockRestraintTier.Rough;
        }

        if (keyType == RestraintsDefOf.Mugirl_SlaveApparelKey_Industrial)
        {
            return ForceUnlockRestraintTier.Fine;
        }

        return ForceUnlockRestraintTier.None;
    }

    internal static bool IsForceUnlockable(Apparel? apparel)
    {
        return GetTier(apparel) != ForceUnlockRestraintTier.None;
    }

    internal static bool IsLockedForceUnlockable(Pawn? wearer, Apparel? apparel)
    {
        return wearer?.apparel != null
            && apparel is RestraintApparel { IsEffectivelyLocked: true }
            && wearer.apparel.WornApparel.Contains(apparel)
            && IsForceUnlockable(apparel);
    }

    internal static bool HasAnyLockedForceUnlockable(Pawn? wearer)
    {
        List<Apparel>? wornApparel = wearer?.apparel?.WornApparel;
        if (wornApparel == null)
        {
            return false;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            if (IsLockedForceUnlockable(wearer, wornApparel[i]))
            {
                return true;
            }
        }

        return false;
    }

    internal static float GetStrengthLevel(Pawn? pawn)
    {
        if (pawn == null)
        {
            return 0f;
        }

        return ForceUnlockStrength.Calculate(GetBodySize(pawn), GetManipulation(pawn), GetMeleeLevel(pawn));
    }

    internal static float GetRequiredStrength(Apparel? apparel)
    {
        return ForceUnlockStrength.Required(GetTier(apparel));
    }

    internal static bool HasRequiredStrength(Pawn? pawn, Apparel? apparel)
    {
        return GetStrengthLevel(pawn) >= GetRequiredStrength(apparel);
    }

    internal static string RequirementSummary(Apparel? apparel)
    {
        return GetTier(apparel) switch
        {
            ForceUnlockRestraintTier.Rough => "Mugirl.Restraints.ForceUnlock.RequirementRough".Translate(),
            ForceUnlockRestraintTier.Fine => "Mugirl.Restraints.ForceUnlock.RequirementFine".Translate(),
            _ => "Mugirl.Restraints.ForceUnlock.RequirementUnsupported".Translate(),
        };
    }

    // 1.6 依赖原版把当前文化设为英语；固定为不变文化，保证小数点在任何线程上一致。
    internal static string StrengthText(float strength)
    {
        return strength.ToString("0.0", CultureInfo.InvariantCulture);
    }

    internal static bool CanReserveAndReachTarget(Pawn? actor, Pawn? target)
    {
        if (actor == null || target == null)
        {
            return false;
        }

        return actor == target || actor.CanReserveAndReach(target, PathEndMode.Touch, Danger.Some);
    }

    private static float GetBodySize(Pawn pawn)
    {
        float bodySize = pawn.BodySize;
        return bodySize < 0f ? 0f : bodySize;
    }

    private static float GetManipulation(Pawn pawn)
    {
        float manipulation = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 0f;
        return manipulation < 0f ? 0f : manipulation;
    }

    private static int GetMeleeLevel(Pawn pawn)
    {
        return pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
    }
}
