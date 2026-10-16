using Mugirl.Features.Corporate;
using RimWorld.QuestGen;

namespace Mugirl.Features.Incidents;

// 运货员任务根节点：延迟 1 tick 后由 QuestPart_SpawnCourier 生成运货员。缺少地图、派系或入场格时不添加 QuestPart，
// 由 QuestLauncher 丢弃该任务。
public class QuestNode_Root_CourierContact : QuestNode
{
    private const int RaidDelayTicks = 1;

    protected override void RunInt()
    {
        Slate slate = QuestGen.slate;
        Quest quest = QuestGen.quest;
        Map? map = QuestLauncher.ResolveTargetMap(slate);
        if (map == null)
        {
            MugirlLog.WarningOnce("CourierRaid.Root.NoMap", "Mugirl.CourierRaid.Log.NoMap".Translate().ToString());
            return;
        }

        if (!GameUtility.TryGetFirstFactionOfDef(CorporateDefOf.Mugirl_GiantCorporation, out Faction? faction))
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

        QuestPart_Delay raidDelay = new QuestPart_Delay
        {
            inSignalEnable = quest.InitiateSignal,
            delayTicks = RaidDelayTicks
        };
        raidDelay.outSignalsCompleted.Add(spawnSignal);
        quest.AddPart(raidDelay);

        QuestPart_SpawnCourier spawnPart = new QuestPart_SpawnCourier
        {
            inSignal = spawnSignal,
            map = map,
            faction = faction,
            spawnCell = spawnCell
        };
        quest.AddPart(spawnPart);
    }

    protected override bool TestRunInt(Slate slate)
    {
        Map? map = QuestLauncher.ResolveTargetMap(slate);
        return map != null && GameUtility.TryGetFirstFactionOfDef(CorporateDefOf.Mugirl_GiantCorporation, out _);
    }
}
