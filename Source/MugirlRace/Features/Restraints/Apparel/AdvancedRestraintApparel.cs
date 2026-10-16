namespace Mugirl.Features.Restraints;

// 高级奴隶服装额外保存破解状态；破解后不再锁定，自动效果改为手动控制。
public class AdvancedRestraintApparel : RestraintApparel
{
    private bool mugirlRestraintCracked;

    public bool IsCracked() => mugirlRestraintCracked;

    public override bool IsEffectivelyLocked => IsLocked && !mugirlRestraintCracked;

    public override void Crack()
    {
        // 1.6 只在已穿戴时清除锁定；解码器只能破解地面上的束具，于是 IsLocked 保留为 true，
        // 穿上后既无法脱下也会阻挡冲突服装。这里无条件清除，保证“破解即解锁”。
        mugirlRestraintCracked = true;
        ClearLockState();

        Pawn? wearer = Wearer;
        if (wearer != null)
        {
            wearer.apparel?.Unlock(this);
            Messages.Message("Mugirl.SlaveApparelCracked".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
            SelectionUtility.SelectInPlaying(wearer);
        }
    }

    public override void Notify_RecipeProduced(Pawn pawn)
    {
        base.Notify_RecipeProduced(pawn);

        // 玩家制造的电击项圈直接以已破解、未锁定状态产出；其他来源仍保留默认状态。
        if (def == RestraintsDefOf.Mugirl_ShockCollar && pawn != null && PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
        {
            mugirlRestraintCracked = true;
            ClearLockState();
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref mugirlRestraintCracked, "mugirlRestraintCracked", false);

        // 破解状态与锁状态必须一致；这是当前束具的不变量。
        if (Scribe.mode == LoadSaveMode.PostLoadInit && mugirlRestraintCracked && IsLocked)
        {
            ClearLockState();
        }
    }

    public override string LabelNoCount
    {
        get
        {
            string label = base.LabelNoCount;
            string status = mugirlRestraintCracked ? "Mugirl.Cracked".Translate().ToString() : "Mugirl.Uncracked".Translate().ToString();
            return "Mugirl.SlaveApparel.StatusLabel".Translate(label, status).ToString();
        }
    }

    public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
    {
        foreach (StatDrawEntry baseEntry in base.SpecialDisplayStats())
        {
            yield return baseEntry;
        }

        yield return new StatDrawEntry(
            StatCategoryDefOf.Apparel,
            "Mugirl.CrackStatus".Translate(),
            mugirlRestraintCracked ? "Mugirl.Cracked".Translate() : "Mugirl.Uncracked".Translate(),
            "Mugirl.CrackStatusDesc".Translate(),
            999);
    }

    public override void Notify_Equipped(Pawn pawn)
    {
        base.Notify_Equipped(pawn);
        Pawn_ApparelTracker? apparelTracker = pawn?.apparel;
        if (apparelTracker == null)
        {
            return;
        }

        // 穿上高级束具时按每件奴隶服装的实际锁定状态同步原版穿戴锁：已破解的高级束具解除，其余按 IsLocked。
        List<Apparel> wornApparel = apparelTracker.WornApparel;
        for (int i = 0; i < wornApparel.Count; i++)
        {
            if (wornApparel[i] is not RestraintApparel slaveApparel)
            {
                continue;
            }

            if (slaveApparel.IsEffectivelyLocked)
            {
                apparelTracker.Lock(slaveApparel);
            }
            else
            {
                apparelTracker.Unlock(slaveApparel);
            }
        }
    }
}
