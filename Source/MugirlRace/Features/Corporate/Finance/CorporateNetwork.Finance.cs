using System.Diagnostics.CodeAnalysis;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// 贷款、抵押托管、计息、违约与讨债袭击。贷款账户负责金额计算；网络编排抵押交付、外交投影和通知。
public partial class CorporateNetwork
{
    private bool loanRead;

    public static float LoanDailyRate => CorporateDefOf.Mugirl_CorporateFinanceConfig.dailyRate;
    public static float LoanOverdueDailyRate => CorporateDefOf.Mugirl_CorporateFinanceConfig.overdueDailyRate;
    public static float LoanCollateralRatio => CorporateDefOf.Mugirl_CorporateFinanceConfig.collateralRatio;
    public static int LoanTermDays => CorporateDefOf.Mugirl_CorporateFinanceConfig.termDays;
    public static int LoanMinimum => CorporateDefOf.Mugirl_CorporateFinanceConfig.minimumPrincipal;
    public static int LoanMaximum => CorporateDefOf.Mugirl_CorporateFinanceConfig.maximumPrincipal;

    // StaticCacheLifecycle: 每局状态的只读投影——当前存档已违约贷款的债主派系，没有违约时为 null。
    // 供每 tick 调用成千上万次的敌对判定补丁使用，避免逐次扫描 GameComponent。CorporateNetwork 构造函数清空它
    // （新档、读档都会构造新组件），读档的 ResolvingCrossRefs/PostLoadInit 与每次贷款状态变化后重算。
    // 回到主菜单后可能暂留上一局的派系引用：它与之后任何游戏的派系引用都不相等，不会误判，下一局构造组件时清除。
    private static Faction? defaultedLoanFaction;

    /// <summary>已违约贷款的债主派系；没有违约时为 null。只读投影，见 <see cref="defaultedLoanFaction"/>。</summary>
    internal static Faction? DefaultedLoanFaction => defaultedLoanFaction;

    public float LoanPrincipal => loan.principal;
    public float LoanInterest => loan.interest;
    public int LoanBalance => loan.Balance;
    public int LoanDueTick => loan.dueTick;
    public bool HasLoan => loan.HasBalance;
    public bool LoanDefaulted => loan.defaulted && HasLoan;
    public int LoanNextRaidTick => loan.nextRaidTick;
    public int LoanCollateralValue => loan.collateralValue;
    public IReadOnlyList<Thing> LoanCollateral => loan.collateral;
    public int FinancePendingSilver => loan.pendingSilver;
    public float CurrentLoanDailyRate => HasLoan ? loan.dailyRate : LoanDailyRate;
    public float CurrentLoanOverdueRate => HasLoan ? loan.overdueDailyRate : LoanOverdueDailyRate;
    public int LoanLiquidationValue => CorporateMoney.FloorShare(loan.collateralValue, loan.liquidationRatio);
    public int LoanProjectedBalance => loan.ProjectedBalance;

