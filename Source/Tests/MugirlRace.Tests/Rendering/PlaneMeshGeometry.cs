namespace Mugirl.Tests.Rendering;

/// <summary>
/// Verse <c>MeshMakerPlanes.NewPlaneMesh(Vector2 size, bool flipped, bool backLift, bool twist)</c> 的顶点与 UV（1.7 反编译逐行照抄），
/// 不创建网格对象，供 1.6 算法在同一几何上运行。
/// </summary>
internal static class PlaneMeshGeometry
{
    public static (Vector3[] Vertices, Vector2[] Uv) Create(float width, float height, bool flipped, bool backLift, bool twist)
    {
        Vector3[] vertices =
        [
            new(-0.5f * width, 0f, -0.5f * height),
            new(-0.5f * width, 0f, 0.5f * height),
            new(0.5f * width, 0f, 0.5f * height),
            new(0.5f * width, 0f, -0.5f * height),
        ];
        if (backLift)
        {
            vertices[1].Y = 0.0018292684f;
            vertices[2].Y = 0.0018292684f;
            vertices[3].Y = 0.0007317074f;
        }

        if (twist)
        {
            vertices[0].Y = 0.0009146342f;
            vertices[1].Y = 0.0004573171f;
            vertices[2].Y = 0f;
            vertices[3].Y = 0.0004573171f;
        }

        Vector2[] uv = flipped
            ? [new(1f, 0f), new(1f, 1f), new(0f, 1f), new(0f, 0f)]
            : [new(0f, 0f), new(0f, 1f), new(1f, 1f), new(1f, 0f)];
        return (vertices, uv);
    }
}
