namespace Mugirl.Features.Mounting;

// 骑乘绘制的纯几何计算（System.Numerics，与 1.6 Unity 运算符逐分量一致），不读游戏状态，便于离线测试。
internal static class MountDrawOffsets
{
    // 武器相对骑手的横向偏移与高度偏移（1.6 常量）。
    internal const float WeaponSideDistance = 0.14f;
    internal const float MeleeWeaponExtraSideDistance = 0.12f;
    internal const float WeaponAltitudeOffset = 0.01f;

    // 原版 PawnRenderUtility.DrawEquipmentAndApparelExtras 的瞄准偏移：武器沿瞄准方向前移 0.4 + equippedDistanceOffset。
    internal const float AimedWeaponForwardOffset = 0.4f;

    // 原版判断瞄准方向是否有效的水平距离平方阈值，目标与骑手几乎重合时角度取 0。
    internal const float MinAimDistanceSquared = 0.001f;

    internal static Vector3 RotationOffset(CompProperties_Mount? props, Rot4 rotation)
    {
        if (props is null)
        {
            return Vector3.Zero;
        }

        UVector3 offset = rotation.AsInt switch
        {
            0 => props.northOffset,
            1 => props.eastOffset,
            3 => props.westOffset,
            _ => props.southOffset,
        };
        return offset.ToNumerics();
    }

    internal static float RiderAltitudeOffset(CompProperties_Mount props, Rot4 rotation)
    {
        if (rotation == Rot4.North)
        {
            return props.northRiderAltitudeOffset;
        }

        return rotation == Rot4.South ? props.southRiderAltitudeOffset : props.riderAltitudeOffset;
    }

    // 1.6：carrier + head + offset 从左到右逐分量相加；高度只取载具高度加朝向偏移（无 Props 时不加）。
    internal static Vector3 RiderPosition(Vector3 carrierDrawPos, Vector3 headOffset, Vector3 rotationOffset, float? altitudeOffset)
    {
        Vector3 result = carrierDrawPos + headOffset + rotationOffset;
        result.Y = altitudeOffset is { } offset ? carrierDrawPos.Y + offset : carrierDrawPos.Y;
        return result;
    }

    // 南北朝向沿 RighthandCell 横移，东西朝向不偏移（武器已在身体侧面）。
    internal static Vector3 WeaponSideOffset(Rot4 rotation, float distance)
    {
        if (rotation == Rot4.East || rotation == Rot4.West)
        {
            return Vector3.Zero;
        }

        IntVec3 cell = rotation.RighthandCell;
        return new Vector3(cell.x, cell.y, cell.z) * distance;
    }

    internal static Vector3 WeaponPosition(Vector3 riderPos, Rot4 rotation)
    {
        Vector3 pos = riderPos + WeaponSideOffset(rotation, WeaponSideDistance);
        pos.Y += WeaponAltitudeOffset;
        return pos;
    }

    // 镜像原版：水平距离过近时角度为 0，否则取 Verse AngleFlat（0 = 北，顺时针）。
    internal static float AimAngle(Vector3 targetPos, Vector3 riderPos)
    {
        Vector3 delta = targetPos - riderPos;
        return UnityVector.MagnitudeHorizontalSquared(delta) > MinAimDistanceSquared ? UnityVector.AngleFlat(delta) : 0f;
    }

    // 原版：drawLoc += (0, 0, 0.4 + equippedDistanceOffset).RotatedBy(angle) * equipmentDrawDistanceFactor。
    internal static Vector3 AimedWeaponPosition(Vector3 weaponPos, float aimAngle, float equippedDistanceOffset, float drawDistanceFactor)
    {
        Vector3 forward = new(0f, 0f, AimedWeaponForwardOffset + equippedDistanceOffset);
        return weaponPos + (UnityVector.RotatedBy(forward, aimAngle) * drawDistanceFactor);
    }
}
