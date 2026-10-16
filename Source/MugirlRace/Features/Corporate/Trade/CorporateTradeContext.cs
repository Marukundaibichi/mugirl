using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mugirl.Platform;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// 一次终端会话（地图通讯台或商队到访据点），不序列化；每笔业务重新确认地点和入口权限。
// 本文件负责权限校验、可用库存缓存与付款；交付与白银落点见 CorporateTradeContext.Delivery.cs。
public sealed partial class CorporateTradeContext
{
    // 库存列表在界面每个 GUI 事件中都会读取；0.5 秒的实时缓存避免重复枚举全图可交易物品。
    private const double InventoryCacheSeconds = 0.5;

    private readonly Func<bool>? accessCheck;
    private List<Thing>? cachedThings;
    private int cachedSilver;
    private List<Thing>? cachedContractThings;
    private List<Pawn>? cachedPawns;
    private double refreshAt;
    private double contractRefreshAt;
    private double pawnRefreshAt;
    // 权限校验（含寻路可达）在同一引擎帧内只做一次；任何转移物品或白银的操作先 Invalidate，强制重新校验。
    private int validatedFrame = -1;
    private bool validated;

    public CorporateTradeContext(Map map, Func<bool>? accessCheck = null)
    {
        Map = map;
        this.accessCheck = accessCheck;
    }

    public CorporateTradeContext(Caravan caravan, Func<bool>? accessCheck = null)
    {
        Caravan = caravan;
        this.accessCheck = accessCheck;
    }

    public Map? Map { get; }
    public Caravan? Caravan { get; }
    public Pawn? Negotiator { get; set; }
    public string Label => Map != null ? Map.Parent.LabelCap.ToString() : Caravan?.LabelCap.ToString() ?? "-";

    public bool IsValid
    {
        get
        {
            int frame = EngineClock.FrameCount;
            if (frame != validatedFrame)
            {
                validated = EvaluateAccess();
                validatedFrame = frame;
            }

            return validated;
        }
    }

    private bool EvaluateAccess()
    {
        bool located = Map != null
            ? GameUtility.LoadedMaps?.Contains(Map) == true && Map.IsPlayerHome
            : Caravan != null && Caravan.Spawned && Caravan.IsPlayerControlled && Caravan.PawnsListForReading.Count > 0;
        return located && (accessCheck == null || accessCheck());
    }

    public IEnumerable<Thing> AvailableThings => CurrentThings() ?? Enumerable.Empty<Thing>();

    public int SilverCount => CurrentThings() != null ? cachedSilver : 0;

    // 地图上为已供电轨道贸易信标范围内与原版可交易枚举的物品；商队为背包物品。不含 Pawn。
    private List<Thing>? CurrentThings()
    {
        if (!IsValid)
        {
            return null;
        }

        double now = EngineClock.RealtimeSinceStartupAsDouble;
        if (cachedThings != null && now < refreshAt)
        {
            return cachedThings;
        }

        refreshAt = now + InventoryCacheSeconds;
        IEnumerable<Thing> source = Map != null ? TradeUtility.AllLaunchableThingsForTrade(Map)
            : CaravanInventoryUtility.AllInventoryItems(Caravan);
        var things = new List<Thing>();
        long silver = 0;
        foreach (Thing thing in source)
        {
            if (thing != null && !thing.Destroyed && thing.stackCount > 0 && thing is not Pawn)
            {
                things.Add(thing);
                if (thing.def == ThingDefOf.Silver)
                {
                    silver += thing.stackCount;
                }
            }
        }

        cachedSilver = (int)Math.Min(int.MaxValue, silver);
        return cachedThings = things;
    }

