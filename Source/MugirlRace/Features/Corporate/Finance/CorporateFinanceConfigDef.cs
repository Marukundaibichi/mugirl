namespace Mugirl.Features.Corporate;

// 贷款条款，XML 见 1.7/Defs/Misc/CorporateFinance.xml。签约时把利率与清算比例写入存档，之后修改 Def 不影响已签贷款。
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
