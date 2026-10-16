using System.Globalization;

namespace Mugirl.Tests.Mathematics;

/// <summary><see cref="UnityVector"/>：Unity 公式的成员与 UnityStubs 差分比较；复现 1.7 Verse 的成员与 Verse 逐位比较。</summary>
public sealed class UnityVectorTests
{
    [Fact]
    public void Normalized_ReturnsZeroAtOrBelowUnityEpsilon()
    {
        Assert.Equal(Vector3.Zero, UnityVector.Normalized(new Vector3(1E-06f, 0f, 0f)));
        Assert.Equal(Vector3.Zero, UnityVector.Normalized(new Vector3(float.NaN, 1f, 0f)));
        Assert.Equal(Vector2.Zero, UnityVector.Normalized(new Vector2(0f, 1E-05f)));
        Vector3 unit = UnityVector.Normalized(new Vector3(3f, 4f, 0f));
        Bits.Equal(0.6f, unit.X);
        Bits.Equal(0.8f, unit.Y);
    }

    [Fact]
    public void ApproximatelyEqual_UsesUnityTolerance()
    {
        Assert.True(UnityVector.ApproximatelyEqual(new Vector3(1f, 2f, 3f), new Vector3(1f + 9E-06f, 2f, 3f)));
        Assert.False(UnityVector.ApproximatelyEqual(new Vector3(1f, 2f, 3f), new Vector3(1f + 2E-05f, 2f, 3f)));
        Assert.False(UnityVector.ApproximatelyEqual(new Vector3(float.NaN, 0f, 0f), new Vector3(float.NaN, 0f, 0f)));
        Assert.True(UnityVector.IsApproximatelyZero(new Vector3(5E-06f, 0f, 0f)));
        Assert.True(UnityVector.ApproximatelyEqual(new Vector2(1f, 1f), new Vector2(1f, 1f + 9E-06f)));
    }

    [Fact]
    public void MinMax_UseUnityTernaryPerComponent()
    {
        Vector3 max = UnityVector.Max(new Vector3(float.NaN, 1f, -0f), new Vector3(2f, float.NaN, 0f));
        Bits.Equal(2f, max.X);
        Assert.True(float.IsNaN(max.Y));
        Bits.Equal(0f, max.Z);
        Vector2 min = UnityVector.Min(new Vector2(float.NaN, 3f), new Vector2(1f, 2f));
        Bits.Equal(1f, min.X);
        Bits.Equal(2f, min.Y);
    }

    [Fact]
    public void Angle_AndSignedAngle_FollowUnity()
    {
        Bits.Equal(90f, UnityVector.Angle(Vector3.UnitX, Vector3.UnitY));
        Bits.Equal(0f, UnityVector.Angle(Vector3.Zero, Vector3.UnitY));
        // 左手 Y 向上：俯视从北（+Z）转到东（+X）为顺时针，取正。
        Bits.Equal(90f, UnityVector.SignedAngle(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY));
        Bits.Equal(-90f, UnityVector.SignedAngle(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY));
    }

    [Fact]
    public void MoveTowards_FollowsUnity2022Formula()
    {
        Vector3 step = UnityVector.MoveTowards(Vector3.Zero, new Vector3(10f, 0f, 0f), 4f);
        Bits.Equal(4f, step.X);
        Assert.Equal(new Vector3(1f, 2f, 3f), UnityVector.MoveTowards(Vector3.Zero, new Vector3(1f, 2f, 3f), 100f));
        // 负步长：远离目标，即使已很近（UnityStubs 在距离 ≤ 1e-5 时直接返回目标）。
        Vector3 away = UnityVector.MoveTowards(Vector3.Zero, new Vector3(5E-06f, 0f, 0f), -1f);
        Bits.Equal(-1f, away.X);
    }

    [Fact]
    public void Constants_MatchUnityDirections()
    {
        Assert.Equal(new Vector3(-1f, 0f, 0f), UnityVector.Left);
        Assert.Equal(new Vector3(0f, -1f, 0f), UnityVector.Down);
        Assert.Equal(new Vector3(0f, 0f, -1f), UnityVector.Back);
        Assert.Equal(Vector3.UnitX, UnityVector.Right);
        Assert.Equal(Vector3.UnitY, UnityVector.Up);
        Assert.Equal(Vector3.UnitZ, UnityVector.Forward);
        Bits.Equal(0f, UnityVector.Left.Y, "Left uses +0, not -0");
    }

