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
        private float refreshAt;
        private float contractRefreshAt;
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

        public IEnumerable<Pawn> AvailablePawns => !IsValid ? Enumerable.Empty<Pawn>()
            : Map != null ? TradeUtility.AllSellableColonyPawns(Map, false).ToList()
            : Caravan.PawnsListForReading.Where(p => p.IsPrisonerOfColony || p.IsSlaveOfColony).ToList();
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

        public void Invalidate() { cachedThings = null; cachedContractThings = null; refreshAt = contractRefreshAt = 0f; }

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
                // 一组空投一次接管全部白银，避免分堆结算产生半笔支付。
                DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(Map), Map, stacks, 110,
                    canInstaDropDuringInit: false, leaveSlag: false, forbid: false);
                return true;
            }
            foreach (Thing stack in stacks) Deliver(stack);
            return true;
        }

        public float IncomingMass(Thing thing, int count) => thing == null ? 0f : thing.GetStatValue(StatDefOf.Mass) * count;
    }
}
