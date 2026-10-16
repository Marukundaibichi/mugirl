namespace Mugirl.Features.Restraints;

// 奴隶服装基类：保存锁数与锁定状态，负责穿戴 Hediff、原版穿戴锁和下一阶段自动替换。
// 状态修改集中在本类；其他功能通过只读属性查询，动态配置的锁数总是显式保存。
public class RestraintApparel : Apparel
{
    private int mugirlRestraintLockCount;
    private bool mugirlRestraintLocked = true;

    public int LockCount => mugirlRestraintLockCount;

    public bool IsLocked => mugirlRestraintLocked;

    public RestraintExtension? Restraint => def?.GetModExtension<RestraintExtension>();

    // 是否真正处于锁定。高级束具破解后不再锁定，由 AdvancedRestraintApparel 覆盖。
    public virtual bool IsEffectivelyLocked => mugirlRestraintLocked;

    private int InitialLockCount => Restraint?.requiredKeys ?? 0;

    public virtual void Crack()
    {
    }

    public override void PostMake()
    {
        base.PostMake();
        if (Restraint is { } configuration)
        {
            mugirlRestraintLockCount = configuration.requiredKeys;
        }
    }

    public override bool AllowVerbCast(Verb verb)
    {
        // Verb 关联的身体部位组被束缚时，禁止该动作。
        List<BodyPartGroupDef>? boundGroups = Restraint?.boundBodyPartGroups;
        if (boundGroups != null && verb.tool != null && boundGroups.Contains(verb.tool.linkedBodyPartsGroup))
        {
            return false;
        }

        // 基类遍历各 comp 的 CompAllowVerbCast；1.6 直接返回 true，磁力镣铐的激活束缚因此从未生效。
        return base.AllowVerbCast(verb);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref mugirlRestraintLockCount, "mugirlRestraintLockCount", 0, forceSave: true);
        Scribe_Values.Look(ref mugirlRestraintLocked, "mugirlRestraintLocked", true);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && mugirlRestraintLocked && mugirlRestraintLockCount <= 0)
        {
            mugirlRestraintLockCount = InitialLockCount;
        }
    }

    public override void Notify_Equipped(Pawn pawn)
    {
        base.Notify_Equipped(pawn);

        RestraintExtension? configuration = Restraint;
        Pawn_ApparelTracker? apparelTracker = pawn?.apparel;
        if (configuration == null || pawn == null || apparelTracker == null)
        {
            return;
        }

        // 穿戴时按配置给指定身体部位添加 Hediff，避免重复添加同部位效果。
        HediffDef? equippedHediff = configuration.equippedHediff;
        List<BodyPartRecord>? bodyParts = pawn.RaceProps?.body?.AllParts;
        if (equippedHediff != null && configuration.targetBodyParts != null && pawn.health?.hediffSet != null && bodyParts != null)
        {
            foreach (BodyPartDef partDef in configuration.targetBodyParts)
            {
                if (partDef == null)
                {
                    continue;
                }

                for (int i = 0; i < bodyParts.Count; i++)
                {
                    BodyPartRecord bodyPart = bodyParts[i];
                    if (bodyPart.def == partDef && !pawn.health.hediffSet.HasHediff(equippedHediff, bodyPart))
                    {
                        pawn.health.AddHediff(equippedHediff, bodyPart);
                    }
                }
            }
        }

        if (IsEffectivelyLocked)
        {
            apparelTracker.Lock(this);
        }
    }

    public override void Notify_Unequipped(Pawn pawn)
    {
        base.Notify_Unequipped(pawn);

        RestraintExtension? configuration = Restraint;
        Pawn_ApparelTracker? apparelTracker = pawn?.apparel;
        if (configuration == null || pawn == null || apparelTracker == null)
        {
            return;
        }

        // 卸下时只移除本服装配置过的 Hediff 和目标部位。
        if (configuration.equippedHediff != null && configuration.targetBodyParts != null && pawn.health?.hediffSet != null)
        {
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff hediff = hediffs[i];
                if (hediff.def == configuration.equippedHediff && ContainsDef(configuration.targetBodyParts, hediff.Part?.def))
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        // 存在下一阶段服装时，卸下当前件后自动换上第一件可穿目标。
        if (configuration.nextApparelDefs != null && !RestraintAutoStageContext.SuppressNextStage)
        {
            foreach (ThingDef nextDef in configuration.nextApparelDefs)
            {
                if (!CanAutoWearNextStage(pawn, nextDef))
                {
                    continue;
                }

                ThingDef? stuff = Stuff ?? GenStuff.DefaultStuffFor(nextDef);
                if (ThingMaker.MakeThing(nextDef, stuff) is Apparel nextApparel)
                {
                    apparelTracker.Wear(nextApparel, locked: true);
                    break;
                }
            }
        }
    }

    // 钥匙开锁一次。锁数归零后解除锁定并脱下；脱下失败时回滚到开锁前的锁数。
    internal KeyUnlockResult UseKey(Pawn wearer, IntVec3 dropCell)
    {
        int previousLockCount = mugirlRestraintLockCount;
        mugirlRestraintLockCount--;
        if (mugirlRestraintLockCount > 0)
        {
            return KeyUnlockResult.StillLocked;
        }

        return TryReleaseAndDrop(wearer, dropCell, previousLockCount) ? KeyUnlockResult.Released : KeyUnlockResult.DropFailed;
    }

    // 解除锁定并把束具脱到 dropCell。脱下期间抑制“下一阶段”自动穿戴；失败时恢复锁定状态
    // （锁数至少为 1）并重新加原版穿戴锁，与 1.6 三处开锁流程各自的回滚相同。
    internal bool TryReleaseAndDrop(Pawn wearer, IntVec3 dropCell, int rollbackLockCount)
    {
        Pawn_ApparelTracker? apparelTracker = wearer.apparel;
        if (apparelTracker == null)
        {
            return false;
        }

        bool previousLocked = mugirlRestraintLocked;
        mugirlRestraintLocked = false;
        mugirlRestraintLockCount = 0;
        apparelTracker.Unlock(this);

        bool dropped;
        using (RestraintAutoStageContext.Suppress())
        {
            dropped = apparelTracker.WornApparel.Contains(this)
                && apparelTracker.TryDrop(this, out Apparel _, dropCell, forbid: false);
        }

        if (dropped)
        {
            return true;
        }

        mugirlRestraintLocked = previousLocked;
        mugirlRestraintLockCount = rollbackLockCount > 0 ? rollbackLockCount : 1;
        apparelTracker.Lock(this);
        return false;
    }

    // 开发者解锁：清除锁定并解除原版穿戴锁，不脱下服装。
    internal void DevUnlock()
    {
        Pawn? wearer = Wearer;
        if (wearer == null)
        {
            return;
        }

        mugirlRestraintLocked = false;
        mugirlRestraintLockCount = 0;
        wearer.apparel?.Unlock(this);
        Messages.Message("Mugirl.Restraints.DevUnlock.Message".Translate(wearer.LabelShortCap, LabelCap), wearer, MessageTypeDefOf.PositiveEvent);
    }

    // 破解与配方产出共用：破解后的束具不再锁定（AdvancedRestraintApparel 使用）。
    private protected void ClearLockState()
    {
        mugirlRestraintLocked = false;
        mugirlRestraintLockCount = 0;
    }

    // 等同 List.Contains（Def 未重写 Equals(object)，按引用比较）；部位为空的全身 Hediff 只匹配列表中的 null 项。
    private static bool ContainsDef(List<BodyPartDef> defs, BodyPartDef? partDef)
    {
        for (int i = 0; i < defs.Count; i++)
        {
            if (defs[i] == partDef)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanAutoWearNextStage(Pawn pawn, ThingDef? nextDef)
    {
        return pawn.apparel != null
            && nextDef != null
            && nextDef.IsApparel
            && pawn.RaceProps?.body != null
            && ApparelUtility.HasPartsToWear(pawn, nextDef);
    }
}
