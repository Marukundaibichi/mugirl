using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    public class QuestNode_Root_Mugirl_RunawayFarmQuest : QuestNode
    {
        private const int MinSiteDistance = 2;
        private const int MaxSiteDistance = 10;
        private const int TimeoutTicks = 12 * 60000;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Map map = ResolveTargetMap(slate);
            if (map == null)
            {
                MugirlLog.WarningOnce("RunawayFarm.Root.NoMap", "Mugirl.RunawayFarm.Log.NoMap".Translate().ToString());
                return;
            }

            if (!TryFindSiteTile(map, out PlanetTile tile))
            {
                MugirlLog.WarningOnce("RunawayFarm.Root.NoTile", "Mugirl.RunawayFarm.Log.NoTile".Translate().ToString());
                return;
            }

            Site site = QuestGen_Sites.GenerateSite(
                Gen.YieldSingle(new SitePartDefWithParams(MugirlContentDefOf.Mugirl_RunawayFarm, new SitePartParams
                {
                    points = slate.Get("points", 0f),
                    threatPoints = 0f
                })),
                tile,
                null);

            slate.Set("site", site);

            string mapGeneratedSignal = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");
            string mapRemovedSignal = QuestGenUtility.HardcodedSignalWithQuestID("site.MapRemoved");
            string successSignal = QuestGenUtility.HardcodedSignalWithQuestID("RunawayFarm_Success");
            string failSignal = QuestGenUtility.HardcodedSignalWithQuestID("RunawayFarm_Fail");
            string expiredSignal = QuestGenUtility.HardcodedSignalWithQuestID("RunawayFarm_Expired");
            string protectedTargetTag = QuestGenUtility.HardcodedTargetQuestTagWithQuestID("RunawayFarm_Protected");
            string protectedDamagedSignal = QuestGenUtility.QuestTagSignal(protectedTargetTag, "TookDamageFromPlayer");

            quest.SpawnWorldObject(site);
            quest.WorldObjectTimeout(site, TimeoutTicks, outSignalsCompleted: new List<string> { expiredSignal });
            quest.End(QuestEndOutcome.Fail, inSignal: expiredSignal, sendStandardLetter: true, playSound: true);
            quest.End(QuestEndOutcome.Fail, inSignal: mapRemovedSignal, sendStandardLetter: true, playSound: true);
            quest.End(QuestEndOutcome.Fail, inSignal: failSignal, sendStandardLetter: true, playSound: true);
            quest.End(QuestEndOutcome.Fail, inSignal: protectedDamagedSignal, sendStandardLetter: true, playSound: true);
            quest.End(QuestEndOutcome.Success, inSignal: successSignal, sendStandardLetter: false, playSound: true);

            QuestPart_SpawnRunawayFarm spawnPart = new QuestPart_SpawnRunawayFarm
            {
                inSignal = mapGeneratedSignal,
                site = site,
                protectedTargetTag = protectedTargetTag
            };
            quest.AddPart(spawnPart);

            QuestPart_RunawayFarmCompletion completionPart = new QuestPart_RunawayFarmCompletion
            {
                inSignalEnable = mapGeneratedSignal,
                inSignalDisable = mapRemovedSignal,
                site = site,
                spawnPart = spawnPart,
                failSignal = failSignal
            };
            completionPart.outSignalsCompleted.Add(successSignal);
            quest.AddPart(completionPart);

            QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
            choice.rewards.Add(new Reward_Unknown());
            quest.RewardChoice().choices.Add(choice);
        }

        protected override bool TestRunInt(Slate slate)
        {
            Map map = ResolveTargetMap(slate);
            return map != null && TryFindSiteTile(map, out _);
        }

        private static Map ResolveTargetMap(Slate slate)
        {
            if (slate != null && slate.TryGet("map", out Map mapFromSlate) && mapFromSlate != null)
            {
                return mapFromSlate;
            }

            return MugirlGameUtility.TryResolvePlayerEventMap(out Map map) ? map : null;
        }

        private static bool TryFindSiteTile(Map map, out PlanetTile tile)
        {
            tile = PlanetTile.Invalid;
            if (map == null || !map.Tile.Valid)
            {
                return false;
            }

            return TileFinder.TryFindNewSiteTile(
                out tile,
                map.Tile,
                MinSiteDistance,
                MaxSiteDistance,
                allowCaravans: false);
        }
    }
}
