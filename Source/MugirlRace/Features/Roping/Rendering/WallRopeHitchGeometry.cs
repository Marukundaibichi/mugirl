namespace Mugirl.Features.Roping;

// 墙绳桩的绳点位于所在格朝向一侧的边缘中点（纯几何，便于离线测试）。
internal static class WallRopeHitchGeometry
{
    private const float HalfCell = 0.5f;

    // cellCenter 为 IntVec3.ToVector3Shifted() 的结果（格子中心）；与 1.6 的 Unity 加法逐分量一致。
    internal static Vector3 EdgeAnchor(Vector3 cellCenter, Rot4 rotation)
    {
        return rotation.AsInt switch
        {
            0 => cellCenter + new Vector3(0f, 0f, HalfCell),
            1 => cellCenter + new Vector3(HalfCell, 0f, 0f),
            2 => cellCenter + new Vector3(0f, 0f, -HalfCell),
            3 => cellCenter + new Vector3(-HalfCell, 0f, 0f),
            _ => cellCenter,
        };
    }
}
