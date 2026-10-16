using Mugirl.Rendering;

namespace Mugirl.Features.Lances;

internal static class LanceChargeDrawingUtility
{
    // 骑枪贴图自身由左下指向右上，比原版装备约定多 45 度。
    private const float LanceTextureAngleOffset = 45f;

    /// <summary>
    /// 冲锋中把骑枪朝前绘制。朝向分区与镜像规则沿用原版 DrawEquipmentAiming：20°–160° 正常、200°–340° 用镜像网格，其余正常。
    /// </summary>
    internal static void DrawForwardLance(ThingWithComps lance, Vector3 drawPosition, float aimAngle)
    {
        float rotation = aimAngle - 90f;
        float equippedOffset = lance.def.equippedAngleOffset + LanceTextureAngleOffset;
        bool mirrored;
        if (aimAngle > 20f && aimAngle < 160f)
        {
            mirrored = false;
            rotation += equippedOffset;
        }
        else if (aimAngle > 200f && aimAngle < 340f)
        {
            mirrored = true;
            rotation -= 180f;
            rotation -= equippedOffset;
        }
        else
        {
            mirrored = false;
            rotation += equippedOffset;
        }

        rotation %= 360f;
        Graphic graphic = lance.Graphic;
        // plane10/plane10Flip 的顶点都在 y = 0 平面上，1.6 矩阵的 y 缩放为 0 与这里的 1 绘制结果相同；
        // 镜像改用 plane10Flip 而不是负缩放（Waylith 固定绕序剔除）。
        VfxDraw.Plane(drawPosition, rotation, graphic.drawSize.ToNumerics(), graphic.MatSingleFor(lance), mirrored: mirrored);
    }
}
