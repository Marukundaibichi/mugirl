using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // 不序列化 UI 会话；每笔业务重新确认地点和入口权限。
    public sealed class CorporateTradeContext
    {
        private readonly Func<bool> accessCheck;
        private List<Thing> cachedThings;
        private List<Thing> cachedContractThings;
        private List<Pawn> cachedPawns;
        private float refreshAt;
        private float contractRefreshAt;
        private float pawnRefreshAt;
        public Map Map { get; }
        public Caravan Caravan { get; }
        public Pawn Negotiator { get; set; }
        public CorporateTradeContext(Map map, Func<bool> accessCheck = null) { Map = map; this.accessCheck = accessCheck; }
        public CorporateTradeContext(Caravan caravan, Func<bool> accessCheck = null) { Caravan = caravan; this.accessCheck = accessCheck; }
        public string Label => Map != null ? Map.Parent.LabelCap.ToString() : Caravan?.LabelCap.ToString() ?? "-";
        public bool IsValid => (Map != null && MugirlGameUtility.LoadedMaps.Contains(Map) == true && Map.IsPlayerHome
            || Caravan != null && Caravan.Spawned && Caravan.IsPlayerControlled && Caravan.PawnsListForReading.Count > 0)
            && (accessCheck == null || accessCheck());

        public IEnumerable<Thing> AvailableThings
        {
            get
            {
                if (!IsValid) return Enumerable.Empty<Thing>();
                if (cachedThings != null && Time.realtimeSinceStartup < refreshAt) return cachedThings;
                refreshAt = Time.realtimeSinceStartup + 0.5f;
                return cachedThings = (Map != null ? TradeUtility.AllLaunchableThingsForTrade(Map)
                    : CaravanInventoryUtility.AllInventoryItems(Caravan))
                    .Where(t => t != null && !t.Destroyed && t.stackCount > 0 && !(t is Pawn)).ToList();
            }
        }

        // 与 AvailableThings 同款 0.5s 缓存：People 页每 GUI 事件读取，
        // 避免每次重新枚举全图可售殖民者/囚犯。
        public IEnumerable<Pawn> AvailablePawns
        {
            get
            {
                if (!IsValid) return Enumerable.Empty<Pawn>();
                if (cachedPawns != null && Time.realtimeSinceStartup < pawnRefreshAt) return cachedPawns;
                pawnRefreshAt = Time.realtimeSinceStartup + 0.5f;
                return cachedPawns = Map != null
                    ? TradeUtility.AllSellableColonyPawns(Map, false).ToList()
                    : Caravan.PawnsListForReading.Where(p => p.IsPrisonerOfColony || p.IsSlaveOfColony).ToList();
            }
        }
        public int SilverCount => (int)Math.Min(int.MaxValue, AvailableThings.Where(t => t.def == ThingDefOf.Silver).Sum(t => (long)t.stackCount));

        // 合同交货不受轨道商人的收购目录限制，例如原版简单餐仅允许买入。
        // 地图仍严格限于已供电的贸易信标；已有可交易容器中的物品沿用原版枚举。
        public IEnumerable<Thing> AvailableContractThings
        {
            get
            {
                if (!IsValid) return Enumerable.Empty<Thing>();
                if (cachedContractThings != null && Time.realtimeSinceStartup < contractRefreshAt) return cachedContractThings;
                contractRefreshAt = Time.realtimeSinceStartup + 0.5f;
                IEnumerable<Thing> goods = Map == null ? CaravanInventoryUtility.AllInventoryItems(Caravan)
                    : Building_OrbitalTradeBeacon.AllPowered(Map).SelectMany(b => b.TradeableCells)
                        .Distinct().SelectMany(cell => cell.GetThingList(Map)).Concat(AvailableThings);
                return cachedContractThings = goods.Where(t => t != null && !t.Destroyed && t.stackCount > 0
                    && t.def.category == ThingCategory.Item && !(t is Pawn)).Distinct().ToList();
            }
        }

        public bool TryTake(Thing thing, int count, out Thing taken)
            => TryTakeAvailable(thing, count, false, out taken);

        public bool TryTakeContractThing(Thing thing, int count, out Thing taken)
            => TryTakeAvailable(thing, count, true, out taken);

        private bool TryTakeAvailable(Thing thing, int count, bool contract, out Thing taken)
        {
            Invalidate();
            taken = null;
            if (count <= 0 || thing == null || thing.Destroyed || count > thing.stackCount
                || !(contract ? AvailableContractThings : AvailableThings).Contains(thing)) return false;
            taken = thing.holdingOwner != null ? thing.holdingOwner.Take(thing, count) : thing.SplitOff(count);
            Invalidate();
            return taken != null;
        }

        public bool TrySpendSilver(int amount)
        {
            Invalidate();
            if (!IsValid || amount < 0) return false;
            List<Thing> silver = AvailableThings.Where(t => t.def == ThingDefOf.Silver).ToList();
            if (silver.Sum(t => (long)t.stackCount) < amount) return false;
            int remaining = amount;
            foreach (Thing source in silver)
            {
                if (remaining == 0) break;
                int count = Math.Min(remaining, source.stackCount);
                Thing taken = source.holdingOwner != null ? source.holdingOwner.Take(source, count) : source.SplitOff(count);
                taken.Destroy();
                remaining -= count;
            }
            Invalidate();
            return true;
        }

        public void Invalidate() { cachedThings = null; cachedContractThings = null; cachedPawns = null; refreshAt = contractRefreshAt = pawnRefreshAt = 0f; }

        public bool CanReceive(Thing thing)
        {
            if (!IsValid || thing == null || thing.Destroyed || thing.Spawned) return false;
            if (Map != null || thing is Pawn) return true;
            return Caravan.PawnsListForReading.Any(p => p.inventory?.innerContainer != null
                && p.inventory.innerContainer.GetCountCanAccept(thing, true) >= thing.stackCount);
        }

        public bool Deliver(Thing thing)
        {
            Invalidate();
            if (!CanReceive(thing)) return false;
            ThingOwner originalOwner = thing.holdingOwner;
            originalOwner?.Remove(thing);
            try
            {
                if (Map != null)
                {
                    TradeUtility.SpawnDropPod(DropCellFinder.TradeDropSpot(Map), Map, thing);
                    return true;
                }
                if (thing is Pawn pawn)
                {
                    if (MugirlGameUtility.WorldPawns.Contains(pawn)) MugirlGameUtility.WorldPawns.RemovePawn(pawn);
                    Caravan.AddPawn(pawn, true);
                    return Caravan.PawnsListForReading.Contains(pawn);
                }
                // 原版 GiveThing 在没有接收者时销毁货物；这里先选择并验证实际持有者。
                foreach (Pawn member in Caravan.PawnsListForReading)
                {
                    ThingOwner inventory = member.inventory?.innerContainer;
                    if (inventory != null && inventory.GetCountCanAccept(thing, true) >= thing.stackCount
                        && inventory.TryAdd(thing, true)) return true;
                }
            }
            catch (Exception ex)
            {
                MugirlLog.WarningOnce("Corporate.Delivery." + ex.GetType().Name,
                    "Mugirl.Corporate.DeliveryError".Translate(ex.Message));
                // 交付已经进入空投/库存持有者时，不能再生成第二份补偿。
                if (thing.Destroyed || thing.Spawned || thing.holdingOwner != null) return true;
            }
            if (!thing.Destroyed && !thing.Spawned && thing.holdingOwner == null)
                (originalOwner ?? CorporateNetwork.Current?.Vault)?.TryAdd(thing, false);
            return false;
        }

        public bool DeliverSilver(int amount)
        {
            Invalidate();
            if (!IsValid || amount < 0) return false;
            if (amount == 0) return true;
            // 先确定落点，失败时不生成无持有者的白银，也不确认本笔付款。
            IntVec3 dropSpot = IntVec3.Invalid;
            if (Map != null && !TryFindSilverDropSpot(out dropSpot)) return false;
            List<Thing> stacks = new List<Thing>();
            int left = amount;
            while (left > 0)
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Math.Min(left, ThingDefOf.Silver.stackLimit);
                stacks.Add(silver);
                left -= silver.stackCount;
            }
            if (stacks.Any(t => !CanReceive(t)))
            {
                foreach (Thing stack in stacks) stack.Destroy();
                return false;
            }
            if (Map != null)
            {
                // 单舱接管整笔白银并精确落在已验证的露天空地。原版 DropThingsNear
                // 会分堆散落，并在无落点时退回 canRoofPunch=true，不能保证屋顶安全。
                var info = new ActiveTransporterInfo { openDelay = 110, leaveSlag = false };
                foreach (Thing stack in stacks) info.innerContainer.TryAdd(stack);
                try { DropPodUtility.MakeDropPodAt(dropSpot, Map, info); }
                catch (Exception ex)
                {
                    // 第三方回调可能在舱已生成后抛错；已交给地图的付款不能再重试。
                    bool delivered = info.innerContainer.Any(t => t.MapHeld == Map);
                    if (!delivered) info.innerContainer.ClearAndDestroyContents();
                    MugirlLog.WarningOnce("Corporate.SilverDelivery." + ex.GetType().Name,
                        "Mugirl.Corporate.DeliveryError".Translate(ex.Message));
                    return delivered;
                }
                return true;
            }
            foreach (Thing stack in stacks) Deliver(stack);
            return true;
        }

        private bool TryFindSilverDropSpot(out IntVec3 result)
        {
            Map map = Map;
            Area_Home home = map.areaManager.Home;
            IntVec3 anchor = Negotiator != null && Negotiator.Spawned && Negotiator.Map == map
                ? Negotiator.Position
                : (map.listerBuildings.allBuildingsColonist.FirstOrDefault(b => b.def.IsCommsConsole)
                    ?? map.listerBuildings.allBuildingsColonist.FirstOrDefault(b => b.def.IsOrbitalTradeBeacon))?.Position ?? map.Center;
            // 以本次通讯附近的居住区为参照，兼顾偏离地图中心和多片居住区的基地。
            if (home.TrueCount > 0)
            {
                IntVec3 origin = anchor;
                anchor = home.ActiveCells.OrderBy(c => (c - origin).LengthHorizontalSquared).First();
            }
            // 仅在付款时扫描；先区外、再按距基地远近选点。全图被划为居住区或
            // 区外全不可达时，退到最近的区内露天空地；绝不放宽屋顶/战争迷雾条件。
            foreach (IntVec3 cell in map.AllCells
                .Where(c => !c.Fogged(map) && !c.Roofed(map) && c.Standable(map))
                .OrderBy(c => home[c] ? 1 : 0).ThenBy(c => (c - anchor).LengthHorizontalSquared))
            {
                if (!DropCellFinder.IsGoodDropSpot(cell, map, allowFogged: false, canRoofPunch: false, allowIndoors: false)
                    || !map.reachability.CanReachColony(cell)) continue;
                result = cell;
                return true;
            }
            result = IntVec3.Invalid;
            return false;
        }

        public float IncomingMass(Thing thing, int count) => thing == null ? 0f : thing.GetStatValue(StatDefOf.Mass) * count;
    }
}
