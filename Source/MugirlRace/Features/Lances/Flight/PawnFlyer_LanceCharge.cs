using HarmonyLib;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Features.Lances;

/// <summary>
/// 骑枪直线冲锋的飞行器（XML thingClass，按类名存档）。每段位移先沿途破墙，沿途冲锋攻击途经敌人，定点冲锋在到达时结算；
/// 绘制时保持人物清晰，另画前指的骑枪与方向模糊。
/// </summary>
public sealed class PawnFlyer_LanceCharge : PawnFlyer, ICustomEquippedWeaponDrawOwner
{
    internal const int WallHitPointBudget = 1550;

    private const float PassingAttackRadius = 1.42f;
    private const int SmokeInterval = 8;
    private const int CameraShakeInterval = 8;
    private const float CameraShakeMagnitude = 0.05f;
    private const float MotionBlurShutterTicks = 5f;
    private const float MinimumBlurTravelSquared = 0.0001f;

    // StaticCacheLifecycle: 原版私有字段访问器只缓存程序集元数据，进程内固定，不持有地图或存档对象。
    private static readonly AccessTools.FieldRef<PawnFlyer, IntVec3> DestinationCell =
        AccessTools.FieldRefAccess<PawnFlyer, IntVec3>("destCell");

    // StaticCacheLifecycle: 冲锋段走线复用缓冲；只在主线程 tick 内使用，每次收集前清空，不跨方法持有内容。
    private static readonly List<IntVec3> tmpCrossedCells = new();

    private bool lineCharge;
    private bool steamTrail;
    private Pawn? pointTarget;
    private bool pointImpactApplied;
    private int smokeTick;
    private int cameraShakeTick = CameraShakeInterval;
    private List<Pawn> hitPawns = new();
    private int wallHitPointsRemaining = WallHitPointBudget;
    private IntVec3 lastPassableCell = IntVec3.Invalid;
    private Vector3 stoppedBlurTravel;

    // 运行期诊断探针：最近一帧画出的模糊层数与是否画出前指骑枪（游戏内验证读取）。
    internal int MotionBlurLayersDrawnLastFrame { get; private set; }

    internal bool ForwardLanceDrawnLastFrame { get; private set; }

    private Vector3 BlurTravel => GroundPositionAt(ticksFlying)
        - GroundPositionAt(UnityMathf.Max(0f, ticksFlying - MotionBlurShutterTicks));

    public void Configure(bool isLineCharge, bool leaveSteamTrail, Pawn? lockedPointTarget = null)
    {
        lineCharge = isLineCharge;
        steamTrail = leaveSteamTrail;
        pointTarget = lockedPointTarget;
    }

