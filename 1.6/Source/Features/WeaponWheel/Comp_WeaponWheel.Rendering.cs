using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    internal struct WeaponWheelAnimationSnapshot
    {
        internal ThingWithComps OutgoingWeapon;
        internal ThingWithComps IncomingWeapon;
        internal float Progress;
        internal float OutgoingProgress;
        internal float AimAngle;
        internal float SnapProgress;
        internal float SnapStartProgress;
        internal bool IsSwitching;
    }

    public sealed partial class Comp_WeaponWheel
    {
        public override List<PawnRenderNode> CompRenderNodes()
        {
            Pawn pawn = Pawn;
            if (pawn?.Drawer?.renderer?.renderTree == null)
            {
                return null;
            }

            backWeaponNodes.Clear();
            List<PawnRenderNode> nodes = new List<PawnRenderNode>(2);
            for (int i = 0; i < 2; i++)
            {
                PawnRenderNodeProperties nodeProps = new PawnRenderNodeProperties
                {
                    debugLabel = "Mugirl weapon wheel back weapon " + i,
                    nodeClass = typeof(PawnRenderNode_BackWeapon),
                    workerClass = typeof(PawnRenderNodeWorker_BackWeapon),
                    pawnType = PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly,
                    useGraphic = true,
                    baseLayer = 0f,
                    drawSize = Vector2.one
                };
                nodes.Add(new PawnRenderNode_BackWeapon(pawn, nodeProps, pawn.Drawer.renderer.renderTree, this, i));
            }
            return nodes;
        }

        internal bool TryGetAnimationSnapshot(out WeaponWheelAnimationSnapshot snapshot)
        {
            snapshot = default(WeaponWheelAnimationSnapshot);
            if (combatState != WeaponWheelCombatState.EquippingPrimary
                && combatState != WeaponWheelCombatState.Switching
                && combatState != WeaponWheelCombatState.SwordDanceSwitching)
            {
                return false;
            }

            int elapsed = Mathf.Max(0, CurrentTick - stateStartTick);
            int duration = Mathf.Max(1, stateDurationTicks);
            int outgoingDuration = Mathf.Max(1, Props.outgoingAnimationTicks);
            int snapTick = CurrentAnimationSnapTick;
            snapshot.OutgoingWeapon = animationOutgoingWeapon;
            snapshot.IncomingWeapon = animationIncomingWeapon;
            snapshot.Progress = Mathf.Clamp01((float)elapsed / duration);
            snapshot.OutgoingProgress = Mathf.Clamp01((float)elapsed / outgoingDuration);
            snapshot.AimAngle = animationAimAngle;
            if (combatState == WeaponWheelCombatState.SwordDanceSwitching)
            {
                snapshot.AimAngle = SwordDanceDashAimAngle(snapshot.AimAngle);
            }
            snapshot.IsSwitching = combatState == WeaponWheelCombatState.Switching
                || combatState == WeaponWheelCombatState.SwordDanceSwitching;
            snapshot.SnapStartProgress = Mathf.Clamp01((float)snapTick / duration);
            snapshot.SnapProgress = elapsed < snapTick || duration <= snapTick
                ? 0f
                : Mathf.Clamp01((float)(elapsed - snapTick + 1) / (duration - snapTick + 1));
            return snapshot.IncomingWeapon != null || snapshot.OutgoingWeapon != null;
        }

        internal bool TryGetAimHandoff(out ThingWithComps weapon, out float aimAngle)
        {
            weapon = aimHandoffWeapon;
            aimAngle = aimHandoffAngle;
            return weapon != null
                && weapon == Pawn?.equipment?.Primary
                && CurrentTick <= aimHandoffUntilTick;
        }
    }

    internal static class WeaponWheelAnimationRenderer
    {
        internal static void Draw(Pawn pawn, DrawPhase phase)
        {
            if (phase != DrawPhase.Draw || pawn == null || pawn.Dead || !pawn.Spawned)
            {
                return;
            }
            Comp_WeaponWheel comp = pawn.TryGetComp<Comp_WeaponWheel>();
            if (comp == null || comp.IsCombatDisabledByMount())
            {
                return;
            }

            float distanceFactor = pawn.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;
            Vector3 pawnDrawPosition = pawn.DrawPos;
            if (!comp.TryGetAnimationSnapshot(out WeaponWheelAnimationSnapshot snapshot))
            {
                if (comp.TryGetSwordDanceDashHeldWeapon(out ThingWithComps dashWeapon, out float dashAimAngle))
                {
                    Vector3 dashWeaponPosition = pawnDrawPosition
                        + new Vector3(0f, 0f, 0.4f + dashWeapon.def.equippedDistanceOffset).RotatedBy(dashAimAngle) * distanceFactor;
                    dashWeaponPosition.y += 0.040f;
                    DrawWeapon(dashWeapon, dashWeaponPosition, dashAimAngle, 1f, 1f, true);
                    return;
                }
                if (!comp.TryGetAimHandoff(out ThingWithComps handoffWeapon, out float handoffAngle))
                {
                    return;
                }
                Vector3 handoffPosition = pawnDrawPosition
                    + new Vector3(0f, 0f, 0.4f + handoffWeapon.def.equippedDistanceOffset).RotatedBy(handoffAngle) * distanceFactor;
                handoffPosition.y += 0.040f;
                DrawWeapon(handoffWeapon, handoffPosition, handoffAngle, 1f, 1f, true);
                return;
            }

            Vector3 holdPosition = pawnDrawPosition
                + new Vector3(0f, 0f, 0.4f + (snapshot.IncomingWeapon?.def?.equippedDistanceOffset ?? 0f)).RotatedBy(snapshot.AimAngle) * distanceFactor;
            holdPosition.y += 0.040f;

            if (snapshot.OutgoingWeapon != null && snapshot.OutgoingProgress < 1f)
            {
                Vector3 outgoingPosition = pawnDrawPosition
                    + new Vector3(0f, 0f, 0.4f + snapshot.OutgoingWeapon.def.equippedDistanceOffset).RotatedBy(snapshot.AimAngle) * distanceFactor;
                outgoingPosition += new Vector3(0f, 0f, -0.62f * snapshot.OutgoingProgress);
                outgoingPosition.y += 0.039f;
                DrawWeapon(snapshot.OutgoingWeapon, outgoingPosition, snapshot.AimAngle + 145f * snapshot.OutgoingProgress, 1f, 1f - snapshot.OutgoingProgress);
            }

            if (snapshot.IncomingWeapon == null)
            {
                return;
            }

            float progress = snapshot.Progress;
            float incomingAlpha = Mathf.Clamp01(progress / 0.24f);
            Vector3 incomingPosition;
            float incomingAngle;
            if (snapshot.IsSwitching)
            {
                Vector3 offsetPosition = pawnDrawPosition
                    + new Vector3(0f, 0f, 0.72f).RotatedBy(snapshot.AimAngle - 58f);
                offsetPosition.z += 0.22f;
                offsetPosition.y = holdPosition.y;

                Vector3 preSnapPosition = holdPosition
                    + new Vector3(0f, 0f, 0.16f).RotatedBy(snapshot.AimAngle - 90f);
                preSnapPosition.z += 0.06f;
                preSnapPosition.y = holdPosition.y;

                float fastSnapStart = Mathf.Max(0.01f, snapshot.SnapStartProgress - 0.16f);
                if (progress < fastSnapStart)
                {
                    float approach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / fastSnapStart));
                    incomingPosition = Vector3.Lerp(offsetPosition, preSnapPosition, approach);
                    incomingAngle = Mathf.LerpAngle(snapshot.AimAngle - 78f, snapshot.AimAngle - 14f, approach);
                }
                else if (snapshot.SnapProgress <= 0f)
                {
                    float snap = Mathf.Clamp01((progress - fastSnapStart) / Mathf.Max(0.01f, snapshot.SnapStartProgress - fastSnapStart));
                    snap = Mathf.SmoothStep(0f, 1f, snap);
                    incomingPosition = Vector3.Lerp(preSnapPosition, holdPosition, snap);
                    incomingAngle = Mathf.LerpAngle(snapshot.AimAngle - 14f, snapshot.AimAngle, snap);
                }
                else
                {
                    incomingPosition = holdPosition;
                    incomingAngle = snapshot.AimAngle;
                    float impact = Mathf.Clamp01(snapshot.SnapProgress / 0.46f);
                    float kick = Mathf.Sin(impact * Mathf.PI);
                    incomingPosition += new Vector3(0f, 0f, 0.03f * kick).RotatedBy(snapshot.AimAngle);
                    incomingAngle += 2.5f * kick;
                }
            }
            else
            {
                if (progress < 0.72f)
                {
                    float slow = Mathf.SmoothStep(0f, 1f, progress / 0.72f);
                    float orbitAngle = Mathf.Lerp(snapshot.AimAngle - 115f, snapshot.AimAngle - 32f, slow);
                    float radius = Mathf.Lerp(0.92f, 0.62f, slow);
                    incomingPosition = pawnDrawPosition + new Vector3(0f, 0f, radius).RotatedBy(orbitAngle);
                    incomingPosition.z += Mathf.Lerp(0.58f, 0.16f, slow);
                    incomingAngle = Mathf.Lerp(snapshot.AimAngle - 165f, snapshot.AimAngle + 38f, slow);
                }
                else
                {
                    float fast = Mathf.Clamp01((progress - 0.72f) / 0.28f);
                    fast *= fast;
                    Vector3 arcEnd = pawnDrawPosition + new Vector3(0f, 0f, 0.62f).RotatedBy(snapshot.AimAngle - 32f);
                    arcEnd.z += 0.16f;
                    incomingPosition = Vector3.Lerp(arcEnd, holdPosition, fast);
                    incomingAngle = Mathf.Lerp(snapshot.AimAngle + 38f, snapshot.AimAngle + 398f, fast);
                }
            }
            incomingPosition.y = holdPosition.y;

            if (snapshot.SnapProgress > 0f)
            {
                Vector3 flashPosition = holdPosition;
                flashPosition.y -= 0.004f;
                float expansion = Mathf.Pow(snapshot.SnapProgress, 1.35f);
                float fade = Mathf.SmoothStep(0f, 1f, snapshot.SnapProgress);
                DrawWeapon(
                    snapshot.IncomingWeapon,
                    flashPosition,
                    snapshot.AimAngle,
                    Mathf.Lerp(1.04f, 2f, expansion),
                    0.72f * (1f - fade),
                    true);
            }

            DrawWeapon(snapshot.IncomingWeapon, incomingPosition, incomingAngle, 1f, incomingAlpha, true);
        }

        private static void DrawWeapon(
            ThingWithComps weapon,
            Vector3 position,
            float angle,
            float scale,
            float alpha,
            bool matchVanillaAiming = false)
        {
            if (weapon?.Graphic == null || alpha <= 0.001f)
            {
                return;
            }

            Material material = weapon.Graphic.MatSingleFor(weapon);
            if (material == null)
            {
                return;
            }
            if (alpha < 0.999f)
            {
                material = FadedMaterialPool.FadedVersionOf(material, Mathf.Clamp01(alpha));
            }

            Vector2 drawSize = weapon.Graphic.drawSize;
            float drawAngle = angle - 90f;
            Mesh mesh = MeshPool.plane10;
            if (matchVanillaAiming)
            {
                if (angle > 20f && angle < 160f)
                {
                    drawAngle += weapon.def.equippedAngleOffset;
                }
                else if (angle > 200f && angle < 340f)
                {
                    mesh = MeshPool.plane10Flip;
                    drawAngle -= 180f;
                    drawAngle -= weapon.def.equippedAngleOffset;
                }
                else
                {
                    drawAngle += weapon.def.equippedAngleOffset;
                }
                drawAngle %= 360f;
            }
            Matrix4x4 matrix = Matrix4x4.TRS(
                position,
                Quaternion.AngleAxis(drawAngle, Vector3.up),
                new Vector3(drawSize.x * scale, 1f, drawSize.y * scale));
            Graphics.DrawMesh(mesh, matrix, material, 0);
        }
    }
}
