using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms
    {
        private Vector2 financeScroll;
        private float financeHeight = 800f;
        private int financeBorrow = 500;
        private string financeBorrowBuffer = "500";
        private int financeRepay = 500;
        private string financeRepayBuffer = "500";
        private readonly Dictionary<Thing, int> financeSelection = new Dictionary<Thing, int>();
        private readonly Dictionary<Thing, string> financeQuantityBuffers = new Dictionary<Thing, string>();

        private void DrawFinance(Rect rect)
        {
            network.UpdateLoanAccrual();
            float width = rect.width - 20f;
            Widgets.BeginScrollView(rect, ref financeScroll, new Rect(0f, 0f, width, financeHeight));
            float y = 0f;
            try
            {
                CorporateUI.Heading(ref y, width, "Mugirl.CorporateFinance.Title".Translate(),
                    "Mugirl.CorporateFinance.Terms".Translate(
                        (network.CurrentLoanDailyRate * 100f).ToString("0.#"),
                        (network.CurrentLoanOverdueRate * 100f).ToString("0.#"), CorporateNetwork.LoanTermDays));
                if (network.HasLoan)
                {
                    string status = "Mugirl.CorporateFinance.Summary".Translate(
                        CorporateUI.Money(network.LoanPrincipal), CorporateUI.Money(network.LoanInterest),
                        CorporateUI.Money(network.LoanBalance),
                        ((network.LoanDueTick - CorporateNetwork.Now) / 60000f).ToString("0.0"),
                        CorporateUI.Money(network.LoanProjectedBalance));
                    float height = Mathf.Max(110f, Text.CalcHeight(status, width - 20f) + 20f);
                    CorporateUI.Notice(new Rect(0f, y, width, height), status, network.LoanDefaulted);
                    y += height + 12f;
                    if (network.LoanDefaulted)
                    {
                        FinanceText(ref y, width, "Mugirl.CorporateFinance.Collection".Translate(
                            Math.Max(0f, (network.LoanNextRaidTick - CorporateNetwork.Now) / 60000f).ToString("0.0")), true);
                    }
                    CorporateUI.Label(new Rect(0f, y, 140f, 30f), "Mugirl.CorporateFinance.Repayment".Translate());
                    Widgets.TextFieldNumeric(new Rect(145f, y, Mathf.Min(160f, width - 145f), 30f),
                        ref financeRepay, ref financeRepayBuffer, 1, int.MaxValue);
                    y += 38f;
                    int amount = Math.Min(financeRepay, network.LoanBalance);
                    if (CorporateUI.Button(new Rect(0f, y, (width - 8f) / 2f, 36f),
                        "Mugirl.CorporateFinance.PayAmount".Translate(CorporateUI.Money(amount)),
                        amount > 0 && context.SilverCount >= amount, true))
                    {
                        string reason;
                        FinanceResult(network.TryRepayLoan(context, amount, out reason), reason);
                    }
                    if (CorporateUI.Button(new Rect((width + 8f) / 2f, y, (width - 8f) / 2f, 36f),
                        "Mugirl.CorporateFinance.PayAll".Translate(), context.SilverCount >= network.LoanBalance))
                    {
                        string reason;
                        FinanceResult(network.TryRepayLoan(context, network.LoanBalance, out reason), reason);
                    }
                    y += 46f;
                    if (context.SilverCount < amount)
                        FinanceText(ref y, width, "Mugirl.CorporateFinance.NoSilver".Translate(), true);
                    FinanceText(ref y, width, "Mugirl.CorporateFinance.HeldValue".Translate(CorporateUI.Money(network.LoanCollateralValue)));
                    foreach (Thing thing in network.LoanCollateral)
                        if (thing != null && !thing.Destroyed) FinanceText(ref y, width, "• " + thing.LabelCap);
                    if (network.LoanDefaulted && network.HasCollateral)
                    {
                        if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateFinance.Liquidate".Translate()))
                        {
                            int proceeds = network.LoanLiquidationValue;
                            string items = string.Join("\n", network.LoanCollateral.Select(t => t.LabelCap.ToString()).ToArray());
                            MugirlGameUtility.Windows.Add(Dialog_MessageBox.CreateConfirmation(
                                "Mugirl.CorporateFinance.LiquidateConfirm".Translate(items, CorporateUI.Money(proceeds),
                                    CorporateUI.Money(Math.Max(0, network.LoanBalance - proceeds))), () =>
                                {
                                    string reason;
                                    FinanceResult(network.TryLiquidateCollateral(context, out reason), reason);
                                }, true));
                        }
                        y += 44f;
                    }
                }
                else if (network.HasCollateral || network.FinancePendingSilver > 0)
                {
                    FinanceText(ref y, width, "Mugirl.CorporateFinance.ReadyToClaim".Translate(CorporateUI.Money(network.FinancePendingSilver)));
                    foreach (Thing thing in network.LoanCollateral)
                        if (thing != null && !thing.Destroyed) FinanceText(ref y, width, "• " + thing.LabelCap);
                }
                else
                {
                    DrawNewFinanceContract(ref y, width);
                }
                if ((!network.HasLoan && network.HasCollateral) || network.FinancePendingSilver > 0)
                {
                    if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateFinance.Claim".Translate(), true, true))
                    {
                        string reason;
                        FinanceResult(network.TryClaimFinanceAssets(context, out reason), reason);
                    }
                    y += 44f;
                }
            }
            finally { Widgets.EndScrollView(); }
            if (Event.current.type == EventType.Layout) financeHeight = y + 12f;
        }

        private void DrawNewFinanceContract(ref float y, float width)
        {
            string accessReason;
            bool allowed = network.CanTrade(context, out accessReason);
            if (!allowed) FinanceText(ref y, width, accessReason, true);
            FinanceText(ref y, width, "Mugirl.CorporateFinance.CollateralRules".Translate(
                (CorporateNetwork.LoanCollateralRatio * 100f).ToString("0"), CorporateNetwork.LoanMinimum, CorporateNetwork.LoanMaximum));
            List<Thing> candidates = context.AvailableThings.Where(CorporateNetwork.IsEligibleCollateral)
                .OrderBy(t => t.LabelCap.ToString()).ToList();
            foreach (Thing old in financeSelection.Keys.Where(t => !candidates.Contains(t)).ToList())
            {
                financeSelection.Remove(old);
                financeQuantityBuffers.Remove(old);
            }
            if (candidates.Count == 0) FinanceText(ref y, width, "Mugirl.CorporateFinance.EmptyCollateral".Translate());
            foreach (Thing thing in candidates)
            {
                int quantity = financeSelection.TryGetValue(thing, out int saved) ? saved : 0;
                string buffer = financeQuantityBuffers.TryGetValue(thing, out string text) ? text : "0";
                CorporateUI.Label(new Rect(0f, y, width - 110f, 43f),
                    thing.LabelCap + " · " + CorporateUI.Money(thing.MarketValue) + "/" + "Mugirl.CorporateFinance.Unit".Translate());
                Widgets.TextFieldNumeric(new Rect(width - 100f, y, 100f, 30f), ref quantity, ref buffer, 0, thing.stackCount);
                financeSelection[thing] = Math.Min(quantity, thing.stackCount);
                financeQuantityBuffers[thing] = buffer;
                y += 48f;
            }
            float value = financeSelection.Sum(pair => pair.Key.MarketValue * pair.Value);
            int maximum = Math.Min(CorporateNetwork.LoanMaximum, Mathf.FloorToInt(value * CorporateNetwork.LoanCollateralRatio));
            FinanceText(ref y, width, "Mugirl.CorporateFinance.SelectedValue".Translate(CorporateUI.Money(value), CorporateUI.Money(maximum)));
            CorporateUI.Label(new Rect(0f, y, 140f, 30f), "Mugirl.CorporateFinance.BorrowAmount".Translate());
            Widgets.TextFieldNumeric(new Rect(145f, y, Mathf.Min(160f, width - 145f), 30f),
                ref financeBorrow, ref financeBorrowBuffer, CorporateNetwork.LoanMinimum, CorporateNetwork.LoanMaximum);
            y += 40f;
            int due = Mathf.CeilToInt(financeBorrow * (1f + CorporateNetwork.LoanDailyRate * CorporateNetwork.LoanTermDays));
            FinanceText(ref y, width, "Mugirl.CorporateFinance.PreviewDue".Translate(CorporateUI.Money(due), CorporateNetwork.LoanTermDays));
            bool enough = financeBorrow >= CorporateNetwork.LoanMinimum && financeBorrow <= maximum;
            if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateFinance.Sign".Translate(), allowed && enough, true))
            {
                Dictionary<Thing, int> pledge = financeSelection.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value);
                int amount = financeBorrow;
                string items = string.Join("\n", pledge.Select(p => p.Key.LabelNoCount + " × " + p.Value).ToArray());
                MugirlGameUtility.Windows.Add(Dialog_MessageBox.CreateConfirmation("Mugirl.CorporateFinance.SignConfirm".Translate(
                    CorporateUI.Money(amount), items, CorporateUI.Money(due), CorporateNetwork.LoanTermDays), () =>
                    {
                        string reason;
                        bool success = network.TryTakeLoan(context, amount, pledge, out reason);
                        if (success) { financeSelection.Clear(); financeQuantityBuffers.Clear(); }
                        FinanceResult(success, reason);
                    }, true));
            }
            y += 44f;
            if (!enough) FinanceText(ref y, width, "Mugirl.CorporateFinance.InsufficientCollateral".Translate(), true);
        }

        private static void FinanceText(ref float y, float width, string text, bool warning = false)
        {
            float height = Mathf.Max(28f, Text.CalcHeight(text, width));
            CorporateUI.Label(new Rect(0f, y, width, height), text, color: warning ? CorporateUI.Danger : CorporateUI.Ink);
            y += height + 8f;
        }

        private static void FinanceResult(bool success, string reason)
        {
            Messages.Message(success ? "Mugirl.CorporateFinance.Success".Translate().ToString() : reason,
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, false);
        }
    }
}
