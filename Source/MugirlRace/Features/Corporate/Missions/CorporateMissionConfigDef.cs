namespace Mugirl.Features.Corporate;

// 委托板平衡参数，XML 见 1.7/Defs/QuestScriptDefs/CorporateQuests.xml。
public sealed class CorporateMissionConfigDef : Def
{
    public int weeklyOffers = 6;
    public int activeLimit = 3;
    public int processingDays = 7;
    public int batchMin = 5;
    public int batchMax = 20;
    public float processingFeeMin = 0.25f;
    public float processingFeeMax = 0.4f;
    public int processingMinimumFee = 150;
    public float processingFeePerWorkHour = 20f;
    public int processingMaxDeposit = 20000;
    public int processingFailureGoodwill = -5;
    public int purgeDays = 10;
    public int purgeMinReward = 1000;
    public int purgeMaxReward = 5000;
    public int purgeGoodwill = 5;
    public int distributionDays = 10;
    public float wholesaleFactor = 0.45f;
    public float salesTarget = 0.8f;
    public int distributionGoodwill = 3;
    public int investmentDays = 7;
    public int investmentMin = 1000;
    public int investmentMax = 3000;
    public int highInvestmentDays = 10;
    public int highInvestmentMin = 3000;
    public int highInvestmentMax = 8000;
    public int fusionInviteMinDays = 1;
    public int fusionInviteMaxDays = 3;
    public int fusionOfferDays = 15;
    public int fusionActionDays = 10;
    public int fusionReward = 4000;
    public int fusionGoodwill = 10;
    public int fusionRefusalGoodwill = -60;
    public List<ThingDefCountClass> fusionProtectionRewards = new List<ThingDefCountClass>();
}
