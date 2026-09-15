using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public sealed class CorporateFinanceConfigDef : Def
    {
        public float dailyRate = 0.02f;
        public float overdueDailyRate = 0.03f;
        public float collateralRatio = 0.7f;
        public float liquidationRatio = 0.5f;
        public int termDays = 14;
        public int minimumPrincipal = 500;
        public int maximumPrincipal = 30000;
        public int firstRaidDelayDays = 1;
        public IntRange repeatRaidDays = new IntRange(2, 4);
        public float maximumRaidMultiplier = 1.5f;
        public float minimumRaidPoints = 250f;
        public float maximumRaidPoints = 10000f;
    }

    [DefOf]
    public static class CorporateFinanceDefOf
    {
        public static CorporateFinanceConfigDef Mugirl_CorporateFinance;
        static CorporateFinanceDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CorporateFinanceDefOf)); }
    }

    public partial class CorporateNetwork
    {
        public static float LoanDailyRate => CorporateFinanceDefOf.Mugirl_CorporateFinance.dailyRate;
        public static float LoanOverdueDailyRate => CorporateFinanceDefOf.Mugirl_CorporateFinance.overdueDailyRate;
        public static float LoanCollateralRatio => CorporateFinanceDefOf.Mugirl_CorporateFinance.collateralRatio;
        public static int LoanTermDays => CorporateFinanceDefOf.Mugirl_CorporateFinance.termDays;
        public static int LoanMinimum => CorporateFinanceDefOf.Mugirl_CorporateFinance.minimumPrincipal;
        public static int LoanMaximum => CorporateFinanceDefOf.Mugirl_CorporateFinance.maximumPrincipal;

        private float loanPrincipal;
        private float loanInterest;
        private float loanOverdueInterestTotal;
        private int loanOriginalPrincipal;
        private int loanLastAccrualTick;
        private int loanDueTick;
        private int loanNextRaidTick;
        private bool loanDefaulted;
        private bool loanThreeDayReminder;
        private bool loanOneDayReminder;
        private int loanCollateralValue;
        private bool loanCollateralLiquidated;
        private List<Thing> loanCollateral = new List<Thing>();
        private int financePendingSilver;
        private Faction loanFaction;
        private float loanSignedDailyRate = 0.02f;
        private float loanSignedOverdueRate = 0.03f;
        private float loanSignedLiquidationRatio = 0.5f;

        public float LoanPrincipal => loanPrincipal;
        public float LoanInterest => loanInterest;
        public int LoanBalance => Mathf.CeilToInt(Mathf.Max(0f, loanPrincipal + loanInterest - 0.0001f));
        public int LoanDueTick => loanDueTick;
        public bool HasLoan => loanPrincipal > 0.0001f || loanInterest > 0.0001f;
        public bool LoanDefaulted => loanDefaulted && HasLoan;
        public int LoanNextRaidTick => loanNextRaidTick;
        public int LoanCollateralValue => loanCollateralValue;
        public IReadOnlyList<Thing> LoanCollateral => loanCollateral;
        public bool HasCollateral => loanCollateral.Any(t => t != null && !t.Destroyed);
        public int FinancePendingSilver => financePendingSilver;
        public float CurrentLoanDailyRate => HasLoan ? loanSignedDailyRate : LoanDailyRate;
        public float CurrentLoanOverdueRate => HasLoan ? loanSignedOverdueRate : LoanOverdueDailyRate;
        public int LoanLiquidationValue => Mathf.FloorToInt(loanCollateralValue * loanSignedLiquidationRatio);
        public int LoanProjectedBalance => LoanBalance + Mathf.CeilToInt(loanPrincipal * loanSignedDailyRate *
            Math.Max(0, (loanDueTick - loanLastAccrualTick) / DayTicks));

        partial void FinanceExposeData()
        {
            Scribe_Values.Look(ref loanPrincipal, "corporateLoanPrincipal", 0f);
            Scribe_Values.Look(ref loanInterest, "corporateLoanInterest", 0f);
            Scribe_Values.Look(ref loanOverdueInterestTotal, "corporateLoanOverdueInterestTotal", 0f);
            Scribe_Values.Look(ref loanOriginalPrincipal, "corporateLoanOriginalPrincipal", 0);
            Scribe_Values.Look(ref loanLastAccrualTick, "corporateLoanLastAccrualTick", 0);
            Scribe_Values.Look(ref loanDueTick, "corporateLoanDueTick", 0);
            Scribe_Values.Look(ref loanNextRaidTick, "corporateLoanNextRaidTick", 0);
            Scribe_Values.Look(ref loanDefaulted, "corporateLoanDefaulted", false);
            Scribe_Values.Look(ref loanThreeDayReminder, "corporateLoanThreeDayReminder", false);
            Scribe_Values.Look(ref loanOneDayReminder, "corporateLoanOneDayReminder", false);
            Scribe_Values.Look(ref loanCollateralValue, "corporateLoanCollateralValue", 0);
            Scribe_Values.Look(ref loanCollateralLiquidated, "corporateLoanCollateralLiquidated", false);
            Scribe_Values.Look(ref financePendingSilver, "corporateFinancePendingSilver", 0);
            Scribe_Values.Look(ref loanSignedDailyRate, "corporateLoanSignedDailyRate", 0.02f);
            Scribe_Values.Look(ref loanSignedOverdueRate, "corporateLoanSignedOverdueRate", 0.03f);
            Scribe_Values.Look(ref loanSignedLiquidationRatio, "corporateLoanSignedLiquidationRatio", 0.5f);
            Scribe_References.Look(ref loanFaction, "corporateLoanFaction");
            Scribe_Collections.Look(ref loanCollateral, "corporateLoanCollateral", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (loanCollateral == null) loanCollateral = new List<Thing>();
                loanCollateral.RemoveAll(t => t == null || t.Destroyed);
            }
        }

        partial void FinanceTick()
        {
            if (!HasLoan) return;
            UpdateLoanAccrual();
            if (!loanThreeDayReminder && Now >= loanDueTick - 3 * DayTicks && Now < loanDueTick)
            {
                loanThreeDayReminder = true;
                FinanceLetter("Mugirl.CorporateFinance.ReminderTitle", "Mugirl.CorporateFinance.Reminder", 3);
            }
            if (!loanOneDayReminder && Now >= loanDueTick - DayTicks && Now < loanDueTick)
            {
                loanOneDayReminder = true;
                FinanceLetter("Mugirl.CorporateFinance.ReminderTitle", "Mugirl.CorporateFinance.Reminder", 1);
            }
            if (LoanDefaulted && Now >= loanNextRaidTick) TryDebtCollectionRaid();
        }

        public void UpdateLoanAccrual()
        {
            if (!HasLoan) return;
            int fullDays = Math.Max(0, (Now - loanLastAccrualTick) / DayTicks);
            if (fullDays > 0)
            {
                int normalDays = Math.Min(fullDays, Math.Max(0, (loanDueTick - loanLastAccrualTick) / DayTicks));
                loanInterest += loanPrincipal * loanSignedDailyRate * normalDays;
                float overdue = Mathf.Min(Mathf.Max(0f, loanOriginalPrincipal - loanOverdueInterestTotal),
                    loanPrincipal * loanSignedOverdueRate * (fullDays - normalDays));
                loanInterest += overdue;
                loanOverdueInterestTotal += overdue;
                loanLastAccrualTick += fullDays * DayTicks;
            }
            if (Now >= loanDueTick && !loanDefaulted)
            {
                FactionRelationKind previous = loanFaction?.RelationKindWith(MugirlWildSlaveUtility.PlayerFaction) ?? FactionRelationKind.Neutral;
                loanDefaulted = true;
                loanNextRaidTick = Now + CorporateFinanceDefOf.Mugirl_CorporateFinance.firstRaidDelayDays * DayTicks;
                NotifyDebtRelationChanged(previous);
                Record("Mugirl.CorporateFinance.DefaultTitle", "", LoanBalance);
                FinanceLetter("Mugirl.CorporateFinance.DefaultTitle", "Mugirl.CorporateFinance.DefaultLetter", LoanBalance);
            }
        }

        public static bool IsEligibleCollateral(Thing thing)
        {
            return thing != null && !thing.Destroyed && thing.def.category == ThingCategory.Item
                && !(thing is Pawn) && !(thing is Corpse) && !(thing is IThingHolder)
                && thing.def != ThingDefOf.Silver && thing.def.tradeability != Tradeability.None
                && thing.TryGetComp<CompRottable>() == null && thing.TryGetComp<CompHatcher>() == null
                && (!(thing is ThingWithComps withComps) || !withComps.AllComps.OfType<IThingHolder>()
                    .Any(holder => holder.GetDirectlyHeldThings()?.Count > 0))
                && thing.questTags.NullOrEmpty() && thing.MarketValue > 0f;
        }

        public bool TryTakeLoan(CorporateTradeContext context, int amount, IDictionary<Thing, int> collateral, out string reason)
        {
            UpdateLoanAccrual();
            if (!CanTrade(context, out reason)) return false;
            if (HasLoan || HasCollateral || financePendingSilver > 0)
                return FinanceReject("Mugirl.CorporateFinance.Existing", out reason);
            if (amount < LoanMinimum || amount > LoanMaximum || collateral == null || collateral.Count == 0)
                return FinanceReject("Mugirl.CorporateFinance.InvalidAmount", out reason);

            HashSet<Thing> available = new HashSet<Thing>(context.AvailableThings);
            float value = 0f;
            foreach (KeyValuePair<Thing, int> entry in collateral)
            {
                if (!available.Contains(entry.Key) || !IsEligibleCollateral(entry.Key)
                    || entry.Value <= 0 || entry.Value > entry.Key.stackCount)
                    return FinanceReject("Mugirl.CorporateFinance.CollateralChanged", out reason);
                value += entry.Key.MarketValue * entry.Value;
            }
            if (amount > Mathf.FloorToInt(value * LoanCollateralRatio))
                return FinanceReject("Mugirl.CorporateFinance.InsufficientCollateral", out reason);

            // Once removed from its source, every asset has the persistent vault as its owner.
            foreach (KeyValuePair<Thing, int> entry in collateral)
            {
                Thing taken;
                if (!context.TryTake(entry.Key, entry.Value, out taken))
                    return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
                if (!Vault.TryAdd(taken, false))
                {
                    context.Deliver(taken);
                    return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
                }
                loanCollateral.Add(taken);
            }
            if (!context.DeliverSilver(amount))
                return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);

            loanPrincipal = loanOriginalPrincipal = amount;
            loanInterest = loanOverdueInterestTotal = 0f;
            loanLastAccrualTick = Now;
            loanDueTick = Now + LoanTermDays * DayTicks;
            loanNextRaidTick = 0;
            loanDefaulted = loanThreeDayReminder = loanOneDayReminder = loanCollateralLiquidated = false;
            loanCollateralValue = Mathf.FloorToInt(value);
            loanFaction = CorporateFaction;
            loanSignedDailyRate = LoanDailyRate;
            loanSignedOverdueRate = LoanOverdueDailyRate;
            loanSignedLiquidationRatio = CorporateFinanceDefOf.Mugirl_CorporateFinance.liquidationRatio;
            Record("Mugirl.CorporateFinance.LoanSigned", "", amount);
            reason = null;
            return true;
        }

        public bool TryRepayLoan(CorporateTradeContext context, int amount, out string reason)
        {
            UpdateLoanAccrual();
            if (!FinanceContextValid(context, out reason)) return false;
            if (!HasLoan || amount <= 0) return FinanceReject("Mugirl.CorporateFinance.NoDebt", out reason);
            amount = Math.Min(amount, LoanBalance);
            if (!context.TrySpendSilver(amount)) return FinanceReject("Mugirl.CorporateFinance.NoSilver", out reason);
            ApplyLoanPayment(amount);
            Record("Mugirl.CorporateFinance.Payment", "", -amount);
            return true;
        }

        public bool TryLiquidateCollateral(CorporateTradeContext context, out string reason)
        {
            UpdateLoanAccrual();
            if (!FinanceContextValid(context, out reason)) return false;
            if (!LoanDefaulted || loanCollateralLiquidated || !HasCollateral)
                return FinanceReject("Mugirl.CorporateFinance.CannotLiquidate", out reason);
            int proceeds = LoanLiquidationValue;
            int payment = Math.Min(proceeds, LoanBalance);
            // Mark the one-shot settlement before destroying assets or paying an excess.
            loanCollateralLiquidated = true;
            foreach (Thing thing in loanCollateral.ToList())
                if (thing != null && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
            loanCollateral.Clear();
            financePendingSilver += proceeds - payment;
            ApplyLoanPayment(payment);
            Record("Mugirl.CorporateFinance.Liquidated", "", proceeds);
            return true;
        }

        private void ApplyLoanPayment(int amount)
        {
            float paidInterest = Mathf.Min(amount, loanInterest);
            loanInterest -= paidInterest;
            loanPrincipal = Mathf.Max(0f, loanPrincipal - (amount - paidInterest));
            if (!HasLoan)
            {
                bool hadDebtLock = loanDefaulted;
                loanPrincipal = loanInterest = 0f;
                loanDefaulted = false;
                loanNextRaidTick = 0;
                if (hadDebtLock) NotifyDebtRelationChanged(FactionRelationKind.Hostile);
                FinanceLetter("Mugirl.CorporateFinance.SettledTitle", "Mugirl.CorporateFinance.SettledLetter", 0);
            }
        }

        public bool TryClaimFinanceAssets(CorporateTradeContext context, out string reason)
        {
            if (!FinanceContextValid(context, out reason)) return false;
            if (!HasLoan)
            {
                foreach (Thing thing in loanCollateral.ToList())
                {
                    if (thing == null || thing.Destroyed) { loanCollateral.Remove(thing); continue; }
                    if (!context.Deliver(thing)) return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
                    loanCollateral.Remove(thing);
                }
            }
            if (financePendingSilver > 0)
            {
                if (!context.DeliverSilver(financePendingSilver)) return FinanceReject("Mugirl.CorporateFinance.TransferFailed", out reason);
                financePendingSilver = 0;
            }
            return true;
        }

        private bool FinanceContextValid(CorporateTradeContext context, out string reason)
        {
            reason = null;
            if (!Unlocked || context == null || !context.IsValid)
                return FinanceReject("Mugirl.CorporateFinance.InvalidContext", out reason);
            return true;
        }

        private static bool FinanceReject(string key, out string reason)
        {
            reason = key.Translate();
            return false;
        }

        private void FinanceLetter(string title, string body, int value)
        {
            MugirlGameUtility.Letters.ReceiveLetter(title.Translate(), body.Translate(value),
                LoanDefaulted ? LetterDefOf.ThreatBig : LetterDefOf.NeutralEvent);
        }

        private void TryDebtCollectionRaid()
        {
            // Failed attempts retry daily, never on every tick and never with a substitute faction.
            loanNextRaidTick = Now + DayTicks;
            Faction faction = loanFaction;
            if (faction == null || faction.defeated) return;
            List<Map> maps = MugirlGameUtility.LoadedMaps.Where(m => m.IsPlayerHome).ToList();
            if (maps.Count == 0) return;
            Map map = maps.RandomElement();
            float basePoints = StorytellerUtility.DefaultThreatPointsNow(map);
            CorporateFinanceConfigDef config = CorporateFinanceDefOf.Mugirl_CorporateFinance;
            float multiplier = Mathf.Clamp(1f + LoanBalance / (float)LoanMaximum, 1f, config.maximumRaidMultiplier);
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = Mathf.Clamp(basePoints * multiplier, config.minimumRaidPoints, config.maximumRaidPoints),
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
                forced = true
            };
            if (IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                loanNextRaidTick = Now + config.repeatRaidDays.RandomInRange * DayTicks;
        }

        private void NotifyDebtRelationChanged(FactionRelationKind previous)
        {
            if (loanFaction == null || Faction.OfPlayerSilentFail == null) return;
            bool sentLetter;
            loanFaction.Notify_RelationKindChanged(MugirlWildSlaveUtility.PlayerFaction, previous, false, null,
                GlobalTargetInfo.Invalid, out sentLetter);
        }

        public bool IsDebtFactionPair(Faction first, Faction second)
        {
            return LoanDefaulted && loanFaction != null && first != null && second != null
                && ((first == loanFaction && second.IsPlayer) || (second == loanFaction && first.IsPlayer));
        }
    }

    [HarmonyPatch(typeof(FactionUtility), nameof(FactionUtility.HostileTo))]
    internal static class CorporateDebt_HostileTo_Patch
    {
        public static void Postfix(Faction fac, Faction other, ref bool __result)
        {
            if (__result || fac == null || other == null || (!fac.IsPlayer && !other.IsPlayer)) return;
            if (fac.def != MugirlContentDefOf.Mugirl_GiantCorporations_Hostile
                && other.def != MugirlContentDefOf.Mugirl_GiantCorporations_Hostile) return;
            if (CorporateNetwork.Current?.IsDebtFactionPair(fac, other) == true) __result = true;
        }
    }

    // Debt is an overlay: underlying goodwill still records independent hostile actions.
    [HarmonyPatch(typeof(Faction), nameof(Faction.RelationKindWith))]
    internal static class CorporateDebt_RelationKind_Patch
    {
        public static void Postfix(Faction __instance, Faction other, ref FactionRelationKind __result)
        {
            if (__result == FactionRelationKind.Hostile || other == null || (!__instance.IsPlayer && !other.IsPlayer)) return;
            if (__instance.def != MugirlContentDefOf.Mugirl_GiantCorporations_Hostile
                && other.def != MugirlContentDefOf.Mugirl_GiantCorporations_Hostile) return;
            if (CorporateNetwork.Current?.IsDebtFactionPair(__instance, other) == true)
                __result = FactionRelationKind.Hostile;
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.CanChangeGoodwillFor))]
    internal static class CorporateDebt_Goodwill_Patch
    {
        public static void Postfix(Faction __instance, Faction other, int goodwillChange, ref bool __result)
        {
            if (goodwillChange > 0 && CorporateNetwork.Current?.IsDebtFactionPair(__instance, other) == true)
                __result = false;
        }
    }
}
