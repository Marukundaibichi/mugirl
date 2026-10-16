using System.Linq;
using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

/// <summary>贷款页：在贷款的状态、还款与违约清算，或以抵押品签订新贷款。</summary>
internal sealed class CorporateFinancePage(Dialog_CorporateTerminal terminal) : CorporateTerminalPage(terminal)
{
    private readonly Dictionary<Thing, int> selection = [];
    private readonly Dictionary<Thing, string> quantityBuffers = [];
    // 抵押品候选按版本缓存（forcePause 下操作后才变化）；行文本随候选一起生成。
    private readonly List<Thing> candidates = [];
    private readonly List<string> candidateLabels = [];
    private readonly HashSet<Thing> candidateSet = [];
    private readonly List<Thing> stale = [];
    private int candidatesVersion = -1;
    private UVector2 scroll;
    private float contentHeight = 800f;
    private int borrow = 500;
    private string borrowBuffer = "500";
    private int repay = 500;
    private string repayBuffer = "500";

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        float width = rect.width - 20f;
        CorporateUI.BeginScrollView(rect, ref scroll, new Rect(0f, 0f, width, contentHeight), "finance/page-scroll");
        float y = 0f;
        try
        {
            CorporateUI.Heading(ref y, width - 128f, "Mugirl.CorporateFinance.Title".Translate(),
                "Mugirl.CorporateFinance.Terms".Translate(
                    (network.CurrentLoanDailyRate * 100f).ToString("0.#"),
                    (network.CurrentLoanOverdueRate * 100f).ToString("0.#"), CorporateNetwork.LoanTermDays));
            Rect terms = new(width - 116f, 4f, 116f, 28f);
            CorporateUI.Label(terms, "Mugirl.CorporateFinance.TermsButton".Translate(), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleRight);
            if (CorporateUI.WantsTooltip(terms))
            {
                TooltipHandler.TipRegion(terms, "Mugirl.CorporateFinance.FinePrint".Translate().ToString()
                    + "\n\n" + "Mugirl.CorporateFinance.CollateralFinePrint".Translate());
            }

            if (network.HasLoan)
            {
                DrawLoan(ref y, width, network);
            }
            else if (network.HasCollateral || network.FinancePendingSilver > 0)
            {
                FinanceText(ref y, width, "Mugirl.CorporateFinance.ReadyToClaim".Translate(CorporateUI.Money(network.FinancePendingSilver)));
                DrawCollateralList(ref y, width, network);
            }
            else
            {
                DrawNewContract(ref y, width, network);
            }

            if ((!network.HasLoan && network.HasCollateral) || network.FinancePendingSilver > 0)
            {
                if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateFinance.Claim".Translate(), true, true, "finance/claim"))
                {
                    FinanceResult(network.TryClaimFinanceAssets(Context, out string? reason), reason);
                }

                y += 44f;
            }
        }
        finally
        {
            CorporateUI.EndScrollView();
        }

