using Mugirl.Rendering;

namespace Mugirl.Features.WeaponWheel;

// 主线程绘制阶段（Pawn.DynamicDrawPhaseAt 的 Draw）手动绘制的轮盘武器：抬枪/换枪动画、卡入闪光、
// 动画结束后一 tick 的瞄准过渡，以及剑舞冲刺中的手持武器。
internal static class WeaponWheelAnimationRenderer
{
    internal static void Draw(Pawn pawn, DrawPhase phase, CompWeaponWheel comp)
    {
        if (phase != DrawPhase.Draw || pawn == null || pawn.Dead || !pawn.Spawned)
        {
            return;
        }
        if (comp == null || comp.IsCombatDisabledByMount())
        {
            return;
        }

        float distanceFactor = pawn.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;
        Vector3 pawnDrawPosition = WeaponWheelHooks.Current.BodyPosition(pawn);
        if (!comp.TryGetAnimationSnapshot(out WeaponWheelAnimationSnapshot snapshot))
        {
            if (comp.TryGetSwordDanceDashHeldWeapon(out ThingWithComps? dashWeapon, out float dashAimAngle))
            {
                DrawHeldWeapon(dashWeapon, pawnDrawPosition, dashAimAngle, distanceFactor);
                return;
            }
            if (comp.TryGetAimHandoff(out ThingWithComps? handoffWeapon, out float handoffAngle))
            {
                DrawHeldWeapon(handoffWeapon, pawnDrawPosition, handoffAngle, distanceFactor);
            }
            return;
        }

        ThingWithComps? incoming = snapshot.IncomingWeapon;
        ThingWithComps? outgoing = snapshot.OutgoingWeapon;
        Vector3 holdPosition = WeaponWheelAnimationCurves.HeldPosition(
            pawnDrawPosition, snapshot.AimAngle, incoming?.def?.equippedDistanceOffset ?? 0f, distanceFactor);

        if (outgoing != null && snapshot.OutgoingProgress < 1f)
        {
            WeaponWheelWeaponPose outgoingPose = WeaponWheelAnimationCurves.Outgoing(
                pawnDrawPosition, snapshot.AimAngle, snapshot.OutgoingProgress, outgoing.def.equippedDistanceOffset, distanceFactor);
            DrawWeapon(outgoing, outgoingPose, matchVanillaAiming: false);
        }

        if (incoming == null)
        {
            return;
        }

        WeaponWheelWeaponPose incomingPose = WeaponWheelAnimationCurves.Incoming(
            pawnDrawPosition,
            holdPosition,
            snapshot.AimAngle,
            snapshot.Progress,
            snapshot.SnapProgress,
            snapshot.SnapStartProgress,
            snapshot.IsSwitching);

        if (snapshot.SnapProgress > 0f)
        {
            DrawWeapon(incoming, WeaponWheelAnimationCurves.Flash(holdPosition, snapshot.AimAngle, snapshot.SnapProgress), matchVanillaAiming: true);
        }

        DrawWeapon(incoming, incomingPose, matchVanillaAiming: true);
    }

    private static void DrawHeldWeapon(ThingWithComps weapon, Vector3 pawnDrawPosition, float aimAngle, float distanceFactor)
    {
        Vector3 position = WeaponWheelAnimationCurves.HeldPosition(
            pawnDrawPosition, aimAngle, weapon.def.equippedDistanceOffset, distanceFactor);
        DrawWeapon(weapon, new WeaponWheelWeaponPose(position, aimAngle, 1f, 1f), matchVanillaAiming: true);
    }

    private static void DrawWeapon(ThingWithComps weapon, in WeaponWheelWeaponPose pose, bool matchVanillaAiming)
    {
        Graphic? graphic = weapon.Graphic;
        if (graphic == null || pose.Alpha <= 0.001f)
        {
            return;
        }

        // 与原版 DrawEquipmentAiming 保持一致：MatSingleFor 按武器实例解析文化风格差分，
        // 可堆叠武器取单个贴图而不是堆叠贴图。
        Graphic resolved = graphic is Graphic_StackCount stackCountGraphic
            ? stackCountGraphic.SubGraphicForStackCount(1, weapon.def)
            : graphic;
        Material? material = resolved.MatSingleFor(weapon);
        if (material is null)
        {
            return;
        }

        MaterialPropertyBlock? properties = null;
        if (pose.Alpha < 0.999f)
        {
            int step = WeaponWheelAnimationCurves.FadeStep(pose.Alpha);
            if (step == 0)
            {
                // 原版在最低一级换成全透明材质，等同不绘制。
                return;
            }
            if (step < WeaponWheelAnimationCurves.FadeSteps - 1)
            {
                // 与 FadedMaterialPool 相同的分级透明度，但保留 Graphic 的材质色（材料/风格染色）；
                // FadedVersionOf 会把 _Color 重置为白色，渐变期间武器失去染色。
                // Waylith 的 MaterialBindingResolver 对颜色与向量属性走同一读取（TryGetVectorOrColor），用 SetVector 写 _Color。
                Color tint = resolved.color;
                properties = VfxDraw.SharedBlock();
                properties.SetVector(ShaderPropertyIDs.Color, new UVector4(tint.r, tint.g, tint.b, (float)step / WeaponWheelAnimationCurves.FadeSteps));
            }
        }

        UVector2 drawSize = graphic.drawSize;
        float drawAngle = pose.Angle - 90f;
        bool mirrored = false;
        if (matchVanillaAiming)
        {
            // 与原版 PawnRenderUtility.DrawEquipmentAiming 相同的左右翻转与 equippedAngleOffset 规则。
            if (pose.Angle > 20f && pose.Angle < 160f)
            {
                drawAngle += weapon.def.equippedAngleOffset;
            }
            else if (pose.Angle > 200f && pose.Angle < 340f)
            {
                mirrored = true;
                drawAngle -= 180f;
                drawAngle -= weapon.def.equippedAngleOffset;
            }
            else
            {
                drawAngle += weapon.def.equippedAngleOffset;
            }
            drawAngle %= 360f;
        }

        VfxDraw.Plane(
            pose.Position,
            drawAngle,
            new Vector2(drawSize.x * pose.Scale, drawSize.y * pose.Scale),
            material,
            properties,
            mirrored);
    }

}