    public bool HasCollateral
    {
        get
        {
            foreach (Thing thing in loan.collateral)
            {
                if (thing != null && !thing.Destroyed)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void FinanceExposeData()
    {
        CorporateStateScribe.Look(loan, "loan", ref loanRead);
        if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            RefreshDebtOverlay();
        }
    }

    private void FinanceTick()
    {
        if (!HasLoan)
        {
            return;
        }

        UpdateLoanAccrual();
        int now = Now;
        if (!loan.threeDayReminder && now >= loan.dueTick - (3 * DayTicks) && now < loan.dueTick)
        {
            loan.threeDayReminder = true;
            FinanceLetter("Mugirl.CorporateFinance.ReminderTitle", "Mugirl.CorporateFinance.Reminder", 3);
        }

        if (!loan.oneDayReminder && now >= loan.dueTick - DayTicks && now < loan.dueTick)
        {
            loan.oneDayReminder = true;
            FinanceLetter("Mugirl.CorporateFinance.ReminderTitle", "Mugirl.CorporateFinance.Reminder", 1);
        }

        if (LoanDefaulted && now >= loan.nextRaidTick)
        {
            TryDebtCollectionRaid();
        }
    }

    public void UpdateLoanAccrual()
    {
        if (!HasLoan)
        {
            return;
        }

        int now = Now;
        loan.AccrueInterest(now);

        if (now >= loan.dueTick && !loan.defaulted)
        {
            FactionRelationKind previous = loan.creditor?.RelationKindWith(PlayerFactionUtility.PlayerFaction) ?? FactionRelationKind.Neutral;
            loan.defaulted = true;
            // 先更新投影，原版随后读取的关系即为债务敌对。
            RefreshDebtOverlay();
            loan.nextRaidTick = now + (CorporateDefOf.Mugirl_CorporateFinanceConfig.firstRaidDelayDays * DayTicks);
            NotifyDebtRelationChanged(previous);
            Record("Mugirl.CorporateFinance.DefaultTitle", "", LoanBalance);
            FinanceLetter("Mugirl.CorporateFinance.DefaultTitle", "Mugirl.CorporateFinance.DefaultLetter", LoanBalance);
        }
    }

    public static bool IsEligibleCollateral([NotNullWhen(true)] Thing? thing)
    {
        return thing != null && !thing.Destroyed && thing.def.category == ThingCategory.Item
            && thing is not Pawn && thing is not Corpse && thing is not IThingHolder
            && thing.def != ThingDefOf.Silver && thing.def.tradeability != Tradeability.None
            && thing.TryGetComp<CompRottable>() == null && thing.TryGetComp<CompHatcher>() == null
            && !HoldsThingsInComps(thing)
            && thing.questTags.NullOrEmpty() && thing.MarketValue > 0f;
    }

    // 带内部容器的组件（例如装弹或封装物）里有东西时不能作抵押，否则清算会连带销毁其中物品。
    private static bool HoldsThingsInComps(Thing thing)
    {
        if (thing is not ThingWithComps withComps)
        {
            return false;
        }

        List<ThingComp> comps = withComps.AllComps;
        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i] is IThingHolder holder && holder.GetDirectlyHeldThings()?.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryTakeLoan(CorporateTradeContext context, int amount, IDictionary<Thing, int> collateral, [NotNullWhen(false)] out string? reason)
    {
        UpdateLoanAccrual();
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        if (HasLoan || HasCollateral || loan.pendingSilver > 0)
        {
            return FinanceReject("Mugirl.CorporateFinance.Existing", out reason);
        }

        if (amount < LoanMinimum || amount > LoanMaximum || collateral == null || collateral.Count == 0)
        {
            return FinanceReject("Mugirl.CorporateFinance.InvalidAmount", out reason);
        }

        HashSet<Thing> available = new HashSet<Thing>(context.AvailableThings);
        float value = 0f;
        foreach (KeyValuePair<Thing, int> entry in collateral)
        {
            if (!available.Contains(entry.Key) || !IsEligibleCollateral(entry.Key)
                || entry.Value <= 0 || entry.Value > entry.Key.stackCount)
            {
                return FinanceReject("Mugirl.CorporateFinance.CollateralChanged", out reason);
            }

            value += entry.Key.MarketValue * entry.Value;
        }

        if (amount > UnityMathf.FloorToInt(value * LoanCollateralRatio))
        {
            return FinanceReject("Mugirl.CorporateFinance.InsufficientCollateral", out reason);
        }

        // 离开原持有者后，每件资产都以持久化的 Vault 为持有者。任一步失败都把已收押的抵押品退回，不留下无贷款的抵押。
        var taken = new List<Thing>(collateral.Count);
        foreach (KeyValuePair<Thing, int> entry in collateral)
        {
            if (!context.TryTake(entry.Key, entry.Value, out Thing? part))
            {
                ReturnCollateral(context, taken);
                return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
            }

            if (!Vault.TryAdd(part, false))
            {
                context.Deliver(part);
                ReturnCollateral(context, taken);
                return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
            }

            loan.collateral.Add(part);
            taken.Add(part);
        }

        if (!context.DeliverSilver(amount))
        {
            ReturnCollateral(context, taken);
            return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
        }

        int now = Now;
        loan.principal = loan.originalPrincipal = amount;
        loan.interest = loan.overdueInterestTotal = 0f;
        loan.lastAccrualTick = now;
        loan.dueTick = now + (LoanTermDays * DayTicks);
        loan.nextRaidTick = 0;
        loan.defaulted = loan.threeDayReminder = loan.oneDayReminder = loan.collateralLiquidated = false;
        loan.collateralValue = UnityMathf.FloorToInt(value);
        loan.creditor = CorporateFaction;
        loan.dailyRate = LoanDailyRate;
        loan.overdueDailyRate = LoanOverdueDailyRate;
        loan.liquidationRatio = CorporateDefOf.Mugirl_CorporateFinanceConfig.liquidationRatio;
        RefreshDebtOverlay();
        Record("Mugirl.CorporateFinance.LoanSigned", "", amount);
        reason = null;
        return true;
    }

    // 签约失败时退回本次已收押的抵押品；暂时无法交付的留在 Vault 的抵押清单中，没有贷款时可在财务页领回。
    private void ReturnCollateral(CorporateTradeContext context, List<Thing> taken)
    {
        foreach (Thing thing in taken)
        {
            if (thing.Destroyed || context.Deliver(thing))
            {
                loan.collateral.Remove(thing);
            }
        }
    }

    public bool TryRepayLoan(CorporateTradeContext context, int amount, [NotNullWhen(false)] out string? reason)
    {
        UpdateLoanAccrual();
        if (!FinanceContextValid(context, out reason))
        {
            return false;
        }

        if (!HasLoan || amount <= 0)
        {
            return FinanceReject("Mugirl.CorporateFinance.NoDebt", out reason);
        }

        amount = Math.Min(amount, LoanBalance);
        if (!context.TrySpendSilver(amount))
        {
            return FinanceReject("Mugirl.CorporateFinance.NoSilver", out reason);
        }

        ApplyLoanPayment(amount);
        Record("Mugirl.CorporateFinance.Payment", "", -amount);
        return true;
    }

    public bool TryLiquidateCollateral(CorporateTradeContext context, [NotNullWhen(false)] out string? reason)
    {
        UpdateLoanAccrual();
        if (!FinanceContextValid(context, out reason))
        {
            return false;
        }

        if (!LoanDefaulted || loan.collateralLiquidated || !HasCollateral)
        {
            return FinanceReject("Mugirl.CorporateFinance.CannotLiquidate", out reason);
        }

        int proceeds = LoanLiquidationValue;
        int payment = Math.Min(proceeds, LoanBalance);
        // 先标记一次性清算，再销毁资产或记入多余款项。
        loan.collateralLiquidated = true;
        foreach (Thing thing in loan.collateral)
        {
            if (thing != null && !thing.Destroyed)
            {
                thing.Destroy(DestroyMode.Vanish);
            }
        }

        loan.collateral.Clear();
        loan.pendingSilver += proceeds - payment;
        ApplyLoanPayment(payment);
        Record("Mugirl.CorporateFinance.Liquidated", "", proceeds);
        return true;
    }

    private void ApplyLoanPayment(int amount)
    {
        loan.ApplyPayment(amount);
        if (HasLoan)
        {
            return;
        }

        bool hadDebtLock = loan.defaulted;
        loan.principal = loan.interest = 0f;
        loan.defaulted = false;
        loan.nextRaidTick = 0;
        RefreshDebtOverlay();
        if (hadDebtLock)
        {
            NotifyDebtRelationChanged(FactionRelationKind.Hostile);
        }

        FinanceLetter("Mugirl.CorporateFinance.SettledTitle", "Mugirl.CorporateFinance.SettledLetter", 0);
    }

    public bool TryClaimFinanceAssets(CorporateTradeContext context, [NotNullWhen(false)] out string? reason)
    {
        if (!FinanceContextValid(context, out reason))
        {
            return false;
        }

        if (!HasLoan)
        {
            loan.collateral.RemoveAll(t => t == null || t.Destroyed);
            foreach (Thing thing in new List<Thing>(loan.collateral))
            {
                if (!context.Deliver(thing))
                {
                    return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
                }

                loan.collateral.Remove(thing);
            }
        }

        if (loan.pendingSilver > 0)
        {
            if (!context.DeliverSilver(loan.pendingSilver))
            {
                return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
            }

            loan.pendingSilver = 0;
        }

        return true;
    }

    private bool FinanceContextValid([NotNullWhen(true)] CorporateTradeContext? context, [NotNullWhen(false)] out string? reason)
    {
        reason = null;
        if (!Unlocked || context == null || !context.IsValid)
        {
            return FinanceReject("Mugirl.CorporateFinance.InvalidContext", out reason);
        }

        return true;
    }

    private static bool FinanceReject(string key, [NotNullWhen(false)] out string? reason)
    {
        reason = key.Translate();
        return false;
    }

    private void FinanceLetter(string title, string body, int value)
    {
        GameUtility.Letters.ReceiveLetter(title.Translate(), body.Translate(value),
            LoanDefaulted ? LetterDefOf.ThreatBig : LetterDefOf.NeutralEvent);
    }

    private void TryDebtCollectionRaid()
    {
        // 失败时每天重试一次，不会每 tick 重试，也不会换用其他派系。
        loan.nextRaidTick = Now + DayTicks;
        Faction? faction = loan.creditor;
        if (faction == null || faction.defeated)
        {
            return;
        }

        List<Map> homes = PlayerHomeMaps();
        if (homes.Count == 0)
        {
            return;
        }

        Map map = homes.RandomElement();
        float basePoints = StorytellerUtility.DefaultThreatPointsNow(map);
        CorporateFinanceConfigDef config = CorporateDefOf.Mugirl_CorporateFinanceConfig;
        // UnityMathf.Clamp 在配置的下限大于上限时按顺序钳制，不抛异常。
        float multiplier = UnityMathf.Clamp(1f + (LoanBalance / (float)LoanMaximum), 1f, config.maximumRaidMultiplier);
        IncidentParms parms = new IncidentParms
        {
            target = map,
            faction = faction,
            points = UnityMathf.Clamp(basePoints * multiplier, config.minimumRaidPoints, config.maximumRaidPoints),
            raidStrategy = RaidStrategyDefOf.ImmediateAttack,
            raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
            forced = true,
        };
        if (IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
        {
            loan.nextRaidTick = Now + (config.repeatRaidDays.RandomInRange * DayTicks);
        }
    }

    private void NotifyDebtRelationChanged(FactionRelationKind previous)
    {
        if (loan.creditor == null || Faction.OfPlayerSilentFail == null)
        {
            return;
        }

        loan.creditor.Notify_RelationKindChanged(PlayerFactionUtility.PlayerFaction, previous, false, null,
            GlobalTargetInfo.Invalid, out _);
    }

    public bool IsDebtFactionPair(Faction? first, Faction? second)
        => LoanDefaulted && loan.creditor != null && IsDebtPair(loan.creditor, first, second);

    /// <summary>一方是债主派系、另一方是玩家派系。</summary>
    internal static bool IsDebtPair(Faction debtor, Faction? first, Faction? second)
    {
        return first != null && second != null
            && ((first == debtor && second.IsPlayer) || (second == debtor && first.IsPlayer));
    }

    private void RefreshDebtOverlay() => defaultedLoanFaction = LoanDefaulted ? loan.creditor : null;

    private static void ResetDebtOverlay() => defaultedLoanFaction = null;
}
