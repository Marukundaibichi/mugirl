using Mugirl.Features.Xenotype;

namespace Mugirl.SaveUpgrade;

/// <summary>跨存读档保留一次性异种初始化的剩余等待；业务判定由 XenotypeUtility 执行。</summary>
public sealed class GameComponent_MilkUpgrade : GameComponent
{
    private bool xenotypeRepairPending = true;
    private int xenotypeRepairTicks = 300;

    public GameComponent_MilkUpgrade()
    {
    }

    public GameComponent_MilkUpgrade(Game game)
    {
    }

    public override void GameComponentTick()
    {
        if (XenotypeRepairTiming.ShouldRun(ref xenotypeRepairTicks, xenotypeRepairPending, ModsConfig.BiotechActive))
        {
            XenotypeUtility.InitializeEligiblePawns();
            xenotypeRepairPending = false;
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        // 升级失败时仍写来源戳；始终保存完整当前形状，下一次不能把省略的默认字段误判成混合旧结构。
        Scribe_Values.Look(ref xenotypeRepairPending, "xenotypeRepairPending", true, forceSave: true);
        Scribe_Values.Look(ref xenotypeRepairTicks, "xenotypeRepairTicks", 300, forceSave: true);
    }
}
