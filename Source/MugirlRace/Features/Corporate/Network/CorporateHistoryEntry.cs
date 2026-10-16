namespace Mugirl.Features.Corporate;

// 终端历史记录的一行；key 为翻译键，detail 为已翻译或玩家可读的补充文本。
public sealed class CorporateHistoryEntry : IExposable
{
    public int tick;
    public string key = string.Empty;
    public string? detail;
    public int amount;

    public void ExposeData()
    {
        // key 的空默认值保持稀疏记录的表达；展示时才翻译，历史备注不重建。
        string? savedKey = key;
        Scribe_Values.Look(ref tick, "tick", 0);
        Scribe_Values.Look(ref savedKey, "key", null);
        Scribe_Values.Look(ref detail, "detail", null);
        Scribe_Values.Look(ref amount, "amount", 0);
        key = savedKey ?? string.Empty;
    }
}
