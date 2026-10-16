namespace Mugirl.Features.WeaponWheel;

// 抬枪、换枪与卡入闪光的纯曲线，只依赖输入的位置与进度，供渲染器绘制并由单元测试覆盖。
// 数学全部按 Unity（1.6）语义：Lerp 钳制 t，SmoothStep 为 Unity 插值写法，RotatedBy 与 1.7 Verse 一致。
internal static class WeaponWheelAnimationCurves
{
    // 手持武器沿瞄准方向前伸的基础距离（格），与原版 PawnRenderUtility 一致。
    internal const float HeldDistance = 0.4f;
    // 武器相对 Pawn 绘制位置的高度偏移：手持 > 收枪中的旧武器 > 卡入闪光。
    internal const float HeldAltitudeOffset = 0.040f;
    internal const float OutgoingAltitudeOffset = 0.039f;
    internal const float FlashAltitudeDrop = 0.004f;

    // 新武器在前 24% 的进度内淡入。
    private const float IncomingFadeInProgress = 0.24f;

    // FadedMaterialPool 的透明度分级数：floor(alpha × 30)，最高级 29 表示不透明，0 表示不可见。
    internal const int FadeSteps = 30;

    // 与 FadedMaterialPool.IndexFromAlpha 相同的分级（alpha 先钳制到 [0, 1]）。
    internal static int FadeStep(float alpha)
    {
        int step = UnityMathf.FloorToInt(UnityMathf.Clamp01(alpha) * FadeSteps);
        return step == FadeSteps ? FadeSteps - 1 : step;
    }

    // 手持位置：沿瞄准角前伸 (0.4 + equippedDistanceOffset) 格，按年龄阶段的装备距离缩放，再抬到手持高度。
    internal static Vector3 HeldPosition(Vector3 pawnDrawPosition, float aimAngle, float equippedDistanceOffset, float distanceFactor)
    {
        Vector3 position = pawnDrawPosition
            + UnityVector.RotatedBy(new Vector3(0f, 0f, HeldDistance + equippedDistanceOffset), aimAngle) * distanceFactor;
        position.Y += HeldAltitudeOffset;
        return position;
    }

    // 旧武器从手持位置向下滑出、旋转并淡出。
    internal static WeaponWheelWeaponPose Outgoing(
        Vector3 pawnDrawPosition,
        float aimAngle,
        float outgoingProgress,
        float equippedDistanceOffset,
        float distanceFactor)
    {
        Vector3 position = pawnDrawPosition
            + UnityVector.RotatedBy(new Vector3(0f, 0f, HeldDistance + equippedDistanceOffset), aimAngle) * distanceFactor;
        position += new Vector3(0f, 0f, -0.62f * outgoingProgress);
        position.Y += OutgoingAltitudeOffset;
        return new WeaponWheelWeaponPose(position, aimAngle + 145f * outgoingProgress, 1f, 1f - outgoingProgress);
    }

    // 卡入瞬间在手持位置下方放大并淡出的残影。
    internal static WeaponWheelWeaponPose Flash(Vector3 holdPosition, float aimAngle, float snapProgress)
    {
        Vector3 position = holdPosition;
        position.Y -= FlashAltitudeDrop;
        float expansion = UnityMathf.Pow(snapProgress, 1.35f);
        float fade = UnityMathf.SmoothStep(0f, 1f, snapProgress);
        return new WeaponWheelWeaponPose(position, aimAngle, UnityMathf.Lerp(1.04f, 2f, expansion), 0.72f * (1f - fade));
    }

    // 新武器的轨迹。换枪：从身侧快速贴近后卡入并轻微后坐；抬枪：从背后绕弧线甩到手持位置。
    internal static WeaponWheelWeaponPose Incoming(
        Vector3 pawnDrawPosition,
        Vector3 holdPosition,
        float aimAngle,
        float progress,
        float snapProgress,
        float snapStartProgress,
        bool isSwitching)
    {
        Vector3 position;
        float angle;
        if (isSwitching)
        {
            Vector3 offsetPosition = pawnDrawPosition + UnityVector.RotatedBy(new Vector3(0f, 0f, 0.72f), aimAngle - 58f);
            offsetPosition.Z += 0.22f;
            offsetPosition.Y = holdPosition.Y;

            Vector3 preSnapPosition = holdPosition + UnityVector.RotatedBy(new Vector3(0f, 0f, 0.16f), aimAngle - 90f);
            preSnapPosition.Z += 0.06f;
            preSnapPosition.Y = holdPosition.Y;

            float fastSnapStart = UnityMathf.Max(0.01f, snapStartProgress - 0.16f);
            if (progress < fastSnapStart)
            {
                float approach = UnityMathf.SmoothStep(0f, 1f, UnityMathf.Clamp01(progress / fastSnapStart));
                position = UnityVector.Lerp(offsetPosition, preSnapPosition, approach);
                angle = UnityMathf.LerpAngle(aimAngle - 78f, aimAngle - 14f, approach);
            }
            else if (snapProgress <= 0f)
            {
                float snap = UnityMathf.Clamp01((progress - fastSnapStart) / UnityMathf.Max(0.01f, snapStartProgress - fastSnapStart));
                snap = UnityMathf.SmoothStep(0f, 1f, snap);
                position = UnityVector.Lerp(preSnapPosition, holdPosition, snap);
                angle = UnityMathf.LerpAngle(aimAngle - 14f, aimAngle, snap);
            }
            else
            {
                float impact = UnityMathf.Clamp01(snapProgress / 0.46f);
                float kick = UnityMathf.Sin(impact * UnityMathf.PI);
                position = holdPosition + UnityVector.RotatedBy(new Vector3(0f, 0f, 0.03f * kick), aimAngle);
                angle = aimAngle + 2.5f * kick;
            }
        }
        else if (progress < 0.72f)
        {
            float slow = UnityMathf.SmoothStep(0f, 1f, progress / 0.72f);
            float orbitAngle = UnityMathf.Lerp(aimAngle - 115f, aimAngle - 32f, slow);
            float radius = UnityMathf.Lerp(0.92f, 0.62f, slow);
            position = pawnDrawPosition + UnityVector.RotatedBy(new Vector3(0f, 0f, radius), orbitAngle);
            position.Z += UnityMathf.Lerp(0.58f, 0.16f, slow);
            angle = UnityMathf.Lerp(aimAngle - 165f, aimAngle + 38f, slow);
        }
        else
        {
            float fast = UnityMathf.Clamp01((progress - 0.72f) / 0.28f);
            fast *= fast;
            Vector3 arcEnd = pawnDrawPosition + UnityVector.RotatedBy(new Vector3(0f, 0f, 0.62f), aimAngle - 32f);
            arcEnd.Z += 0.16f;
            position = UnityVector.Lerp(arcEnd, holdPosition, fast);
            angle = UnityMathf.Lerp(aimAngle + 38f, aimAngle + 398f, fast);
        }

        position.Y = holdPosition.Y;
        return new WeaponWheelWeaponPose(position, angle, 1f, UnityMathf.Clamp01(progress / IncomingFadeInProgress));
    }
}
