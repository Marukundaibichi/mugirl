using Mugirl.Features.Roping;
using Verse;

namespace Mugirl.Tests.Roping;

/// <summary><see cref="WallRopeHitchGeometry"/>：墙绳桩绳点与 1.6 写法（ToVector3Shifted + UnityStubs 加法）逐位一致。</summary>
public sealed class WallRopeHitchGeometryTests
{
    [Fact]
    public void EdgeAnchor_MatchesUnityAdditionForEveryRotation()
    {
        Random random = new(7201);
        UVector3[] offsets =
        [
            new UVector3(0f, 0f, 0.5f),
            new UVector3(0.5f, 0f, 0f),
            new UVector3(0f, 0f, -0.5f),
            new UVector3(-0.5f, 0f, 0f),
        ];

        Mismatches mismatches = new("WallRopeHitchGeometry vs 1.6 anchor");
        for (int i = 0; i < 4000; i++)
        {
            IntVec3 cell = new(random.Next(-400, 400), random.Next(-3, 3), random.Next(-400, 400));
            UVector3 center = cell.ToVector3Shifted();
            for (int rot = 0; rot < 4; rot++)
            {
                UVector3 expected = center + offsets[rot];
                Vector3 actual = WallRopeHitchGeometry.EdgeAnchor(center.ToNumerics(), new Rot4(rot));
                string Input() => cell + " rot=" + rot;
                Mathematics.UnityVectorTests.CheckVector(mismatches, expected, actual, Input);
            }
        }

        mismatches.AssertNone();
    }
}
