using RimWorld;
using RimWorld.QuestGen;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    public class QuestNode_Root_MooGirl_CourierRaid : QuestNode
    {
        private const int RaidDelayTicks = 1;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Map map = ResolveTargetMap(slate);
            if (map == null)
            {
                MooGirlLog.Warning("MooGirl.CourierRaid.Log.NoMap".Translate().ToString());
                return;
            }

            Faction faction = Find.FactionManager.FirstFactionOfDef(MooGirlContentDefOf.MooGirl_GiantCorporations_Hostile);
            if (faction == null)
            {
                MooGirlLog.Warning("MooGirl.CourierRaid.Log.NoFaction".Translate().ToString());
                return;
            }

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 spawnCell, map, 0f))
            {
                MooGirlLog.Warning("MooGirl.CourierRaid.Log.NoSpawnCell".Translate().ToString());
                return;
            }

            string spawnSignal = QuestGenUtility.HardcodedSignalWithQuestID("CourierRaid_Spawn");

            QuestPart_Delay raidDelay = new QuestPart_Delay();
            raidDelay.inSignalEnable = quest.InitiateSignal;
            raidDelay.delayTicks = RaidDelayTicks;
            raidDelay.outSignalsCompleted.Add(spawnSignal);
            quest.AddPart(raidDelay);

            QuestPart_SpawnCourier spawnPart = new QuestPart_SpawnCourier();
            spawnPart.inSignal = spawnSignal;
            spawnPart.map = map;
            spawnPart.faction = faction;
            spawnPart.spawnCell = spawnCell;
            quest.AddPart(spawnPart);
        }

        protected override bool TestRunInt(Slate slate)
        {
            Map map = ResolveTargetMap(slate);
            return map != null && Find.FactionManager.FirstFactionOfDef(MooGirlContentDefOf.MooGirl_GiantCorporations_Hostile) != null;
        }

        private static Map ResolveTargetMap(Slate slate)
        {
            if (slate != null && slate.TryGet("map", out Map mapFromSlate) && mapFromSlate != null)
            {
                return mapFromSlate;
            }

            if (Find.AnyPlayerHomeMap != null)
            {
                return Find.AnyPlayerHomeMap;
            }

            List<Map> maps = Find.Maps;
            return maps != null && maps.Count > 0 ? maps[0] : null;
        }
    }
}
