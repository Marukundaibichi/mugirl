namespace Mugirl.Features.Mounting;

// 骑乘状态变化时同步炮塔：上骑登记 verb，下骑或卸下武器时取消施放并把施放者还原为骑手。
public static partial class MountedCombatController
{
    public static void NotifyMounted(CompMount comp)
    {
        if (GetPrimaryRangedVerb(comp) is { } verb)
        {
            EnsureVerbCaster(comp, verb);
        }
    }

    public static void NotifyDismounting(CompMount comp, Pawn? rider)
    {
        CancelMountedCast(comp, GetPrimaryRangedVerb(rider));
        RestoreVerbCaster(rider);
        comp.Turret.burstCooldownTicksLeft = 0;
        ClearAim(comp);
        comp.Turret.lastAttackedTarget = LocalTargetInfo.Invalid;
    }

    public static void NotifyEquipmentRemoved(ThingWithComps? equipment)
    {
        // 每个 Pawn 卸下装备都会经过这里；没有登记的骑乘 verb 时（施放覆盖也必然为空）直接返回。
        if (OriginalCasters.Count == 0)
        {
            return;
        }

        List<Verb>? verbs = equipment?.GetComp<CompEquippable>()?.AllVerbs;
        if (verbs is null)
        {
            return;
        }

        for (int i = 0; i < verbs.Count; i++)
        {
            ForgetVerb(verbs[i]);
        }
    }
}
