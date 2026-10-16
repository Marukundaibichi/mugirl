namespace Mugirl.Features.Incidents;

// 事件投资的白银账本：正序计数、逆序消费，保持物品列表中的选堆顺序。
internal static class EventSilverLedger
{
    internal static bool HasSilver(Map map, int amount)
    {
        return CountSilver(map, amount) >= amount;
    }

    internal static bool TryTakeSilver(Map map, int amount)
    {
        if (CountSilver(map, amount) < amount)
        {
            return false;
        }

        // 与 1.6 遍历 AllThings 的顺序一致：ListerThings 按加入顺序维护每个 Def 的列表，倒序扣除同一批堆叠。
        // Destroy 会把物品移出这个列表，倒序遍历不受影响。
        int remaining = amount;
        List<Thing> things = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
        for (int i = things.Count - 1; i >= 0 && remaining > 0; i--)
        {
            Thing thing = things[i];
            if (!IsUsableSilver(thing))
            {
                continue;
            }

            int taken = Math.Min(remaining, thing.stackCount);
            remaining -= taken;
            if (taken >= thing.stackCount)
            {
                thing.Destroy();
            }
            else
            {
                thing.stackCount -= taken;
            }
        }

        return remaining <= 0;
    }

    private static int CountSilver(Map? map, int stopAt)
    {
        if (map?.listerThings == null)
        {
            return 0;
        }

        int count = 0;
        List<Thing> things = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
        for (int i = 0; i < things.Count; i++)
        {
            Thing thing = things[i];
            if (!IsUsableSilver(thing))
            {
                continue;
            }

            count += thing.stackCount;
            if (count >= stopAt)
            {
                break;
            }
        }

        return count;
    }

    private static bool IsUsableSilver(Thing? thing)
    {
        Faction? playerFaction = PlayerFactionUtility.PlayerFaction;
        return thing != null
            && playerFaction != null
            && thing.Spawned
            && !thing.Destroyed
            && thing.def == ThingDefOf.Silver
            && thing.IsInValidStorage()
            && !thing.IsForbidden(playerFaction);
    }
}
