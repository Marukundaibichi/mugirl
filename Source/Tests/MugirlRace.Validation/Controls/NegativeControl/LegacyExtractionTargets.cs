namespace Mugirl.Control.Legacy;

// 提取器对照（Controls/Legacy）：负对照程序集同时作为一个“旧构建”被提取，Controls/Legacy/control-save.rws 是它写的存档。

/// <summary>以类名存档的物品子类：controlCharge 是它静态写的标签；存档中它的元素下还有原版 Thing 的标签（def、id、health）与来源不明的标签。</summary>
public class ControlApparel : RimWorld.Apparel
{
    private int charge;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref charge, "controlCharge");
    }
}
