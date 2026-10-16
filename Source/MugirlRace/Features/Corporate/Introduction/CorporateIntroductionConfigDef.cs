namespace Mugirl.Features.Corporate;

// 开户主线参数，XML 见 1.7/Defs/Corporate/CorporateIntroduction.xml。
public class CorporateIntroductionConfigDef : Def
{
    public int longAbsenceTicks = 900000;
    public int retryTicks = 2500;
    public int representativeStayTicks = 240000;
    public int attackRetryTicks = 120000;
    public int contactDelayTicks = 120000;       // 运货员事件后约两天到访。
    public int contactDelayJitterTicks = 9000;   // ±3.6 小时随机浮动。
    public List<ThingDefCountClass> gifts = new List<ThingDefCountClass>();
    public List<ResearchProjectDef> researchGifts = new List<ResearchProjectDef>();
}