        if (GuiPhase.IsLayout)
        {
            contentHeight = y + 12f;
        }
    }

    private void DrawLoan(ref float y, float width, CorporateNetwork network)
    {
        string status = "Mugirl.CorporateFinance.Summary".Translate(
            CorporateUI.Money(network.LoanPrincipal), CorporateUI.Money(network.LoanInterest),
            CorporateUI.Money(network.LoanBalance),
            ((network.LoanDueTick - CorporateNetwork.Now) / 60000f).ToString("0.0"),
            CorporateUI.Money(network.LoanProjectedBalance));
        float height = UnityMathf.Max(110f, Text.CalcHeight(status, width - 20f) + 20f);
        CorporateUI.Notice(new Rect(0f, y, width, height), status, network.LoanDefaulted);
        y += height + 12f;
        if (network.LoanDefaulted)
        {
            FinanceText(ref y, width, "Mugirl.CorporateFinance.Collection".Translate(
                UnityMathf.Max(0f, (network.LoanNextRaidTick - CorporateNetwork.Now) / 60000f).ToString("0.0")), true);
        }

        CorporateUI.Label(new Rect(0f, y, 140f, 30f), "Mugirl.CorporateFinance.Repayment".Translate());
        CorporateUI.Numeric(new Rect(145f, y, UnityMathf.Min(160f, width - 145f), 30f), ref repay, ref repayBuffer, 1, int.MaxValue, "finance/repay");
        y += 38f;
        int silver = Terminal.SilverCount;
        int amount = Math.Min(repay, network.LoanBalance);
        if (CorporateUI.Button(new Rect(0f, y, (width - 8f) / 2f, 36f),
            "Mugirl.CorporateFinance.PayAmount".Translate(CorporateUI.Money(amount)),
            amount > 0 && silver >= amount, true, "finance/pay-amount"))
        {
            FinanceResult(network.TryRepayLoan(Context, amount, out string? reason), reason);
        }

        if (CorporateUI.Button(new Rect((width + 8f) / 2f, y, (width - 8f) / 2f, 36f),
            "Mugirl.CorporateFinance.PayAll".Translate(), Terminal.SilverCount >= network.LoanBalance, id: "finance/pay-all"))
        {
            FinanceResult(network.TryRepayLoan(Context, network.LoanBalance, out string? reason), reason);
        }

        y += 46f;
        if (Terminal.SilverCount < amount)
        {
            FinanceText(ref y, width, "Mugirl.CorporateFinance.NoSilver".Translate(), true);
        }

        FinanceText(ref y, width, "Mugirl.CorporateFinance.HeldValue".Translate(CorporateUI.Money(network.LoanCollateralValue)));
        DrawCollateralList(ref y, width, network);
        if (network.LoanDefaulted && network.HasCollateral)
        {
            if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateFinance.Liquidate".Translate(), id: "finance/liquidate"))
            {
                ConfirmLiquidation(network);
            }

            y += 44f;
        }
    }

    private static void DrawCollateralList(ref float y, float width, CorporateNetwork network)
    {
        IReadOnlyList<Thing> collateral = network.LoanCollateral;
        for (int i = 0; i < collateral.Count; i++)
        {
            Thing? thing = collateral[i];
            if (thing is not null && !thing.Destroyed)
            {
                FinanceText(ref y, width, "• " + thing.LabelCap);
            }
        }
    }

    private void ConfirmLiquidation(CorporateNetwork network)
    {
        int proceeds = network.LoanLiquidationValue;
        IReadOnlyList<Thing> collateral = network.LoanCollateral;
        string[] names = new string[collateral.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = collateral[i]?.LabelCap.ToString() ?? string.Empty;
        }

        Terminal.Confirm(
            "Mugirl.CorporateFinance.LiquidateConfirm".Translate(string.Join("\n", names), CorporateUI.Money(proceeds),
                CorporateUI.Money(Math.Max(0, network.LoanBalance - proceeds))), () =>
            {
                FinanceResult(network.TryLiquidateCollateral(Context, out string? reason), reason);
            }, true);
    }

    private void DrawNewContract(ref float y, float width, CorporateNetwork network)
    {
        bool allowed = network.CanTrade(Context, out string? accessReason);
        if (!allowed && accessReason is not null)
        {
            FinanceText(ref y, width, accessReason, true);
        }

        FinanceText(ref y, width, "Mugirl.CorporateFinance.CollateralRules".Translate(
            (CorporateNetwork.LoanCollateralRatio * 100f).ToString("0"), CorporateNetwork.LoanMinimum, CorporateNetwork.LoanMaximum));
        EnsureCandidates();
        if (candidates.Count == 0)
        {
            FinanceText(ref y, width, "Mugirl.CorporateFinance.EmptyCollateral".Translate());
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Thing thing = candidates[i];
            int quantity = selection.TryGetValue(thing, out int saved) ? saved : 0;
            string buffer = quantityBuffers.TryGetValue(thing, out string? text) ? text : "0";
            CorporateUI.Label(new Rect(0f, y, width - 110f, 43f), candidateLabels[i]);
            CorporateUI.Numeric(new Rect(width - 100f, y, 100f, 30f), ref quantity, ref buffer, 0, thing.stackCount,
                "finance/collateral", thing.thingIDNumber);
            selection[thing] = Math.Min(quantity, thing.stackCount);
            quantityBuffers[thing] = buffer;
            y += 48f;
        }

        float value = PledgeValue();
        int maximum = Math.Min(CorporateNetwork.LoanMaximum, UnityMathf.FloorToInt(value * CorporateNetwork.LoanCollateralRatio));
        FinanceText(ref y, width, "Mugirl.CorporateFinance.SelectedValue".Translate(CorporateUI.Money(value), CorporateUI.Money(maximum)));
        CorporateUI.Label(new Rect(0f, y, 140f, 30f), "Mugirl.CorporateFinance.BorrowAmount".Translate());
        CorporateUI.Numeric(new Rect(145f, y, UnityMathf.Min(160f, width - 145f), 30f),
            ref borrow, ref borrowBuffer, CorporateNetwork.LoanMinimum, CorporateNetwork.LoanMaximum, "finance/borrow");
        y += 40f;
        int due = UnityMathf.CeilToInt(borrow * (1f + CorporateNetwork.LoanDailyRate * CorporateNetwork.LoanTermDays));
        FinanceText(ref y, width, "Mugirl.CorporateFinance.PreviewDue".Translate(CorporateUI.Money(due), CorporateNetwork.LoanTermDays));
        bool enough = borrow >= CorporateNetwork.LoanMinimum && borrow <= maximum;
        if (CorporateUI.Button(new Rect(0f, y, width, 36f), "Mugirl.CorporateFinance.Sign".Translate(), allowed && enough, true, "finance/sign"))
        {
            ConfirmLoan(network, due);
        }

        y += 44f;
        if (!enough)
        {
            FinanceText(ref y, width, "Mugirl.CorporateFinance.InsufficientCollateral".Translate(), true);
        }
    }

    // 候选与行文本按缓存版本重建；失效的选择只会在候选变化时出现，所以也只在这里清理。
    private void EnsureCandidates()
    {
        if (candidatesVersion == Terminal.CacheVersion)
        {
            return;
        }

        candidatesVersion = Terminal.CacheVersion;
        candidates.Clear();
        foreach (Thing thing in Context.AvailableThings)
        {
            if (CorporateNetwork.IsEligibleCollateral(thing))
            {
                candidates.Add(thing);
            }
        }

        // 稳定排序（与 1.6 的 OrderBy 相同）：同名物品保持原有顺序。
        List<Thing> sorted = candidates.OrderBy(static t => t.LabelCap.ToString(), StringComparer.CurrentCulture).ToList();
        candidates.Clear();
        candidates.AddRange(sorted);
        candidateSet.Clear();
        candidateLabels.Clear();
        string unit = "Mugirl.CorporateFinance.Unit".Translate();
        for (int i = 0; i < candidates.Count; i++)
        {
            Thing thing = candidates[i];
            candidateSet.Add(thing);
            candidateLabels.Add(thing.LabelCap + " · " + CorporateUI.Money(thing.MarketValue) + "/" + unit);
        }

        stale.Clear();
        foreach (Thing old in selection.Keys)
        {
            if (!candidateSet.Contains(old))
            {
                stale.Add(old);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            selection.Remove(stale[i]);
            quantityBuffers.Remove(stale[i]);
        }

        stale.Clear();
    }

    // 与 CorporateNetwork.TryTakeLoan 的抵押估值逐步相同：只计数量为正的条目，按登记顺序逐项累加单精度乘积。
    // 1.6 用 LINQ Sum（双精度累加）显示上限，边界附近“签约”按钮可用而网络端以抵押不足拒绝，或反之。
    private float PledgeValue()
    {
        float value = 0f;
        foreach (KeyValuePair<Thing, int> pair in selection)
        {
            if (pair.Value > 0)
            {
                value += pair.Key.MarketValue * pair.Value;
            }
        }

        return value;
    }

    private void ConfirmLoan(CorporateNetwork network, int due)
    {
        Dictionary<Thing, int> pledge = [];
        List<string> items = [];
        foreach (KeyValuePair<Thing, int> pair in selection)
        {
            if (pair.Value > 0)
            {
                pledge.Add(pair.Key, pair.Value);
                items.Add(pair.Key.LabelNoCount + " × " + pair.Value);
            }
        }

        int amount = borrow;
        Terminal.Confirm("Mugirl.CorporateFinance.SignConfirm".Translate(
            CorporateUI.Money(amount), string.Join("\n", items), CorporateUI.Money(due), CorporateNetwork.LoanTermDays)
            + "\n\n" + "Mugirl.CorporateFinance.FinePrint".Translate(), () =>
            {
                bool success = network.TryTakeLoan(Context, amount, pledge, out string? reason);
                if (success)
                {
                    selection.Clear();
                    quantityBuffers.Clear();
                }

                FinanceResult(success, reason);
            }, true);
    }

    private static void FinanceText(ref float y, float width, string text, bool warning = false)
    {
        float height = UnityMathf.Max(28f, Text.CalcHeight(text, width));
        CorporateUI.Label(new Rect(0f, y, width, height), text, color: warning ? CorporateUI.Danger : CorporateUI.Ink);
        y += height + 8f;
    }

    private void FinanceResult(bool success, string? reason)
        => Terminal.ShowFeedback(success, success ? "Mugirl.CorporateFinance.Success".Translate().ToString() : reason);
}
