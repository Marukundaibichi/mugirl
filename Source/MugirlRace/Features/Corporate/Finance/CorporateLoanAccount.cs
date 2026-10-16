namespace Mugirl.Features.Corporate;

// 贷款合同与抵押账户：保留逐步浮点运算，实际派系与消息副作用由 Network 编排。
internal sealed class CorporateLoanAccount : IExposable
{
    internal float principal;
    internal float interest;
    internal float overdueInterestTotal;
    internal int originalPrincipal;
    internal int lastAccrualTick;
    internal int dueTick;
    internal int nextRaidTick;
    internal bool defaulted;
    internal bool threeDayReminder;
    internal bool oneDayReminder;
    internal int collateralValue;
    internal bool collateralLiquidated;
    internal List<Thing> collateral = new List<Thing>();
    internal int pendingSilver;
    internal Faction? creditor;
    internal float dailyRate = 0.02f;
    internal float overdueDailyRate = 0.03f;
    internal float liquidationRatio = 0.5f;

    internal bool HasBalance => principal > 0.0001f || interest > 0.0001f;
    internal int Balance => UnityMathf.CeilToInt(UnityMathf.Max(0f, principal + interest - 0.0001f));
    internal int ProjectedBalance => Balance + UnityMathf.CeilToInt(principal * dailyRate
        * Math.Max(0, (dueTick - lastAccrualTick) / CorporateNetwork.DayTicks));

    internal void AccrueInterest(int now)
    {
        int fullDays = Math.Max(0, (now - lastAccrualTick) / CorporateNetwork.DayTicks);
        if (fullDays > 0)
        {
            int normalDays = Math.Min(fullDays, Math.Max(0, (dueTick - lastAccrualTick) / CorporateNetwork.DayTicks));
            interest += principal * dailyRate * normalDays;
            float overdue = UnityMathf.Min(UnityMathf.Max(0f, originalPrincipal - overdueInterestTotal),
                principal * overdueDailyRate * (fullDays - normalDays));
            interest += overdue;
            overdueInterestTotal += overdue;
            lastAccrualTick += fullDays * CorporateNetwork.DayTicks;
        }
    }

    internal void ApplyPayment(int amount)
    {
        float paidInterest = UnityMathf.Min(amount, interest);
        interest -= paidInterest;
        principal = UnityMathf.Max(0f, principal - (amount - paidInterest));
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref principal, "principal", 0f);
        Scribe_Values.Look(ref interest, "interest", 0f);
        Scribe_Values.Look(ref overdueInterestTotal, "overdueInterestTotal", 0f);
        Scribe_Values.Look(ref originalPrincipal, "originalPrincipal", 0);
        Scribe_Values.Look(ref lastAccrualTick, "lastAccrualTick", 0);
        Scribe_Values.Look(ref dueTick, "dueTick", 0);
        Scribe_Values.Look(ref nextRaidTick, "nextRaidTick", 0);
        Scribe_Values.Look(ref defaulted, "defaulted", false);
        Scribe_Values.Look(ref threeDayReminder, "threeDayReminder", false);
        Scribe_Values.Look(ref oneDayReminder, "oneDayReminder", false);
        Scribe_Values.Look(ref collateralValue, "collateralValue", 0);
        Scribe_Values.Look(ref collateralLiquidated, "collateralLiquidated", false);
        Scribe_Values.Look(ref pendingSilver, "pendingSilver", 0);
        Scribe_Values.Look(ref dailyRate, "dailyRate", 0.02f);
        Scribe_Values.Look(ref overdueDailyRate, "overdueDailyRate", 0.03f);
        Scribe_Values.Look(ref liquidationRatio, "liquidationRatio", 0.5f);
        Scribe_References.Look(ref creditor, "creditor");
        Scribe_Collections.Look(ref collateral, "collateral", LookMode.Reference);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            collateral ??= new List<Thing>();
            collateral.RemoveAll(t => t == null || t.Destroyed);
        }
    }
}