    /// <summary>E41：Unity 与 UnityStubs 在这些成员上 0 处不同。</summary>
    [Fact]
    public void SharedMembers_MatchUnityStubsBitForBit()
    {
        Random random = new(31);
        Mismatches mismatches = new("UnityVector vs UnityStubs Vector2/Vector3");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 a = RandomVector3(random);
            Vector3 b = RandomVector3(random);
            float t = Samples.Uniform(random, -0.5f, 1.5f);
            UVector3 ua = a.ToUnity();
            UVector3 ub = b.ToUnity();
            string Input() => Describe(a) + " " + Describe(b) + " t=" + t.ToString("R", CultureInfo.InvariantCulture);
            CheckVector(mismatches, ua.normalized, UnityVector.Normalized(a), Input);
            mismatches.Check(ua.magnitude, UnityVector.Magnitude(a), Input);
            mismatches.Check(ua.sqrMagnitude, UnityVector.SqrMagnitude(a), Input);
            mismatches.Check(UVector3.Dot(ua, ub), UnityVector.Dot(a, b), Input);
            mismatches.Check(UVector3.Distance(ua, ub), UnityVector.Distance(a, b), Input);
            CheckVector(mismatches, UVector3.Lerp(ua, ub, t), UnityVector.Lerp(a, b, t), Input);
            CheckVector(mismatches, UVector3.LerpUnclamped(ua, ub, t), UnityVector.LerpUnclamped(a, b, t), Input);
            CheckVector(mismatches, UVector3.Cross(ua, ub), UnityVector.Cross(a, b), Input);

            Vector2 a2 = new(a.X, a.Z);
            Vector2 b2 = new(b.X, b.Z);
            UVector2 ua2 = a2.ToUnity();
            UVector2 ub2 = b2.ToUnity();
            UVector2 normalized2 = ua2.normalized;
            Vector2 mine2 = UnityVector.Normalized(a2);
            mismatches.Check(normalized2.x, mine2.X, Input);
            mismatches.Check(normalized2.y, mine2.Y, Input);
            mismatches.Check(ua2.magnitude, UnityVector.Magnitude(a2), Input);
            mismatches.Check(UVector2.Dot(ua2, ub2), UnityVector.Dot(a2, b2), Input);
            mismatches.Check(UVector2.Distance(ua2, ub2), UnityVector.Distance(a2, b2), Input);
            UVector2 lerp2 = UVector2.Lerp(ua2, ub2, t);
            Vector2 mineLerp2 = UnityVector.Lerp(a2, b2, t);
            mismatches.Check(lerp2.x, mineLerp2.X, Input);
            mismatches.Check(lerp2.y, mineLerp2.Y, Input);
        }

        mismatches.AssertNone();
    }

    /// <summary>这些成员复现 1.7 Verse 的计算（经 UnityStubs 的四元数），与 Verse 本身逐位比较。</summary>
    [Fact]
    public void VerseMirrors_MatchVerseBitForBit()
    {
        float[] angles = Samples.Floats(41, 1080f);
        Random random = new(42);
        Mismatches mismatches = new("UnityVector vs Verse Vector3Utility/GenGeo");
        foreach (float angle in angles)
        {
            Vector3 v = RandomVector3(random);
            UVector3 uv = v.ToUnity();
            string Input() => Describe(v) + " angle=" + angle.ToString("R", CultureInfo.InvariantCulture);
            CheckVector(mismatches, Verse.Vector3Utility.RotatedBy(uv, angle), UnityVector.RotatedBy(v, angle), Input);
            CheckVector(mismatches, Verse.Vector3Utility.HorizontalVectorFromAngle(angle), UnityVector.HorizontalVectorFromAngle(angle), Input);
            mismatches.Check(Verse.Vector3Utility.AngleFlat(uv), UnityVector.AngleFlat(v), Input);
            mismatches.Check(Verse.GenGeo.MagnitudeHorizontal(uv), UnityVector.MagnitudeHorizontal(v), Input);
            mismatches.Check(Verse.GenGeo.MagnitudeHorizontalSquared(uv), UnityVector.MagnitudeHorizontalSquared(v), Input);
        }

        mismatches.AssertNone();
    }

    internal static Vector3 RandomVector3(Random random)
    {
        return random.Next(16) switch
        {
            0 => Vector3.Zero,
            1 => new Vector3(Samples.Uniform(random, -1E-05f, 1E-05f), 0f, Samples.Uniform(random, -1E-05f, 1E-05f)),
            2 => new Vector3(Samples.Uniform(random, -10f, 10f), 0f, Samples.Uniform(random, -10f, 10f)),
            _ => new Vector3(Samples.Next(random, 300f), Samples.Next(random, 300f), Samples.Next(random, 300f)),
        };
    }

    internal static void CheckVector(Mismatches mismatches, UVector3 expected, Vector3 actual, Func<string> input)
    {
        mismatches.Check(expected.x, actual.X, input);
        mismatches.Check(expected.y, actual.Y, input);
        mismatches.Check(expected.z, actual.Z, input);
    }

    internal static string Describe(Vector3 v)
        => string.Create(CultureInfo.InvariantCulture, $"({v.X:R}, {v.Y:R}, {v.Z:R})");
}
