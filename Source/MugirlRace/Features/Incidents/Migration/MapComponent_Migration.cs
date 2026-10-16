using Mugirl.Features.Grazing;
using Mugirl.Features.Roping;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

// 迁徙的状态与行为（MapComponent，按类名存档）：强制吃草、停留计时、受击后离开；玩家在时限内亲手击杀全部迁徙者时召唤惩罚者。
// 每位成员与其日程共同持久化；地图组件负责 Pawn 行为，状态聚合负责记录与计时事实。
public class MapComponent_Migration : MapComponent
{
    private bool migrationRead;

    private const int ForcedGrazeIntervalTicks = 300;
    private const int FirstGrazeMinDelayTicks = 30;
    private const int PunisherKillWindowTicks = 15000;
    private const float ForcedGrazeSearchRadius = 30f;
    private const float FoodSatisfiedTolerance = 0.02f;

    internal MigrationState State { get; } = new();
    internal IMigrationInitialization? initialization;

    public bool HasActiveMigration => State.members != null && State.members.Count > 0;

    public MapComponent_Migration(Map map) : base(map)
    {
    }

    public void RegisterMigration(List<Pawn> pawns, IntVec3 exit, int stayTicks)
    {
        State.CompactForRegistration();
        int currentTick = TickUtility.CurrentGameTickOrFallback(0);
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn != null && !State.Contains(pawn))
            {
                State.members.Add(new MigrationMember(pawn, currentTick + Rand.RangeInclusive(FirstGrazeMinDelayTicks, ForcedGrazeIntervalTicks)));
            }
        }

        State.exit = exit;
        State.departureRemaining = stayTicks;
        State.initialCount = State.members.Count;
        State.playerKillCount = 0;
        State.playerKillIds.Clear();
        State.killWindowRemaining = PunisherKillWindowTicks;
        State.outcomeFailed = State.initialCount == 0;
        State.departing = false;
    }

    internal static void NotifyMigrationPawnAttacked(Pawn? pawn)
    {
        if (pawn == null)
        {
            return;
        }

        pawn.MapHeld?.GetComponent<MapComponent_Migration>()?.NotifyPawnAttacked(pawn);
    }

    private void NotifyPawnAttacked(Pawn pawn)
    {
        if (!State.Contains(pawn))
        {
            return;
        }

        BeginDeparture(LocomotionUrgency.Sprint, preferBestExit: true);
    }

    internal static void NotifyMigrationPawnKilled(Pawn? pawn, DamageInfo? dinfo)
    {
        MapComponent_Migration? component = pawn?.MapHeld?.GetComponent<MapComponent_Migration>();
        if (component == null || pawn == null || !component.State.Contains(pawn))
        {
            return;
        }

        if (WasDirectlyKilledByPlayer(dinfo)
            && !component.State.playerKillIds.Contains(pawn.thingIDNumber))
        {
            component.State.playerKillIds.Add(pawn.thingIDNumber);
        }
    }

    private static bool WasDirectlyKilledByPlayer(DamageInfo? dinfo)
    {
        if (!dinfo.HasValue)
        {
            return false;
        }

        DamageInfo fatalDamage = dinfo.Value;
        return fatalDamage.Def != null
            && fatalDamage.Category != DamageInfo.SourceCategory.Collapse
            && fatalDamage.Instigator != null
            && PlayerFactionUtility.IsPlayerFaction(fatalDamage.Instigator.Faction);
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();
        if (State.members.Count == 0)
        {
            State.departureRemaining = 0;
            return;
        }

        int ticksGame = TickUtility.CurrentGameTickOrFallback(0);
        for (int i = State.members.Count - 1; i >= 0; i--)
        {
            Pawn? pawn = State.members[i].pawn;
            if (pawn != null && pawn.Dead)
            {
                bool killedByPlayer = State.playerKillIds.Contains(pawn.thingIDNumber);
                State.playerKillIds.Remove(pawn.thingIDNumber);
                if (!State.outcomeFailed && State.killWindowRemaining > 0 && killedByPlayer)
                {
                    State.playerKillCount++;
                }
                else
                {
                    State.outcomeFailed = true;
                }

                RemoveMigrationPawnAt(i);
                continue;
            }

            if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map != map)
            {
                State.outcomeFailed = true;
                RemoveMigrationPawnAt(i);
                continue;
            }

            if (PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
            {
                State.outcomeFailed = true;
                EventPawnTags.ClearTemporaryEventTags(pawn);
                RemoveMigrationPawnAt(i);
                continue;
            }

            if (!State.departing)
            {
                TryForcedGraze(pawn, i, ticksGame);
            }
        }

        if (State.members.Count == 0)
        {
            ResolveMigrationOutcome();
            State.departureRemaining = 0;
            return;
        }

        if (State.killWindowRemaining > 0)
        {
            State.killWindowRemaining--;
            if (State.killWindowRemaining <= 0)
            {
                State.outcomeFailed = true;
                if (State.departing)
                {
                    AbandonMigrationTracking();
                    return;
                }
            }
        }

        if (State.departing)
        {
            return;
        }

        State.departureRemaining--;
        if (State.departureRemaining > 0)
        {
            return;
        }

        BeginDeparture(LocomotionUrgency.Walk, preferBestExit: false);
    }

    // 每个成员保留独立的吃草日程，按原顺序消费到期重试。
    private void TryForcedGraze(Pawn pawn, int index, int ticksGame)
    {
        if (pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.Map != map)
        {
            return;
        }

        if (!ShouldGrazeForFood(pawn))
        {
            return;
        }

        if (index < 0 || index >= State.members.Count || ticksGame < State.members[index].grazeAtTick)
        {
            return;
        }

        State.members[index].grazeAtTick = ticksGame + ForcedGrazeIntervalTicks;
        // 仍然消费本次重试间隔；被牵引/正在进食等情况不进入植物搜索和可达性检查。
        if (!CanStartGrazeJob(pawn))
        {
            return;
        }

        Thing? plant = FindGrazePlant(pawn);
        if (plant == null)
        {
            return;
        }

        TryStartGrazeJob(pawn, plant);
    }

    private static Thing? FindGrazePlant(Pawn pawn)
    {
        TraverseParms traverseParms = TraverseParms.For(pawn, DangerUtility.NormalMaxDanger(pawn));
        return GenClosest.ClosestThingReachable(
            pawn.Position,
            pawn.Map,
            ThingRequest.ForGroup(ThingRequestGroup.Plant),
            PathEndMode.Touch,
            traverseParms,
            ForcedGrazeSearchRadius,
            thing => IsForcedGrazePlant(pawn, thing));
    }

    private static bool IsForcedGrazePlant(Pawn pawn, Thing thing)
    {
        return thing is Plant plant
            && !plant.sown
            && GrazeUtility.IsMapPlantFood(plant)
            && !thing.IsForbidden(pawn)
            && pawn.CanReserve(thing, 1, 1, null, ignoreOtherReservations: true);
    }

    private static void TryStartGrazeJob(Pawn pawn, Thing plant)
    {
        if (!plant.Spawned || plant.Destroyed || !CanStartGrazeJob(pawn))
        {
            return;
        }

        if (!pawn.CanReserve(plant, 1, 1, null, ignoreOtherReservations: false))
        {
            return;
        }

        Job job = JobMaker.MakeJob(JobDefOf.Ingest, plant);
        job.count = 1;
        job.overeat = true;
        pawn.jobs.StartJob(job, JobCondition.InterruptForced, tag: JobTag.Misc);
    }

    private static bool CanStartGrazeJob(Pawn pawn)
    {
        if (pawn.jobs == null)
        {
            return false;
        }

        if (RopingService.HasAnyRope(pawn) || pawn.Drafted || pawn.InMentalState)
        {
            return false;
        }

        if (!ShouldGrazeForFood(pawn))
        {
            return false;
        }

        if (pawn.Map?.physicalInteractionReservationManager?.IsReserved(pawn) == true)
        {
            return false;
        }

        Job? curJob = pawn.CurJob;
        if (curJob?.def == JobDefOf.Ingest)
        {
            return false;
        }

        if (curJob != null && !curJob.def.isIdle && curJob.def != JobDefOf.GotoWander)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldGrazeForFood(Pawn pawn)
    {
        Need_Food? food = pawn.needs?.food;
        return food != null && food.CurLevel < food.MaxLevel - FoodSatisfiedTolerance;
    }

    private void RemoveMigrationPawnAt(int index) => State.members.RemoveAt(index);

    private void BeginDeparture(LocomotionUrgency urgency, bool preferBestExit)
    {
        if (State.departing)
        {
            if (!preferBestExit || !NeedsBestExitReroute())
            {
                return;
            }
        }

        State.departing = true;
        List<Pawn> departing = new List<Pawn>();
        for (int i = 0; i < State.members.Count; i++)
        {
            Pawn? pawn = State.members[i].pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
            {
                continue;
            }

            if (pawn.Map != map)
            {
                continue;
            }

            pawn.GetLord()?.RemovePawn(pawn);
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, false);
            departing.Add(pawn);
        }

        if (departing.Count > 0)
        {
            LordJob lordJob = !preferBestExit && State.exit.IsValid
                ? new LordJob_ExitMapNear(State.exit, urgency)
                : new LordJob_ExitMapBest(urgency, canDig: false, canDefendSelf: false);
            LordMaker.MakeNewLord(null, lordJob, map, departing);
        }

        State.departureRemaining = 0;
        if (State.outcomeFailed || State.killWindowRemaining <= 0)
        {
            AbandonMigrationTracking();
        }
    }

    // 已经在离开、但仍有迁徙者没有改走最近出口（例如离开途中受击）时需要重新编组。
    private bool NeedsBestExitReroute()
    {
        for (int i = 0; i < State.members.Count; i++)
        {
            Pawn? pawn = State.members[i].pawn;
            if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.Map == map
                && pawn.GetLord()?.LordJob is not LordJob_ExitMapBest)
            {
                return true;
            }
        }

        return false;
    }

    private void AbandonMigrationTracking()
    {
        State.members.Clear();
        State.playerKillIds.Clear();
        State.departureRemaining = 0;
        State.exit = IntVec3.Invalid;
        ResetMigrationOutcome();
    }

    private void ResolveMigrationOutcome()
    {
        bool summonPunisher = !State.outcomeFailed
            && State.killWindowRemaining > 0
            && State.initialCount > 0
            && State.playerKillCount == State.initialCount;

        ResetMigrationOutcome();
        if (summonPunisher)
        {
            MapComponent_Punisher.TrySpawnPunisher(map);
        }
    }

    private void ResetMigrationOutcome()
    {
        State.killWindowRemaining = 0;
        State.initialCount = 0;
        State.playerKillCount = 0;
        State.playerKillIds.Clear();
        State.outcomeFailed = false;
        State.departing = false;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        EventStateScribe.Look(State, "migration", ref migrationRead);
        Scribe_Deep.Look(ref initialization, "initialization");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            State.NormalizeCollections();
            initialization?.Complete(State);
            initialization = null;
        }
    }
}
