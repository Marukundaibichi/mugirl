namespace Mugirl.Features.Corporate;

// 历史账目保留记账时的原文和顺序；显示语言变化不会重写已经保存的备注。
internal sealed class CorporateHistory : IExposable
{
    private const int Capacity = 200;
    private List<CorporateHistoryEntry> entries = [];
    internal IReadOnlyList<CorporateHistoryEntry> Entries => entries;

    internal void Add(int tick, string key, string? detail, int amount)
    {
        entries.Add(new CorporateHistoryEntry { tick = tick, key = key, detail = detail, amount = amount });
        if (entries.Count > Capacity)
        {
            entries.RemoveRange(0, entries.Count - Capacity);
        }
    }

    public void ExposeData() => Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);

    internal void RemoveMissingEntries()
    {
        entries ??= [];
        entries.RemoveAll(record => record == null);
    }
}
