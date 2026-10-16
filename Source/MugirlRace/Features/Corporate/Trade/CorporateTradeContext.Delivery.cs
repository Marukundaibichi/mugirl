using System.Linq;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// 交付：地图用空投舱，商队直接放进成员背包；白银整笔落在已验证的露天空地。
public sealed partial class CorporateTradeContext
{
    public bool CanReceive(Thing? thing)
    {
        if (!IsValid || thing == null || thing.Destroyed || thing.Spawned)
        {
            return false;
        }

        if (Map != null || thing is Pawn)
        {
            return true;
        }

        if (Caravan == null)
        {
            return false;
        }

        foreach (Pawn member in Caravan.PawnsListForReading)
        {
            ThingOwner? inventory = member.inventory?.innerContainer;
            if (inventory != null && inventory.GetCountCanAccept(thing, true) >= thing.stackCount)
            {
                return true;
            }
        }

        return false;
    }

    public bool Deliver(Thing thing)
    {
        Invalidate();
        if (!CanReceive(thing))
        {
            return false;
        }

        ThingOwner? originalOwner = thing.holdingOwner;
        originalOwner?.Remove(thing);
        try
        {
            if (Map != null)
            {
                TradeUtility.SpawnDropPod(DropCellFinder.TradeDropSpot(Map), Map, thing);
                return true;
            }

            // 通过 CanReceive 时会话有效：没有地图就一定有商队。
            if (Caravan is { } caravan)
            {
                if (thing is Pawn pawn)
                {
                    if (GameUtility.WorldPawns.Contains(pawn))
                    {
                        GameUtility.WorldPawns.RemovePawn(pawn);
                    }

                    caravan.AddPawn(pawn, true);
                    return caravan.PawnsListForReading.Contains(pawn);
                }

                // 原版 GiveThing 在没有接收者时销毁货物；这里先选择并验证实际持有者。
                foreach (Pawn member in caravan.PawnsListForReading)
                {
                    ThingOwner? inventory = member.inventory?.innerContainer;
                    if (inventory != null && inventory.GetCountCanAccept(thing, true) >= thing.stackCount
                        && inventory.TryAdd(thing, true))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MugirlLog.WarningOnce("Corporate.Delivery." + ex.GetType().Name,
                "Mugirl.Corporate.DeliveryError".Translate(ex.Message));
            // 交付已经进入空投/库存持有者时，不能再生成第二份补偿。
            if (thing.Destroyed || thing.Spawned || thing.holdingOwner != null)
            {
                return true;
            }
        }

        if (!thing.Destroyed && !thing.Spawned && thing.holdingOwner == null)
        {
            (originalOwner ?? CorporateNetwork.Current?.Vault)?.TryAdd(thing, false);
        }

        return false;
    }

    public bool DeliverSilver(int amount)
    {
        Invalidate();
        if (!IsValid || amount < 0)
        {
            return false;
        }

        if (amount == 0)
        {
            return true;
        }

        // 先确定落点，失败时不生成无持有者的白银，也不确认本笔付款。
        IntVec3 dropSpot = IntVec3.Invalid;
        if (Map != null && !TryFindSilverDropSpot(Map, out dropSpot))
        {
            return false;
        }

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
            foreach (Thing stack in stacks)
            {
                stack.Destroy();
            }

            return false;
        }

        return Map != null ? DropSilver(Map, dropSpot, stacks) : GiveSilverToCaravan(stacks);
    }

    // 单舱接管整笔白银并精确落在已验证的露天空地。原版 DropThingsNear 会分堆散落，
    // 并在无落点时退回 canRoofPunch=true，不能保证屋顶安全。
    private static bool DropSilver(Map map, IntVec3 dropSpot, List<Thing> stacks)
    {
        var info = new ActiveTransporterInfo { openDelay = 110, leaveSlag = false };
        foreach (Thing stack in stacks)
        {
            info.innerContainer.TryAdd(stack);
        }

        try
        {
            DropPodUtility.MakeDropPodAt(dropSpot, map, info);
        }
        catch (Exception ex)
        {
            // 第三方回调可能在舱已生成后抛错；已交给地图的付款不能再重试。
            bool delivered = info.innerContainer.Any(t => t.MapHeld == map);
            if (!delivered)
            {
                info.innerContainer.ClearAndDestroyContents();
            }

            MugirlLog.WarningOnce("Corporate.SilverDelivery." + ex.GetType().Name,
                "Mugirl.Corporate.DeliveryError".Translate(ex.Message));
            return delivered;
        }

        return true;
    }

    // 商队逐堆放入背包。1.6 忽略单堆失败并返回成功，失败的白银留在 Vault 中、没有任何收据。
    // 现在：全部失败则销毁并返回 false（调用方保留应付款）；部分失败则把剩余白银存为可领取的已到货收据。
    private bool GiveSilverToCaravan(List<Thing> stacks)
    {
        int delivered = 0;
        List<Thing>? failed = null;
        foreach (Thing stack in stacks)
        {
            int count = stack.stackCount;
            if (Deliver(stack))
            {
                delivered += count;
            }
            else
            {
                (failed ??= new List<Thing>()).Add(stack);
            }
        }

        if (failed == null)
        {
            return true;
        }

        if (delivered == 0)
        {
            foreach (Thing stack in failed)
            {
                if (!stack.Destroyed)
                {
                    stack.Destroy();
                }
            }

            return false;
        }

        CorporateNetwork? network = CorporateNetwork.Current;
        foreach (Thing stack in failed)
        {
            if (network != null && !stack.Destroyed && stack.holdingOwner == network.Vault)
            {
                network.SaveRecoveryOrder(stack);
            }
        }

        MugirlLog.WarningOnce("Corporate.SilverDelivery.Partial",
            "Corporate silver could only be partly delivered to the caravan; the rest is kept as a ready order in the corporate terminal.");
        return true;
    }

    // 仅在付款时扫描一次全图：先居住区外、再按距基地远近选点。全图被划为居住区或区外全不可达时，
    // 退到最近的区内露天空地；绝不放宽屋顶/战争迷雾条件。候选按 CorporateDropSpotOrder 的全序放入优先队列，
    // 与 1.6 的整图稳定排序给出相同的第一个合格格子，但只对实际检查到的少数格子做寻路判定。
    private bool TryFindSilverDropSpot(Map map, out IntVec3 result)
    {
        IntVec3 anchor = SilverDropAnchor(map);
        Area_Home home = map.areaManager.Home;
        IntVec3 size = map.Size;
        var candidates = new List<(IntVec3 Cell, long Key)>();
        int index = 0;
        for (int z = 0; z < size.z; z++)
        {
            for (int x = 0; x < size.x; x++, index++)
            {
                var cell = new IntVec3(x, 0, z);
                if (cell.Fogged(map) || cell.Roofed(map) || !cell.Standable(map))
                {
                    continue;
                }

                candidates.Add((cell, CorporateDropSpotOrder.Key(home[cell], (cell - anchor).LengthHorizontalSquared, index)));
            }
        }

        var queue = new PriorityQueue<IntVec3, long>(candidates);
        while (queue.TryDequeue(out IntVec3 cell, out _))
        {
            if (DropCellFinder.IsGoodDropSpot(cell, map, allowFogged: false, canRoofPunch: false, allowIndoors: false)
                && map.reachability.CanReachColony(cell))
            {
                result = cell;
                return true;
            }
        }

        result = IntVec3.Invalid;
        return false;
    }

    // 以本次通讯附近的居住区为参照，兼顾偏离地图中心和多片居住区的基地。
    private IntVec3 SilverDropAnchor(Map map)
    {
        IntVec3 anchor = Negotiator != null && Negotiator.Spawned && Negotiator.Map == map
            ? Negotiator.Position
            : (map.listerBuildings.allBuildingsColonist.Find(b => b.def.IsCommsConsole)
                ?? map.listerBuildings.allBuildingsColonist.Find(b => b.def.IsOrbitalTradeBeacon))?.Position ?? map.Center;
        Area_Home home = map.areaManager.Home;
        if (home.TrueCount <= 0)
        {
            return anchor;
        }

        // 与 1.6 的 OrderBy(...).First() 相同：取枚举顺序中第一个距离最小的居住区格子。
        IntVec3 nearest = anchor;
        int best = int.MaxValue;
        foreach (IntVec3 cell in home.ActiveCells)
        {
            int distance = (cell - anchor).LengthHorizontalSquared;
            if (distance < best)
            {
                best = distance;
                nearest = cell;
            }
        }

        return nearest;
    }
}
