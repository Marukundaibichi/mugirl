using System.Collections.Generic;
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
        private const float PassingAttackRadius = 1.42f;
        private const int SmokeInterval = 8;
        private const int CameraShakeInterval = 8;
        private const float CameraShakeMagnitude = 0.05f;
        private const int AfterimageSpacingTicks = 2;
        private static readonly float[] AfterimageAlphas = { 0.58f, 0.44f, 0.32f, 0.22f, 0.14f };
        private static readonly Color AfterimageTint = new Color(0.68f, 0.9f, 1f);

        private bool lineCharge;
        private bool steamTrail;
        private Pawn pointTarget;
        private bool pointImpactApplied;
        private int smokeTick;
        private int cameraShakeTick = CameraShakeInterval;
        private List<Pawn> hitPawns = new List<Pawn>();

        internal int AfterimagesDrawnLastFrame { get; private set; }
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

            base.TickInterval(delta);
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

                hitPawns.Add(target);
                LanceChargeImpactUtility.ApplyLineImpact(attacker, target, lance, chargeCell, map);
            }
        }

        protected override void RespawnPawn()
        {
            Map map = Map;
            Vector3 destination = DestinationPos;
            base.RespawnPawn();
            LanceChargeEffects.ThrowShockwave(destination, map);
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (phase == DrawPhase.Draw)
            {
                // 先消费 PawnRenderer 为当前位置预计算的 results，再为每个历史位置单独重建渲染矩阵。
                // 直接重用 renderTree.Draw 会把所有副本叠在当前位置，完全看不出残影。
                base.DynamicDrawPhaseAt(phase, drawLoc, flip);
                DrawForwardLance();
                DrawAfterimages();
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

        private void DrawAfterimages()
        {
            Pawn pawn = FlyingPawn;
            PawnRenderer renderer = pawn?.Drawer?.renderer;
            AfterimagesDrawnLastFrame = 0;
            if (renderer == null || ticksFlightTime <= 0)
            {
                return;
            }

            renderer.EnsureGraphicsInitialized();
            for (int index = AfterimageAlphas.Length - 1; index >= 0; index--)
            {
                int ghostTick = ticksFlying - (index + 1) * AfterimageSpacingTicks;
                if (ghostTick < 0)
                {
                    continue;
                }

                Color tint = Color.Lerp(renderer.flasher.CurColor, AfterimageTint, 0.42f);
                tint = tint.ToTransparent(InvisibilityUtility.GetAlpha(pawn) * AfterimageAlphas[index]);
                Vector3 ghostPosition = GroundPositionAt(ghostTick);
                ghostPosition.y -= 0.001f + index * 0.0001f;
                PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
                parms.facing = pawn.Rotation;
                parms.posture = PawnPosture.Standing;
                parms.flags |= PawnRenderFlags.NeverAimWeapon;
                parms.rotDrawMode = renderer.CurRotDrawMode;
                parms.tint = tint;
                parms.carriedThing = pawn.carryTracker?.CarriedThing;
                parms.matrix = Matrix4x4.TRS(
                    ghostPosition + pawn.ageTracker.CurLifeStage.bodyDrawOffset,
                    Quaternion.identity,
                    Vector3.one);
                LanceChargeAfterimageRenderScope.Begin();
                try
                {
                    renderer.renderTree.ParallelPreDraw(parms);
                    renderer.renderTree.Draw(parms);
                    AfterimagesDrawnLastFrame++;
                }
                finally
                {
                    LanceChargeAfterimageRenderScope.End();
                }
            }
        }

        private Vector3 GroundPositionAt(int tick)
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
            if (Scribe.mode == LoadSaveMode.PostLoadInit && hitPawns == null)
            {
                hitPawns = new List<Pawn>();
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
