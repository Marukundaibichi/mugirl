using RimWorld;
using RimWorld.QuestGen;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    public class QuestNode_Root_Mugirl_CourierRaid : QuestNode
    {
        private const int RaidDelayTicks = 1;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Map map = ResolveTargetMap(slate);
            if (map == null)
            {
                MugirlLog.WarningOnce("CourierRaid.Root.NoMap", "Mugirl.CourierRaid.Log.NoMap".Translate().ToString());
                return;
            }

            if (!MugirlGameUtility.TryGetFirstFactionOfDef(MugirlContentDefOf.Mugirl_GiantCorporations_Hostile, out Faction faction))
            {
                MugirlLog.WarningOnce("CourierRaid.Root.NoFaction", "Mugirl.CourierRaid.Log.NoFaction".Translate().ToString());
                return;
            }

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 spawnCell, map, 0f))
            {
                MugirlLog.WarningOnce("CourierRaid.Root.NoSpawnCell", "Mugirl.CourierRaid.Log.NoSpawnCell".Translate().ToString());
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
            return map != null && MugirlGameUtility.TryGetFirstFactionOfDef(MugirlContentDefOf.Mugirl_GiantCorporations_Hostile, out _);
        }

        private static Map ResolveTargetMap(Slate slate)
        {
            if (slate != null && slate.TryGet("map", out Map mapFromSlate) && mapFromSlate != null)
            {
                return mapFromSlate;
            }

            return MugirlGameUtility.TryResolvePlayerEventMap(out Map map) ? map : null;
        }
    }
}
