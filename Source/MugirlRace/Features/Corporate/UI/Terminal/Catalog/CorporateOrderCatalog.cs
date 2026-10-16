using System.Diagnostics;
using System.Globalization;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 订购目录的索引、筛选排序与可见区报价。大型模组列表可产生数万条目录记录，所有全量工作都在 <see cref="Update"/>
/// （窗口的 WindowUpdate）中按每帧 0.75 毫秒的预算分步推进；绘制只读取已提交的结果与已算好的报价。
/// </summary>
internal sealed class CorporateOrderCatalog
{
    private const double WorkBudgetMilliseconds = 0.75;

    private readonly List<Entry> entries = [];
    private readonly Dictionary<ThingDef, Entry> entryByDef = [];
    private readonly Dictionary<ThingDef, int> prices = [];
    private readonly Dictionary<ThingDef, float> marketValues = [];
    private List<Entry> view = [];
    private IReadOnlyList<ThingDef>? source;
    private ViewTask? task;
    private int indexPosition;
    private bool indexComplete;
    private int viewVersion;
    private int committedVersion = -1;
    private int priceServiceLevel = -1;
    private int visibleStart;
    private int visibleEnd;
    private int visibleCursor;

    /// <summary>一条目录记录：定义、显示名（及其是否适用快速搜索）、分类位掩码、备货天数与是否特殊渠道。</summary>
    internal readonly record struct Entry(ThingDef Def, string Label, bool LabelIsSimple, int CategoryMask, IntRange Lead, bool Special);

    // 一次筛选排序：按开始时的搜索词、分类与排序方式扫描全部记录，按价格排序时再分时展开价格桶。
    private sealed class ViewTask(int version, string query, int category, int sort)
    {
        internal readonly int Version = version;
        internal readonly string Query = query;
        internal readonly bool QueryIsSimple = CorporateSearchMatcher.IsSimple(query);
        internal readonly int Category = category;
        internal readonly CultureInfo Culture = CultureInfo.CurrentCulture;
        internal readonly List<Entry> Results = [];
        internal readonly CorporatePriceBuckets<Entry>? Buckets = sort == 0 ? null : new CorporatePriceBuckets<Entry>(descending: sort != 1);
        internal int ScanIndex;
    }

    /// <summary>最近一次提交的筛选结果。</summary>
    internal IReadOnlyList<Entry> View => view;

    /// <summary>索引尚未建完或筛选结果尚未提交。</summary>
    internal bool Busy => !indexComplete || task is not null || committedVersion != viewVersion;

    /// <summary>
    /// 推进分时工作：先建索引，再按当前条件筛选排序，空闲时为可见区预热报价。服务等级变化时报价缓存失效并重新筛选。
    /// </summary>
    internal void Update(CorporateNetwork network, string search, int category, int sort)
    {
        int serviceLevel = network.ServiceLevel;
        if (priceServiceLevel < 0)
        {
            priceServiceLevel = serviceLevel;
        }
        else if (priceServiceLevel != serviceLevel)
        {
            priceServiceLevel = serviceLevel;
            prices.Clear();
            RequestRefresh();
        }

        source ??= network.OrderCatalog;
        long started = Stopwatch.GetTimestamp();
        long budget = Math.Max(1L, (long)(Stopwatch.Frequency * WorkBudgetMilliseconds / 1000d));
        bool didAny = false;
        while (!didAny || Stopwatch.GetTimestamp() - started < budget)
        {
            bool progressed = false;
            if (!indexComplete)
            {
                BuildNextEntry(network);
                progressed = true;
                didAny = true;
            }

            if (task is null && committedVersion != viewVersion)
            {
                task = new ViewTask(viewVersion, search.Trim(), category, sort);
            }

            if (task is not null)
            {
                progressed |= AdvanceTask(network, task);
                didAny = true;
            }
            else if (committedVersion == viewVersion)
            {
                progressed |= WarmNextVisibleQuote(network);
                didAny |= progressed;
            }

            if (!progressed)
            {
                break;
            }
        }
    }

    /// <summary>筛选条件改变：丢弃进行中的筛选，下一次 <see cref="Update"/> 按新条件重新开始。</summary>
    internal void RequestRefresh()
    {
        viewVersion++;
        task = null;
        visibleCursor = visibleStart;
    }

    /// <summary>记录当前可见的结果范围 [first, last)，空闲时优先为这些项预热报价。</summary>
    internal void SetVisibleRange(int first, int last)
    {
        if (first == visibleStart && last == visibleEnd)
        {
            return;
        }

        visibleStart = first;
        visibleEnd = last;
        visibleCursor = first;
    }

    internal bool TryGetQuote(ThingDef def, out int price) => prices.TryGetValue(def, out price);

    /// <summary><paramref name="def"/> 已建索引且符合 <paramref name="search"/>（已去除首尾空白）与 <paramref name="category"/>。</summary>
    internal bool IsListed(ThingDef def, string search, int category)
        => entryByDef.TryGetValue(def, out Entry entry)
            && Matches(entry, search, CorporateSearchMatcher.IsSimple(search), category, CultureInfo.CurrentCulture);

    /// <summary>单件报价（按服务等级缓存）；不可订购、生成失败时为 0。</summary>
    internal int UnitPrice(CorporateNetwork network, ThingDef def)
    {
        if (!network.IsOrderable(def))
        {
            return 0;
        }

        int serviceLevel = network.ServiceLevel;
        if (priceServiceLevel != serviceLevel)
        {
            priceServiceLevel = serviceLevel;
            prices.Clear();
        }

        if (prices.TryGetValue(def, out int price))
        {
            return price;
        }

        if (!network.CanAutomaticallySupply(def))
        {
            price = network.OrderQuote(def, def.BaseMarketValue, 1);
            prices[def] = price;
            return price;
        }

        if (marketValues.TryGetValue(def, out float marketValue))
        {
            price = network.OrderQuote(def, marketValue, 1);
        }
        else
        {
            price = QuoteFromPreview(network, def);
        }

        prices[def] = price;
        return price;
    }

