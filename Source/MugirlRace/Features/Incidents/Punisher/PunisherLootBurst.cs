namespace Mugirl.Features.Incidents;

// 惩罚者死亡后战利品（雪牛尸体与材料）的飞散动画，持续 PunisherArcMath.DurationTicks 个游戏 tick 后落地。
// 动画期间物品存放在 MapComponent_Punisher 的 ThingOwner 中并随存档保存；本类只保存动画参数与物品引用
// （1.6 不保存，在动画期间存档会丢失全部战利品）。
internal sealed class PunisherLootBurst : IExposable
{
    private const int CorpseTargetRadius = 2;
    private const int ItemTargetRadius = 8;

    private Map? map;
    private IntVec3 originCell;
    private int startTick;
    private List<DebrisArc> arcs = new List<DebrisArc>();

    // Scribe 读档时用无参构造函数创建。
    public PunisherLootBurst()
    {
    }

    // 起点为死亡格中心（DeathActionWorker 的特效也从这里播放）；存 IntVec3 而不是 Vector3，读写存档不经过浮点文本。
    internal PunisherLootBurst(Map map, IntVec3 originCell, List<Thing> payloads)
    {
        this.map = map;
        this.originCell = originCell;
        startTick = TickUtility.CurrentGameTickOrFallback(0);
        BuildArcs(originCell, payloads);
        if (arcs.Count == 0)
        {
            Expired = true;
        }
    }

    internal bool Expired { get; private set; }

    // 读档后接回地图，并丢弃引用已失效的弧线。
    internal void AttachToMap(Map owner)
    {
        map = owner;
        arcs ??= new List<DebrisArc>();
        arcs.RemoveAll(arc => arc.thing == null);
        if (arcs.Count == 0)
        {
            Expired = true;
        }
    }

    // 把战利品放进存档可见的容器；放不进去的物品留在弧线中，落地时照常放置。
    internal void MoveLootInto(ThingOwner holder)
    {
        for (int i = 0; i < arcs.Count; i++)
        {
            Thing? thing = arcs[i].thing;
            if (thing != null && thing.holdingOwner == null && !thing.Spawned)
            {
                holder.TryAdd(thing, canMergeWithExistingStacks: false);
            }
        }
    }

    internal void Tick()
    {
        if (Expired || map == null)
        {
            Expired = true;
            return;
        }

        int age = TickUtility.CurrentGameTickOrFallback(startTick) - startTick;
        if (age >= PunisherArcMath.DurationTicks)
        {
            FinalizeDrops();
            Expired = true;
        }
    }

    internal void Draw()
    {
        if (Expired || !GameUtility.IsCurrentMap(map))
        {
            return;
        }

        int age = TickUtility.CurrentGameTickOrFallback(startTick) - startTick;
        float t = PunisherArcMath.Progress(age);
        Vector3 start = originCell.ToVector3Shifted().ToNumerics();
        float baseAltitude = AltitudeLayer.Pawn.AltitudeFor();
        for (int i = 0; i < arcs.Count; i++)
        {
            DebrisArc arc = arcs[i];
            Thing? thing = arc.thing;
            if (thing == null)
            {
                continue;
            }

            Vector3 target = arc.targetCell.ToVector3Shifted().ToNumerics();
            UVector3 drawPos = PunisherArcMath.Evaluate(start, target, t, arc.sideSwing, arc.launchHeight, baseAltitude).ToUnity();
            if (thing is Corpse)
            {
                thing.DrawNowAt(drawPos);
            }
            else
            {
                float extraRotation = UnityMathf.Lerp(0f, arc.spin, t);
                thing.Graphic?.Draw(drawPos, Rot4.North, thing, extraRotation);
            }
        }
    }

    internal void FinalizeDrops()
    {
        if (map == null)
        {
            return;
        }

        for (int i = 0; i < arcs.Count; i++)
        {
            DebrisArc arc = arcs[i];
            Thing? thing = arc.thing;
            if (thing == null || thing.Destroyed)
            {
                continue;
            }

            thing.holdingOwner?.Remove(thing);
            GenPlace.TryPlaceThing(
                thing,
                arc.targetCell,
                map,
                ThingPlaceMode.Near,
                rot: thing.def.rotatable ? Rot4.Random : Rot4.North);
        }
    }

    private void BuildArcs(IntVec3 centerCell, List<Thing> payloads)
    {
        if (map == null)
        {
            return;
        }

        Map targetMap = map;
        for (int i = 0; i < payloads.Count; i++)
        {
            Thing? thing = payloads[i];
            if (thing == null)
            {
                continue;
            }

            bool corpseLike = thing is Corpse;
            IntVec3 targetCell;
            if (corpseLike)
            {
                bool foundCell = CellFinder.TryFindRandomCellNear(
                    centerCell,
                    targetMap,
                    CorpseTargetRadius,
                    cell => cell.InBounds(targetMap) && cell.Walkable(targetMap) && cell != centerCell,
                    out targetCell);
                if (!foundCell)
                {
                    targetCell = centerCell.ClampInsideMap(targetMap);
                }
            }
            else
            {
                targetCell = CellFinder.RandomClosewalkCellNear(centerCell, targetMap, ItemTargetRadius);
            }

            float launchHeight = corpseLike ? Rand.Range(0.95f, 1.35f) : Rand.Range(0.65f, 1.08f);
            float sideSwing = corpseLike ? Rand.Range(-0.42f, 0.42f) : Rand.Range(-0.26f, 0.26f);
            float spin = corpseLike ? 0f : Rand.Range(-240f, 240f);
            arcs.Add(new DebrisArc(thing, targetCell, launchHeight, sideSwing, spin));
        }
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref originCell, "originCell");
        Scribe_Values.Look(ref startTick, "startTick", 0);
        Scribe_Collections.Look(ref arcs, "arcs", LookMode.Deep);
    }

    private sealed class DebrisArc : IExposable
    {
        internal Thing? thing;
        internal IntVec3 targetCell;
        internal float launchHeight;
        internal float sideSwing;
        internal float spin;

        // Scribe 读档时用无参构造函数创建。
        public DebrisArc()
        {
        }

        internal DebrisArc(Thing thing, IntVec3 targetCell, float launchHeight, float sideSwing, float spin)
        {
            this.thing = thing;
            this.targetCell = targetCell;
            this.launchHeight = launchHeight;
            this.sideSwing = sideSwing;
            this.spin = spin;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref thing, "thing");
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_Values.Look(ref launchHeight, "launchHeight", 0f);
            Scribe_Values.Look(ref sideSwing, "sideSwing", 0f);
            Scribe_Values.Look(ref spin, "spin", 0f);
        }
    }
}
