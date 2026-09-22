using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.Lances
{
    public sealed class PawnFlyerWorker_LanceCharge : PawnFlyerWorker
    {
        public PawnFlyerWorker_LanceCharge(PawnFlyerProperties properties) : base(properties)
        {
        }

        public override float AdjustedProgress(float t)
        {
            return Mathf.Clamp01(t);
        }

        public override float GetHeight(float t)
        {
            return 0f;
        }
    }

    public sealed class PawnFlyer_LanceCharge : PawnFlyer
    {
        internal const int WallHitPointBudget = 1550;
        // StaticCacheLifecycle: 原版字段访问器只缓存程序集元数据，进程内固定，不持有地图或存档对象。
        private static readonly AccessTools.FieldRef<PawnFlyer, IntVec3> DestinationCell =
            AccessTools.FieldRefAccess<PawnFlyer, IntVec3>("destCell");
        private const float PassingAttackRadius = 1.42f;
        private const int SmokeInterval = 8;
        private const int CameraShakeInterval = 8;
        private const float CameraShakeMagnitude = 0.05f;
        private const float MotionBlurShutterTicks = 5f;

        private bool lineCharge;
        private bool steamTrail;
        private Pawn pointTarget;
        private bool pointImpactApplied;
        private int smokeTick;
        private int cameraShakeTick = CameraShakeInterval;
        private List<Pawn> hitPawns = new List<Pawn>();
        private int wallHitPointsRemaining = WallHitPointBudget;
        private IntVec3 lastPassableCell = IntVec3.Invalid;
        private Vector3 stoppedBlurTravel;

        internal int MotionBlurLayersDrawnLastFrame { get; private set; }
        internal bool ForwardLanceDrawnLastFrame { get; private set; }

        public void Configure(bool isLineCharge, bool leaveSteamTrail, Pawn lockedPointTarget = null)
        {
            lineCharge = isLineCharge;
            steamTrail = leaveSteamTrail;
            pointTarget = lockedPointTarget;
        }

        protected override void TickInterval(int delta)
        {
            Map map = Map;
            if (map != null)
            {
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
                    AttackPassingTargets(groundPosition.ToIntVec3(), map);
                }

                if (steamTrail && (smokeTick += delta) >= SmokeInterval)
                {
                    smokeTick = 0;
                    FleckMaker.ThrowSmoke(groundPosition, map, Rand.Range(0.8f, 1.25f));
                }

                cameraShakeTick -= delta;
                if (cameraShakeTick <= 0)
                {
                    cameraShakeTick = CameraShakeInterval;
                    MugirlGameUtility.TryShakeCamera(map, CameraShakeMagnitude, CameraShakeInterval);
                }

                if (!lineCharge && !pointImpactApplied && ticksFlying + delta >= ticksFlightTime)
                {
                    pointImpactApplied = true;
                    Pawn attacker = FlyingPawn;
                    ThingWithComps lance = attacker?.equipment?.Primary;
                    LanceChargeImpactUtility.ApplyPointImpact(attacker, pointTarget, lance, map);
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

        private bool TryClearFlightSegment(Map map, int delta)
        {
            Vector3 from = GroundPositionAt(ticksFlying);
            Vector3 to = GroundPositionAt(Mathf.Min(ticksFlying + delta, ticksFlightTime));
            if (!lastPassableCell.IsValid)
            {
                // 旧存档没有破墙状态，从当前飞行位置继续；新冲锋从起点开始。
                lastPassableCell = startVec.ToIntVec3();
                foreach (IntVec3 priorCell in LanceChargeWallUtility.CrossedCells(startVec, from))
                {
                    if (priorCell.InBounds(map) && priorCell.Standable(map))
                    {
                        lastPassableCell = priorCell;
                    }
                }
            }

            foreach (IntVec3 cell in LanceChargeWallUtility.CrossedCells(from, to))
            {
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
            Pawn attacker = FlyingPawn;
            ThingWithComps lance = attacker?.equipment?.Primary;
            if (attacker == null || lance == null || lance.Destroyed)
            {
                return;
            }

            if (hitPawns == null)
            {
                hitPawns = new List<Pawn>();
            }

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(chargeCell, map, PassingAttackRadius, true))
            {
                Pawn target = thing as Pawn;
                if (target == null || hitPawns.Contains(target)
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

        protected override void RespawnPawn()
        {
            Map map = Map;
            Vector3 destination = DestinationPos;
            LanceMotionBlur.BeginRecovery(FlyingPawn,
                stoppedBlurTravel.sqrMagnitude > 0.0001f ? stoppedBlurTravel : BlurTravel);
            base.RespawnPawn();
            LanceChargeEffects.ThrowShockwave(destination, map);
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (phase == DrawPhase.Draw)
            {
                // 当前帧保持清晰；方向模糊只处理已计算的部件贴图，不生成多个人形副本。
                base.DynamicDrawPhaseAt(phase, drawLoc, flip);
                DrawForwardLance();
                DrawMotionBlur();
                return;
            }

            base.DynamicDrawPhaseAt(phase, drawLoc, flip);
        }

        private void DrawForwardLance()
        {
            ForwardLanceDrawnLastFrame = false;
            Pawn pawn = FlyingPawn;
            ThingWithComps lance = pawn?.equipment?.Primary;
            if (!TryGetLanceAimAngle(pawn, lance, out float aimAngle))
            {
                return;
            }

            float drawDistanceFactor = pawn.ageTracker.CurLifeStage.equipmentDrawDistanceFactor;
            Vector3 drawPosition = GroundPositionAt(ticksFlying)
                + pawn.ageTracker.CurLifeStage.bodyDrawOffset;
            drawPosition.y += PawnRenderUtility.AltitudeForLayer(90f);
            drawPosition += new Vector3(0f, 0f, 0.48f + lance.def.equippedDistanceOffset)
                .RotatedBy(aimAngle) * drawDistanceFactor;
            LanceChargeDrawingUtility.DrawForwardLance(lance, drawPosition, aimAngle);
            ForwardLanceDrawnLastFrame = true;
        }

        private Vector3 BlurTravel => GroundPositionAt(ticksFlying)
            - GroundPositionAt(Mathf.Max(0f, ticksFlying - MotionBlurShutterTicks));

        private void DrawMotionBlur()
        {
            MotionBlurLayersDrawnLastFrame = LanceMotionBlur.Draw(FlyingPawn, BlurTravel, drawPosition: GroundPositionAt(ticksFlying));
        }

        private Vector3 GroundPositionAt(float tick)
        {
            float progress = ticksFlightTime <= 0
                ? 1f
                : Mathf.Clamp01((float)tick / ticksFlightTime);
            float adjustedProgress = def.pawnFlyer.Worker.AdjustedProgress(progress);
            return Vector3.Lerp(startVec, DestinationPos, adjustedProgress);
        }

        internal bool TryGetLanceAimAngle(Pawn pawn, ThingWithComps weapon, out float aimAngle)
        {
            aimAngle = 0f;
            if (pawn == null || weapon == null || FlyingPawn != pawn || pawn.equipment?.Primary != weapon)
            {
                return false;
            }

            Vector3 direction = DestinationPos - startVec;
            if (direction.MagnitudeHorizontalSquared() <= 0.001f)
            {
                return false;
            }

            aimAngle = direction.AngleFlat();
            return true;
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
            // 每次冲锋单独保存额度，读档不补充已消耗的墙体耐久；旧飞行器默认拥有完整额度。
            Scribe_Values.Look(ref wallHitPointsRemaining, "wallHitPointsRemaining", WallHitPointBudget);
            Scribe_Values.Look(ref lastPassableCell, "lastPassableCell", IntVec3.Invalid);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && hitPawns == null)
            {
                hitPawns = new List<Pawn>();
            }
        }
    }

    internal static class LanceChargeWallUtility
    {
        internal static bool TryClearCell(IntVec3 cell, Map map, Pawn attacker, ref int remaining)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            Building building = cell.GetEdifice(map);
            if (building?.def.IsWall == true)
            {
                if (!building.def.destroyable || !building.def.useHitPoints || remaining <= 0)
                {
                    return false;
                }

                int damage = Mathf.Min(remaining, building.HitPoints);
                remaining -= damage;
                // 额度代表实际墙体耐久，直接扣减以免建筑伤害倍率或施放者近战倍率突破总上限。
                building.HitPoints -= damage;
                if (building.HitPoints <= 0)
                {
                    building.Kill(new DamageInfo(DamageDefOf.Blunt, damage, instigator: attacker));
                }
                if (building.Spawned && !building.Destroyed)
                {
                    return false;
                }
            }

            // 销毁可能留下另一层阻挡物；门、岩石等非墙建筑不消耗破墙额度，但不能穿透。
            building = cell.GetEdifice(map);
            return building == null || (building.def.passability != Traversability.Impassable
                && !(building is Building_Door door && !door.Open));
        }

        internal static IEnumerable<IntVec3> CrossedCells(Vector3 from, Vector3 to)
        {
            IntVec3 cell = from.ToIntVec3();
            IntVec3 end = to.ToIntVec3();
            yield return cell;
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            int stepX = System.Math.Sign(dx);
            int stepZ = System.Math.Sign(dz);
            float strideX = stepX == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dx);
            float strideZ = stepZ == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dz);
            float nextX = stepX == 0 ? float.PositiveInfinity
                : ((stepX > 0 ? cell.x + 1 : cell.x) - from.x) / dx;
            float nextZ = stepZ == 0 ? float.PositiveInfinity
                : ((stepZ > 0 ? cell.z + 1 : cell.z) - from.z) / dz;
            // 按实际网格边界依次推进，斜线擦过的窄小格段也必须检查；恰好过角点时同时换轴。
            while (cell != end)
            {
                if (cell.x == end.x) nextX = float.PositiveInfinity;
                if (cell.z == end.z) nextZ = float.PositiveInfinity;
                if (nextX < nextZ)
                {
                    cell.x += stepX;
                    nextX += strideX;
                }
                else if (nextZ < nextX)
                {
                    cell.z += stepZ;
                    nextZ += strideZ;
                }
                else
                {
                    cell.x += stepX;
                    cell.z += stepZ;
                    nextX += strideX;
                    nextZ += strideZ;
                }
                yield return cell;
            }
        }
    }

    internal static class LanceChargeDestinationUtility
    {
        private static readonly IntVec3[] AdjacentOffsets =
        {
            new IntVec3(-1, 0, -1), new IntVec3(0, 0, -1), new IntVec3(1, 0, -1),
            new IntVec3(-1, 0, 0),                              new IntVec3(1, 0, 0),
            new IntVec3(-1, 0, 1),  new IntVec3(0, 0, 1),  new IntVec3(1, 0, 1)
        };

        internal static bool TryFindLandingCell(Pawn pawn, IntVec3 targetCell, bool pointCharge, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            Map map = pawn?.Map;
            if (pawn == null || map == null || !targetCell.IsValid || !targetCell.InBounds(map))
            {
                return false;
            }

            if (!pointCharge)
            {
                if (!JumpUtility.ValidJumpTarget(pawn, map, targetCell))
                {
                    return false;
                }

                destination = targetCell;
                return true;
            }

            int bestDistance = int.MaxValue;
            for (int index = 0; index < AdjacentOffsets.Length; index++)
            {
                IntVec3 candidate = targetCell + AdjacentOffsets[index];
                if (!JumpUtility.ValidJumpTarget(pawn, map, candidate))
                {
                    continue;
                }

                int distance = candidate.DistanceToSquared(pawn.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    destination = candidate;
                }
            }

            return destination.IsValid;
        }
    }

    internal static class LanceChargeImpactUtility
    {
        private static readonly VerbProperties KnockbackVerbProperties = new VerbProperties();

        internal static bool CanHit(Pawn attacker, Pawn target, Map map)
        {
            return attacker != null && target?.Spawned == true && !target.Dead && target != attacker
                && target.Map == map && attacker.HostileTo(target);
        }

        internal static void ApplyLineImpact(Pawn attacker, Pawn target, ThingWithComps lance, IntVec3 chargeCell, Map map)
        {
            if (!CanHit(attacker, target, map) || lance == null || lance.Destroyed)
            {
                return;
            }

            CompLanceCharge comp = lance.TryGetComp<CompLanceCharge>();
            if (comp == null)
            {
                return;
            }

            CompProperties_LanceCharge properties = comp.Props;
            Tool tool = lance.def.tools.NullOrEmpty() ? null : lance.def.tools[0];
            float damage = tool?.AdjustedBaseMeleeDamageAmount(lance, DamageDefOf.Stab) ?? 10f;
            damage *= attacker.GetStatValue(StatDefOf.MeleeDamageFactor);
            DamageInfo damageInfo = new DamageInfo(
                DamageDefOf.Stab,
                damage,
                tool?.armorPenetration ?? 0.5f,
                -1f,
                attacker,
                null,
                lance.def);
            damageInfo.SetTool(tool);
            target.TakeDamage(damageInfo);
            Stun(attacker, target, properties.lineStunTicks);
            DamageLance(lance, properties.lineDurabilityCost);
            MountedPawnMeleeSupport.TryAttackAlongFlyingCharge(
                MountedPawnUtility.GetMountComp(attacker),
                target,
                map,
                chargeCell);
            Knockback(attacker, target, chargeCell);
        }

        internal static void ApplyPointImpact(Pawn attacker, Pawn target, ThingWithComps lance, Map mapOverride = null)
        {
            Map map = mapOverride ?? attacker?.Map;
            if (!CanHit(attacker, target, map) || lance == null || lance.Destroyed)
            {
                return;
            }

            CompLanceCharge comp = lance.TryGetComp<CompLanceCharge>();
            if (comp == null)
            {
                return;
            }

            CompProperties_LanceCharge properties = comp.Props;
            Tool tool = lance.def.tools.NullOrEmpty() ? null : lance.def.tools[0];
            float damage = Mathf.Min(lance.HitPoints, properties.maximumPointDamage)
                * attacker.GetStatValue(StatDefOf.MeleeDamageFactor);
            DamageInfo damageInfo = new DamageInfo(
                DamageDefOf.Stab,
                damage,
                tool?.armorPenetration ?? 0.5f,
                -1f,
                attacker,
                null,
                lance.def);
            damageInfo.SetTool(tool);
            target.TakeDamage(damageInfo);
            Stun(attacker, target, properties.pointStunTicks);
            DamageLance(lance, properties.pointDurabilityCost);
            MountedPawnMeleeSupport.TryAttackAlongCharge(MountedPawnUtility.GetMountComp(attacker), target);
        }

        private static void Stun(Pawn attacker, Pawn target, int ticks)
        {
            if (ticks > 0 && target?.stances?.stunner != null && !target.Dead)
            {
                target.stances.stunner.StunFor(ticks, attacker);
            }
        }

        private static void DamageLance(ThingWithComps lance, int amount)
        {
            if (amount > 0 && lance != null && !lance.Destroyed)
            {
                lance.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, amount));
            }
        }

        private static void Knockback(Pawn attacker, Pawn target, IntVec3 chargeCell)
        {
            if (target == null || target.Dead || !target.Spawned)
            {
                return;
            }

            Vector3 direction = (target.Position - chargeCell).ToVector3();
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = attacker.Rotation.FacingCell.ToVector3();
            }

            IntVec3 offset = direction.normalized.ToIntVec3();
            if (offset == IntVec3.Zero)
            {
                offset = attacker.Rotation.FacingCell;
            }

            JobDriver_CastCharge.DoJump(target, target.Position + offset, KnockbackVerbProperties);
        }
    }

    internal static class LanceChargeEffects
    {
        internal static void ThrowShockwave(Vector3 position, Map map)
        {
            if (map != null && MugirlContentDefOf.ShockwaveFast != null)
            {
                FleckMaker.Static(position, map, MugirlContentDefOf.ShockwaveFast, 0.025f);
            }
        }
    }

    internal static class LanceChargeDrawingUtility
    {
        // 骑枪贴图自身由左下指向右上，比原版装备约定多 45 度。
        private const float LanceTextureAngleOffset = 45f;

        internal static void DrawForwardLance(ThingWithComps lance, Vector3 drawPosition, float aimAngle)
        {
            float rotation = aimAngle - 90f;
            float equippedOffset = lance.def.equippedAngleOffset + LanceTextureAngleOffset;
            Mesh mesh;
            if (aimAngle > 20f && aimAngle < 160f)
            {
                mesh = MeshPool.plane10;
                rotation += equippedOffset;
            }
            else if (aimAngle > 200f && aimAngle < 340f)
            {
                mesh = MeshPool.plane10Flip;
                rotation -= 180f;
                rotation -= equippedOffset;
            }
            else
            {
                mesh = MeshPool.plane10;
                rotation += equippedOffset;
            }

            rotation %= 360f;
            Material material = lance.Graphic.MatSingleFor(lance);
            Matrix4x4 matrix = Matrix4x4.TRS(
                drawPosition,
                Quaternion.AngleAxis(rotation, Vector3.up),
                new Vector3(lance.Graphic.drawSize.x, 0f, lance.Graphic.drawSize.y));
            Graphics.DrawMesh(mesh, matrix, material, 0);
        }
    }
}