    // 生成一件预览品取市价；目录报价不能推进殖民地的随机序列，所以在固定种子块中生成。
    private int QuoteFromPreview(CorporateNetwork network, ThingDef def)
    {
        Thing? preview = null;
        using (Rand.Block(def.shortHash))
        {
            try
            {
                preview = CorporateNetwork.MakeOrderProduct(def, GenStuff.DefaultStuffFor(def), QualityCategory.Normal);
                float marketValue = preview.MarketValue;
                marketValues[def] = marketValue;
                return network.OrderQuote(def, marketValue, 1);
            }
            catch (Exception exception)
            {
                // 其他模组的物品生成或估值失败时按“无法报价”显示；1.6 静默吞掉异常，这里每个定义记录一次警告。
                MugirlLog.WarningOnce("Corporate.CatalogQuote." + def.defName,
                    () => "Corporate catalogue quote failed for " + def.defName + ": " + exception);
                return 0;
            }
            finally
            {
                if (preview is not null)
                {
                    CorporateNetwork.DiscardUnspawnedProduct(preview);
                }
            }
        }
    }

    /// <summary>窗口关闭：释放全部索引、结果与报价缓存。</summary>
    internal void Clear()
    {
        task = null;
        entries.Clear();
        entryByDef.Clear();
        prices.Clear();
        marketValues.Clear();
        view = [];
        source = null;
        indexPosition = 0;
        indexComplete = false;
        committedVersion = -1;
        priceServiceLevel = -1;
    }

    private void BuildNextEntry(CorporateNetwork network)
    {
        if (source is null || indexPosition >= source.Count)
        {
            indexComplete = true;
            return;
        }

        ThingDef def = source[indexPosition++];
        string label = def.LabelCap.ToString();
        Entry entry = new(def, label, CorporateSearchMatcher.IsSimple(label), CategoryMask(network, def), network.OrderLeadTime(def), network.IsSpecialOrder(def));
        entries.Add(entry);
        entryByDef[def] = entry;
        if (indexPosition >= source.Count)
        {
            indexComplete = true;
        }
    }

    /// <summary>分类位掩码：位 0 为全部，1–7 依次为材料、食物、药品、武器、服装、稀有、其他（与分类菜单的下标一致）。</summary>
    private static int CategoryMask(CorporateNetwork network, ThingDef def)
    {
        int mask = 1;
        if (def.IsStuff)
        {
            mask |= 1 << 1;
        }

        if (def.IsNutritionGivingIngestible && !def.IsCorpse)
        {
            mask |= 1 << 2;
        }

        if (def.IsMedicine)
        {
            mask |= 1 << 3;
        }

        if (def.IsWeapon)
        {
            mask |= 1 << 4;
        }

        if (def.IsApparel)
        {
            mask |= 1 << 5;
        }

        if (network.IsRareOrder(def) || network.IsSpecialOrder(def))
        {
            mask |= 1 << 6;
        }

        if (!def.IsStuff && !def.IsNutritionGivingIngestible && !def.IsMedicine && !def.IsWeapon && !def.IsApparel)
        {
            mask |= 1 << 7;
        }

        return mask;
    }

    private static bool Matches(in Entry entry, string query, bool queryIsSimple, int category, CultureInfo culture)
    {
        if (query.Length > 0 && !CorporateSearchMatcher.Contains(entry.Label, entry.LabelIsSimple, query, queryIsSimple, culture))
        {
            return false;
        }

        return category <= 0 || (category < CorporateBrowserText.CategoryKeys.Length && (entry.CategoryMask & (1 << category)) != 0);
    }

    private bool AdvanceTask(CorporateNetwork network, ViewTask current)
    {
        if (current.Version != viewVersion)
        {
            task = null;
            return true;
        }

        if (current.ScanIndex < entries.Count)
        {
            Entry entry = entries[current.ScanIndex++];
            if (Matches(entry, current.Query, current.QueryIsSimple, current.Category, current.Culture))
            {
                if (current.Buckets is null)
                {
                    current.Results.Add(entry);
                }
                else
                {
                    current.Buckets.Add(UnitPrice(network, entry.Def), entry);
                }
            }

            return true;
        }

        if (!indexComplete)
        {
            return false;
        }

        if (current.Buckets is not null && current.Buckets.TryTakeNext(out Entry next))
        {
            current.Results.Add(next);
            return true;
        }

        Commit(current);
        return true;
    }

    private void Commit(ViewTask current)
    {
        if (current != task || current.Version != viewVersion)
        {
            return;
        }

        view = current.Results;
        committedVersion = current.Version;
        task = null;
        visibleCursor = visibleStart;
    }

    private bool WarmNextVisibleQuote(CorporateNetwork network)
    {
        int end = Math.Min(visibleEnd, view.Count);
        if (visibleCursor < visibleStart || visibleCursor >= end)
        {
            visibleCursor = visibleStart;
        }

        while (visibleCursor < end)
        {
            Entry entry = view[visibleCursor++];
            if (prices.ContainsKey(entry.Def))
            {
                continue;
            }

            UnitPrice(network, entry.Def);
            return true;
        }

        return false;
    }
}