    // 与 AvailableThings 同款 0.5 秒缓存：人员页每个 GUI 事件读取，避免每次重新枚举全图可售殖民者/囚犯。
    public IEnumerable<Pawn> AvailablePawns
    {
        get
        {
            if (!IsValid)
            {
                return Enumerable.Empty<Pawn>();
            }

            double now = EngineClock.RealtimeSinceStartupAsDouble;
            if (cachedPawns != null && now < pawnRefreshAt)
            {
                return cachedPawns;
            }

            pawnRefreshAt = now + InventoryCacheSeconds;
            return cachedPawns = Map != null
                ? TradeUtility.AllSellableColonyPawns(Map, false).ToList()
                : Caravan?.PawnsListForReading.Where(p => p.IsPrisonerOfColony || p.IsSlaveOfColony).ToList() ?? new List<Pawn>();
        }
    }

    // 合同交货不受轨道商人的收购目录限制，例如原版简单餐仅允许买入。
    // 地图仍严格限于已供电的贸易信标；已有可交易容器中的物品沿用原版枚举。
    public IEnumerable<Thing> AvailableContractThings
    {
        get
        {
            if (!IsValid)
            {
                return Enumerable.Empty<Thing>();
            }

            double now = EngineClock.RealtimeSinceStartupAsDouble;
            if (cachedContractThings != null && now < contractRefreshAt)
            {
                return cachedContractThings;
            }

            contractRefreshAt = now + InventoryCacheSeconds;
            Map? map = Map;
            IEnumerable<Thing> goods = map == null ? CaravanInventoryUtility.AllInventoryItems(Caravan)
                : Building_OrbitalTradeBeacon.AllPowered(map).SelectMany(b => b.TradeableCells)
                    .Distinct().SelectMany(cell => cell.GetThingList(map)).Concat(AvailableThings);
            return cachedContractThings = goods.Where(t => t != null && !t.Destroyed && t.stackCount > 0
                && t.def.category == ThingCategory.Item && t is not Pawn).Distinct().ToList();
        }
    }

    public bool TryTake(Thing thing, int count, [NotNullWhen(true)] out Thing? taken)
        => TryTakeAvailable(thing, count, false, out taken);

    public bool TryTakeContractThing(Thing thing, int count, [NotNullWhen(true)] out Thing? taken)
        => TryTakeAvailable(thing, count, true, out taken);

    private bool TryTakeAvailable(Thing thing, int count, bool contract, [NotNullWhen(true)] out Thing? taken)
    {
        Invalidate();
        taken = null;
        if (count <= 0 || thing == null || thing.Destroyed || count > thing.stackCount
            || !(contract ? AvailableContractThings : AvailableThings).Contains(thing))
        {
            return false;
        }

        taken = thing.holdingOwner != null ? thing.holdingOwner.Take(thing, count) : thing.SplitOff(count);
        Invalidate();
        return taken != null;
    }

    public bool TrySpendSilver(int amount)
    {
        Invalidate();
        if (!IsValid || amount < 0)
        {
            return false;
        }

        List<Thing> silver = AvailableThings.Where(t => t.def == ThingDefOf.Silver).ToList();
        if (silver.Sum(t => (long)t.stackCount) < amount)
        {
            return false;
        }

        int remaining = amount;
        foreach (Thing source in silver)
        {
            if (remaining == 0)
            {
                break;
            }

            int count = Math.Min(remaining, source.stackCount);
            Thing taken = source.holdingOwner != null ? source.holdingOwner.Take(source, count) : source.SplitOff(count);
            taken.Destroy();
            remaining -= count;
        }

        Invalidate();
        return true;
    }

    /// <summary>丢弃库存缓存与本帧的权限结论；下一次读取重新校验并重新枚举。</summary>
    public void Invalidate()
    {
        cachedThings = null;
        cachedContractThings = null;
        cachedPawns = null;
        cachedSilver = 0;
        refreshAt = contractRefreshAt = pawnRefreshAt = 0d;
        validatedFrame = -1;
    }

    public float IncomingMass(Thing? thing, int count) => thing == null ? 0f : thing.GetStatValue(StatDefOf.Mass) * count;
}
