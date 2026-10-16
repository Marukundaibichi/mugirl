using Mugirl.Features.Corporate;
using Mugirl.Features.Restraints;
using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

// 一次性运货员来访：收到信号后生成巨企运货员，随身带日记与开局束具的钥匙。按类名存档；
// CorporateIntroduction 会从旧任务读取 courier 与 map。
public class QuestPart_SpawnCourier : QuestPart
{
    private const int ConversationWaitTicks = 2 * 60000;

    public Map? map;
    public Faction? faction;
    public IntVec3 spawnCell;
    public Pawn? courier;
    public string? inSignal;

    public override void Notify_QuestSignalReceived(Signal signal)
    {
        base.Notify_QuestSignalReceived(signal);
        if (!string.IsNullOrEmpty(inSignal) && signal.tag == inSignal)
        {
            SpawnCourier();
        }
    }

    private void SpawnCourier()
    {
        if (courier != null && !courier.Destroyed)
        {
            return;
        }

        if (map == null)
        {
            MugirlLog.WarningOnce("CourierRaid.Spawn.NoMap", "Mugirl.CourierRaid.SpawnLog.NoMap".Translate().ToString());
            return;
        }

        if (faction == null)
        {
            MugirlLog.WarningOnce("CourierRaid.Spawn.NoFaction", "Mugirl.CourierRaid.SpawnLog.NoFaction".Translate().ToString());
            return;
        }

        if (!spawnCell.IsValid && !RCellFinder.TryFindRandomPawnEntryCell(out spawnCell, map, 0f))
        {
            MugirlLog.WarningOnce("CourierRaid.Spawn.NoSpawnCell", "Mugirl.CourierRaid.SpawnLog.NoSpawnCell".Translate().ToString());
            return;
        }

        // 运货员始终以巨企雇员身份入场；开战时对话逻辑会另行切断派系归属，避免影响外交。
        PawnGenerationRequest request = new PawnGenerationRequest(
            CorporateDefOf.Mugirl_GiantCorp_Courier,
            faction,
            PawnGenerationContext.NonPlayer,
            map.Tile,
            forceGenerateNewPawn: true,
            canGeneratePawnRelations: false,
            mustBeCapableOfViolence: true,
            allowFood: false);
        Pawn? generated = PawnGenerator.GeneratePawn(request);
        if (generated == null || generated.inventory?.innerContainer == null || generated.mindState == null)
        {
            MugirlLog.WarningOnce(
                "CourierGenerationFailed",
                "Courier raid could not generate a usable courier pawn; skipping courier spawn.");
            GeneratedPawnUtility.Discard(generated);
            courier = null;
            return;
        }

        courier = generated;
        AddToInventory(generated, IncidentsDefOf.Mugirl_CourierDiary, 1);
        AddOpeningCrashKeys(generated);

        GenSpawn.Spawn(generated, spawnCell, map);
        if (!generated.Spawned)
        {
            MugirlLog.WarningOnce(
                "CourierSpawnFailed",
                "Courier raid generated a courier but failed to spawn it on the target map.");
            GeneratedPawnUtility.Discard(generated);
            courier = null;
            return;
        }

        LordMaker.MakeNewLord(
            generated.Faction,
            new LordJob_WaitForDurationThenExit(spawnCell, ConversationWaitTicks),
            map,
            new List<Pawn> { generated });

        CorporateIntroduction.Current?.RememberCourier(generated, map);

        GameUtility.TryReceiveLetter(
            "Mugirl.CourierContactLetterLabel".Translate(),
            "Mugirl.CourierContactLetterText".Translate(),
            LetterDefOf.NeutralEvent,
            generated);
        GameUtility.TrySignalForceNormalSpeedShort();
    }

    private static void AddToInventory(Pawn pawn, ThingDef? thingDef, int count)
    {
        ThingOwner<Thing>? inventory = pawn.inventory?.innerContainer;
        if (inventory == null || thingDef == null || count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Thing? thing = ThingMaker.MakeThing(thingDef);
            if (thing == null)
            {
                return;
            }

            if (!inventory.TryAdd(thing))
            {
                thing.Destroy();
            }
        }
    }

    private static void AddOpeningCrashKeys(Pawn courier)
    {
        List<Pawn>? pawns = FindOpeningCrashPawns();
        if (pawns == null || pawns.Count == 0)
        {
            MugirlLog.WarningOnce("CourierKeys.MissingOpeningPawns", "Mugirl.CourierRaid.Log.MissingOpeningPawns".Translate().ToString());
            return;
        }

        AddOpeningCrashKeys(courier, pawns);
    }

    private static List<Pawn>? FindOpeningCrashPawns()
    {
        List<Pawn>? pawns = GameUtility.GameComponent<GameComponent_Storyline>()?.OpeningCrash.pawns;
        if ((pawns == null || pawns.Count == 0) && GameUtility.TryGetQuestsListForReading(out List<Quest>? quests))
        {
            // 专用目标列表为空时，从仍存在的开局任务按原顺序解析目标。
            for (int i = 0; i < quests.Count && (pawns == null || pawns.Count == 0); i++)
            {
                if (quests[i]?.root != IncidentsDefOf.Mugirl_SlaveOpeningPodCrash)
                {
                    continue;
                }

                List<QuestPart> parts = quests[i].PartsListForReading;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j] is QuestPart_JoinOnRescue rescuePart)
                    {
                        pawns = rescuePart.pawns;
                        break;
                    }
                }
            }
        }

        return pawns;
    }

    private static void AddOpeningCrashKeys(Pawn courier, List<Pawn> pawns)
    {
        for (int i = 0; i < pawns.Count; i++)
        {
            List<Apparel>? apparel = pawns[i]?.apparel?.WornApparel;
            if (apparel == null)
            {
                continue;
            }

            for (int j = 0; j < apparel.Count; j++)
            {
                if (apparel[j] is not RestraintApparel locked || !locked.IsLockedRestraint() || locked.LockCount <= 0)
                {
                    continue;
                }

                ThingDef keyDef = locked.Restraint?.key ?? RestraintsDefOf.Mugirl_SlaveApparelKey_Medieval;
                AddToInventory(courier, keyDef, locked.LockCount);
            }
        }
    }

    // 交出物品前按当前开局对象重新放钥匙。
    internal static void RefreshOpeningCrashKeys(Pawn? courier)
    {
        List<Pawn>? pawns = FindOpeningCrashPawns();
        ThingOwner<Thing>? inventory = courier?.inventory?.innerContainer;
        if (pawns == null || pawns.Count == 0)
        {
            MugirlLog.WarningOnce("CourierKeys.MissingOpeningPawns", "Mugirl.CourierRaid.Log.MissingOpeningPawns".Translate().ToString());
            return;
        }

        if (courier == null || inventory == null)
        {
            return;
        }

        // 按当前目标重建钥匙堆叠，先逆序移除已有的钥匙。
        for (int i = inventory.Count - 1; i >= 0; i--)
        {
            if (inventory[i] is not ThingWithComps thing || thing.GetComp<CompRestraintKey>() == null)
            {
                continue;
            }

            inventory.Remove(thing);
            thing.Destroy();
        }

        AddOpeningCrashKeys(courier, pawns);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref map, "map");
        Scribe_References.Look(ref faction, "faction");
        Scribe_Values.Look(ref spawnCell, "spawnCell", IntVec3.Invalid);
        Scribe_References.Look(ref courier, "courier");
        Scribe_Values.Look(ref inSignal, "inSignal");
    }

    public override void AssignDebugData()
    {
        base.AssignDebugData();
        inSignal = "SpawnCourier";
    }
}
