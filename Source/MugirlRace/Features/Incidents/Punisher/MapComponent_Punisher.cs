using Verse.AI;
using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

// 惩罚者（MapComponent，按类名存档）：迁徙雪牛娘被玩家全部击杀后出现，袭击殖民地一段时间后离开；死亡时的战利品飞散动画也在这里。
// 追踪记录绑定 Pawn、入场 tick 和离开状态；战利品仍由本组件持有。
public class MapComponent_Punisher : MapComponent, IThingHolder
{
    private const int ActiveDurationTicks = 30000;

    private List<TrackedPunisher> activePunishers = [];
    internal IPunisherInitialization? initialization;
    internal List<TrackedPunisher> ActivePunishers => activePunishers;

    // 正在飞散的战利品：物品放在 heldLoot（深存档），动画参数在 lootBursts。
    private ThingOwner<Thing> heldLoot;
    private List<PunisherLootBurst> lootBursts = new List<PunisherLootBurst>();

    public MapComponent_Punisher(Map map) : base(map)
    {
        heldLoot = new ThingOwner<Thing>(this, oneStackOnly: false, LookMode.Deep);
    }

    public bool HasActivePunisher
    {
        get
        {
            for (int i = 0; i < activePunishers.Count; i++)
            {
                Pawn? pawn = activePunishers[i].pawn;
                if (pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned && pawn.Map == map)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public IThingHolder ParentHolder => map;

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return heldLoot;
    }

    public static bool TrySpawnPunisher(Map? map)
    {
        MapComponent_Punisher? component = map?.GetComponent<MapComponent_Punisher>();
        if (map == null || component == null || component.HasActivePunisher)
        {
            return false;
        }

        if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entryCell, map, 0f, allowFogged: true))
        {
            return false;
        }

        Pawn? pawn = PawnGenerator.GeneratePawn(IncidentsDefOf.Mugirl_Punisher, null, map.Tile);
        if (pawn == null)
        {
            return false;
        }

        Rot4 rot = Rot4.FromAngleFlat((map.Center - entryCell).AngleFlat);
        GenSpawn.Spawn(pawn, entryCell, map, rot);
        component.EnsureRegistered(pawn);

        GameUtility.TryReceiveLetter(
            "Mugirl.Punisher.LetterLabel".Translate(),
            "Mugirl.Punisher.LetterText".Translate(),
            LetterDefOf.ThreatBig,
            new TargetInfo(pawn));
        return true;
    }

    internal void EnsureRegistered(Pawn? pawn)
    {
        if (pawn == null || pawn.def != IncidentsDefOf.Mugirl_PunisherRace
            || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map != map)
        {
            return;
        }

        int index = IndexOf(pawn);
        if (index < 0)
        {
            activePunishers.Add(new TrackedPunisher(pawn, TickUtility.CurrentGameTickOrFallback(0)));
            index = activePunishers.Count - 1;
        }

        if (activePunishers[index].leaving)
        {
            EnsureExitLord(pawn);
        }
        else
        {
            EnsureAssaultLord(pawn);
        }
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();
        // 绝大多数地图从未出现惩罚者：两个列表都为空时每 tick 只做这一次判断。
        if (activePunishers.Count == 0 && lootBursts.Count == 0 && heldLoot.Count == 0)
        {
            return;
        }

        TickLootBursts();
        if (activePunishers.Count == 0)
        {
            return;
        }

        int currentTick = TickUtility.CurrentGameTickOrFallback(0);
        for (int i = activePunishers.Count - 1; i >= 0; i--)
        {
            Pawn? pawn = activePunishers[i].pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map != map)
            {
                RemovePunisherAt(i);
                continue;
            }

            if (!activePunishers[i].leaving && currentTick - activePunishers[i].spawnTick >= ActiveDurationTicks)
            {
                activePunishers[i].leaving = true;
                EnsureExitLord(pawn);
            }
            else if (pawn.GetLord() == null)
            {
                if (activePunishers[i].leaving)
                {
                    EnsureExitLord(pawn);
                }
                else
                {
                    EnsureAssaultLord(pawn);
                }
            }
        }
    }

    public override void MapComponentDraw()
    {
        base.MapComponentDraw();
        for (int i = 0; i < lootBursts.Count; i++)
        {
            lootBursts[i].Draw();
        }
    }

    internal void AddLootBurst(PunisherLootBurst burst)
    {
        if (burst.Expired)
        {
            return;
        }

        burst.MoveLootInto(heldLoot);
        lootBursts.Add(burst);
    }

    private void TickLootBursts()
    {
        for (int i = lootBursts.Count - 1; i >= 0; i--)
        {
            PunisherLootBurst burst = lootBursts[i];
            burst.Tick();
            if (burst.Expired)
            {
                lootBursts.RemoveAt(i);
            }
        }

        // 不属于任何动画的残留物品（理论上不会出现，例如存档被外部修改）直接落在地图中央附近，避免永远不可见。
        if (lootBursts.Count == 0 && heldLoot.Count > 0)
        {
            heldLoot.TryDropAll(map.Center, map, ThingPlaceMode.Near);
        }
    }

    private static LordJob MakeAssaultLordJob(Pawn pawn)
    {
        return new LordJob_AssaultColony(
            pawn.Faction,
            canKidnap: false,
            canTimeoutOrFlee: false,
            sappers: false,
            useAvoidGridSmart: false,
            canSteal: false,
            breachers: false,
            canPickUpOpportunisticWeapons: false);
    }

    private void EnsureAssaultLord(Pawn pawn)
    {
        Lord? lord = pawn.GetLord();
        if (lord?.LordJob is LordJob_AssaultColony)
        {
            return;
        }

        lord?.RemovePawn(pawn);
        pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, false);
        LordMaker.MakeNewLord(pawn.Faction, MakeAssaultLordJob(pawn), map, Gen.YieldSingle(pawn));
    }

    private void EnsureExitLord(Pawn pawn)
    {
        Lord? lord = pawn.GetLord();
        if (lord?.LordJob is LordJob_ExitMapBest)
        {
            return;
        }

        lord?.RemovePawn(pawn);
        pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, false);
        LordMaker.MakeNewLord(
            pawn.Faction,
            new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: false, canDefendSelf: false),
            map,
            Gen.YieldSingle(pawn));
    }

    private int IndexOf(Pawn pawn)
    {
        for (int i = 0; i < activePunishers.Count; i++)
        {
            if (EqualityComparer<Pawn?>.Default.Equals(activePunishers[i].pawn, pawn))
            {
                return i;
            }
        }

        return -1;
    }

    private void RemovePunisherAt(int index) => activePunishers.RemoveAt(index);

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref activePunishers, "activePunishers", LookMode.Deep);
        Scribe_Deep.Look(ref initialization, "initialization");
        Scribe_Deep.Look(ref heldLoot, "heldLoot", this);
        Scribe_Collections.Look(ref lootBursts, "lootBursts", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            activePunishers ??= [];
            initialization?.Complete(activePunishers);
            initialization = null;
            heldLoot ??= new ThingOwner<Thing>(this, oneStackOnly: false, LookMode.Deep);
            lootBursts ??= new List<PunisherLootBurst>();
            for (int i = 0; i < lootBursts.Count; i++)
            {
                lootBursts[i].AttachToMap(map);
            }

            lootBursts.RemoveAll(burst => burst.Expired);
        }
    }
}