    public override void DynamicDrawPhaseAt(DrawPhase phase, UVector3 drawLoc, bool flip = false)
    {
        base.DynamicDrawPhaseAt(phase, drawLoc, flip);
        if (phase == DrawPhase.Draw)
        {
            // 当前帧保持清晰；方向模糊只处理已计算的部件贴图，不生成多个人形副本。
            DrawForwardLance();
            DrawMotionBlur();
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref lineCharge, "lineCharge", false);
        Scribe_Values.Look(ref steamTrail, "steamTrail", false);
        Scribe_References.Look(ref pointTarget, "pointTarget");
        Scribe_Values.Look(ref pointImpactApplied, "pointImpactApplied", false);
        Scribe_Values.Look(ref smokeTick, "smokeTick", 0);
        Scribe_Values.Look(ref cameraShakeTick, "cameraShakeTick", CameraShakeInterval);
        Scribe_Collections.Look(ref hitPawns, "hitPawns", LookMode.Reference);
        // 每次冲锋单独保存额度；缺省值也是新飞行状态的完整额度，读档不补充已消耗的耐久。
        Scribe_Values.Look(ref wallHitPointsRemaining, "wallHitPointsRemaining", WallHitPointBudget);
        Scribe_Values.Look(ref lastPassableCell, "lastPassableCell", IntVec3.Invalid);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            // Scribe 可写回空集合；命中去重始终要求一个可用的集合。
            hitPawns ??= new List<Pawn>();
        }
    }

    protected override void TickInterval(int delta)
    {
        Map? map = Map;
        if (map is not null)
        {
            if (!HasFiniteFlightPath())
            {
                // 只有损坏的存档会让起点含 NaN/无穷：此时不再沿途破墙或攻击，直接在原定落点降落。
                MugirlLog.WarningOnce(
                    "LanceCharge.NonFiniteFlightPath",
                    "Lance charge flyer has a non-finite flight path; landing it at its destination without wall breaking or passing attacks.");
                pointImpactApplied = true;
                ticksFlying = ticksFlightTime;
                base.TickInterval(delta);
                return;
            }

            // 在原版推进飞行和绘制下一帧之前检查整段位移，避免高速或合并 tick 穿过墙体。
            if (!TryClearFlightSegment(map, delta))
            {
                stoppedBlurTravel = BlurTravel;
                pointImpactApplied = true;
                DestinationCell(this) = lastPassableCell;
                Position = lastPassableCell;
                ticksFlying = ticksFlightTime;
                base.TickInterval(delta);
                return;
            }

            Vector3 groundPosition = GroundPositionAt(ticksFlying);
            if (lineCharge)
            {
                AttackPassingTargets(LanceChargeGridTraversal.ToCell(groundPosition), map);
            }

            if (steamTrail && (smokeTick += delta) >= SmokeInterval)
            {
                smokeTick = 0;
                FleckMaker.ThrowSmoke(groundPosition.ToUnity(), map, Rand.Range(0.8f, 1.25f));
            }

            cameraShakeTick -= delta;
            if (cameraShakeTick <= 0)
            {
                cameraShakeTick = CameraShakeInterval;
                GameUtility.TryShakeCamera(map, CameraShakeMagnitude, CameraShakeInterval);
            }

            if (!lineCharge && !pointImpactApplied && ticksFlying + delta >= ticksFlightTime)
            {
                pointImpactApplied = true;
                Pawn? attacker = FlyingPawn;
                LanceChargeImpactUtility.ApplyPointImpact(attacker, pointTarget, attacker?.equipment?.Primary, map);
            }
        }

        if (ticksFlying >= ticksFlightTime)
        {
            base.TickInterval(delta);
        }
        else
        {
            // 专用直冲不采用原版跳跃的周期性落点改选；前方新障碍也必须在原路径上撞停。
            // 本飞行器没有原版 flightEffecter，落地仍交还基类恢复任务和骑乘容器。
            ticksFlying += delta;
        }
    }

    protected override void RespawnPawn()
    {
        Map? map = Map;
        UVector3 destination = DestinationPos;
        Vector3 travel = UnityVector.SqrMagnitude(stoppedBlurTravel) > MinimumBlurTravelSquared ? stoppedBlurTravel : BlurTravel;
        if (HasFiniteFlightPath())
        {
            LanceMotionBlur.BeginRecovery(FlyingPawn, travel.ToUnity());
        }

        base.RespawnPawn();
        LanceChargeEffects.ThrowShockwave(destination, map);
    }

    private bool HasFiniteFlightPath()
    {
        UVector3 start = startVec;
        UVector3 destination = DestinationPos;
        return float.IsFinite(start.x) && float.IsFinite(start.z)
            && float.IsFinite(destination.x) && float.IsFinite(destination.z);
    }

    private bool TryClearFlightSegment(Map map, int delta)
    {
        Vector3 from = GroundPositionAt(ticksFlying);
        Vector3 to = GroundPositionAt(Math.Min(ticksFlying + delta, ticksFlightTime));
        if (!lastPassableCell.IsValid)
        {
            // 延迟到实际推进碰撞时建立起点：新飞行从起点开始，未初始化的进行中状态扫描已走过的可站立格。
            // 必须在此读取地图，提前到加载/生成阶段会改变中间地图更新后的结果。
            lastPassableCell = startVec.ToIntVec3();
            LanceChargeGridTraversal.TryCollectCrossedCells(startVec.ToNumerics(), from, tmpCrossedCells);
            for (int i = 0; i < tmpCrossedCells.Count; i++)
            {
                IntVec3 priorCell = tmpCrossedCells[i];
                if (priorCell.InBounds(map) && priorCell.Standable(map))
                {
                    lastPassableCell = priorCell;
                }
            }
        }

        LanceChargeGridTraversal.TryCollectCrossedCells(from, to, tmpCrossedCells);
        for (int i = 0; i < tmpCrossedCells.Count; i++)
        {
            IntVec3 cell = tmpCrossedCells[i];
            if (!LanceChargeWallUtility.TryClearCell(cell, map, FlyingPawn, ref wallHitPointsRemaining))
            {
                return false;
            }

            if (cell.Standable(map))
            {
                lastPassableCell = cell;
            }
        }

        return true;
    }

    private void AttackPassingTargets(IntVec3 chargeCell, Map map)
    {
        Pawn? attacker = FlyingPawn;
        ThingWithComps? lance = attacker?.equipment?.Primary;
        if (attacker is null || lance is null || lance.Destroyed)
        {
            return;
        }

        hitPawns ??= new List<Pawn>();

        // 冲锋期间每 tick 执行；按 RadialPattern 直接遍历格内实体，等价于
        // RadialDistinctThingsAround(useCenter:true)——pawn 恒为单格，占格去重集合
        // 对本分支无作用，hitPawns 已去重——且不再分配枚举器。
        int cellCount = GenRadial.NumCellsInRadius(PassingAttackRadius);
        for (int i = 0; i < cellCount; i++)
        {
            IntVec3 cell = GenRadial.RadialPattern[i] + chargeCell;
            if (!cell.InBounds(map))
            {
                continue;
            }

            List<Thing> thingList = cell.GetThingList(map);
            for (int j = 0; j < thingList.Count; j++)
            {
                if (thingList[j] is not Pawn target || hitPawns.Contains(target)
                    || !LanceChargeImpactUtility.CanHit(attacker, target, map))
                {
                    continue;
                }

                // 邻近攻击半径不能越过尚未撞开的墙命中另一侧目标。
                if (!GenSight.LineOfSight(chargeCell, target.Position, map))
                {
                    continue;
                }

                hitPawns.Add(target);
                LanceChargeImpactUtility.ApplyLineImpact(attacker, target, lance, chargeCell, map);
            }
        }
    }

    private void DrawForwardLance()
    {
        ForwardLanceDrawnLastFrame = false;
        Pawn? pawn = FlyingPawn;
        ThingWithComps? lance = pawn?.equipment?.Primary;
        if (pawn is null || lance is null || !TryGetLanceAimAngle(out float aimAngle))
        {
            return;
        }

        LifeStageDef lifeStage = pawn.ageTracker.CurLifeStage;
        Vector3 drawPosition = GroundPositionAt(ticksFlying) + lifeStage.bodyDrawOffset.ToNumerics();
        drawPosition.Y += PawnRenderUtility.AltitudeForLayer(90f);
        drawPosition += UnityVector.RotatedBy(new Vector3(0f, 0f, 0.48f + lance.def.equippedDistanceOffset), aimAngle)
            * lifeStage.equipmentDrawDistanceFactor;
        LanceChargeDrawingUtility.DrawForwardLance(lance, drawPosition, aimAngle);
        ForwardLanceDrawnLastFrame = true;
    }

    private void DrawMotionBlur()
    {
        MotionBlurLayersDrawnLastFrame = LanceMotionBlur.Draw(
            FlyingPawn,
            BlurTravel.ToUnity(),
            drawPosition: GroundPositionAt(ticksFlying).ToUnity());
    }

    private Vector3 GroundPositionAt(float tick)
    {
        float progress = ticksFlightTime <= 0
            ? 1f
            : UnityMathf.Clamp01(tick / ticksFlightTime);
        float adjustedProgress = def.pawnFlyer.Worker.AdjustedProgress(progress);
        // Unity Lerp 语义（钳制 t、逐分量 a + (b - a) * t、无 FMA）：位置决定破墙与沿途命中的地格。
        return UnityVector.Lerp(startVec.ToNumerics(), DestinationPos.ToNumerics(), adjustedProgress);
    }

    private bool TryGetLanceAimAngle(out float aimAngle)
    {
        aimAngle = 0f;
        Vector3 direction = DestinationPos.ToNumerics() - startVec.ToNumerics();
        if (UnityVector.MagnitudeHorizontalSquared(direction) <= 0.001f)
        {
            return false;
        }

        aimAngle = UnityVector.AngleFlat(direction);
        return true;
    }
}
