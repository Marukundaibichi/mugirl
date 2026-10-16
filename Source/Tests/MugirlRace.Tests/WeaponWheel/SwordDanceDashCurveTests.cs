using System.Globalization;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Tests.WeaponWheel;

/// <summary>
/// <see cref="SwordDanceDashCurve"/> 与 1.6 原实现逐位比较。参照实现照抄 1.6 的 CompWeaponWheel.SwordDance，
/// 用 UnityEngine 类型（1.7 中为 UnityStubs，这些运算与 Unity 逐位相同，见迁移证据 E41）。
/// </summary>
public sealed class SwordDanceDashCurveTests
{
    private const float PauseEnd = 5f / 42f;
    private const float RecoilEnd = 15f / 42f;
    private const float RushEnd = 24f / 42f;
    private const float OvershootEnd = 31f / 42f;

    [Fact]
    public void PhaseConstants_MatchOriginalDesign()
    {
        Bits.Equal(PauseEnd, SwordDanceDashCurve.PauseEnd);
        Bits.Equal(RecoilEnd, SwordDanceDashCurve.RecoilEnd);
        Bits.Equal(RushEnd, SwordDanceDashCurve.RushEnd);
        Bits.Equal(OvershootEnd, SwordDanceDashCurve.OvershootEnd);
    }

    [Theory]
    [InlineData(0, 42, 0f)]
    [InlineData(21, 42, 0.5f)]
    [InlineData(84, 42, 1f)]
    [InlineData(-5, 42, 0f)]
    [InlineData(3, 0, 1f)]
    [InlineData(3, -7, 1f)]
    public void Progress_ClampsAndTreatsDurationAsAtLeastOneTick(int elapsed, int duration, float expected)
        => Bits.Equal(expected, SwordDanceDashCurve.Progress(elapsed, duration));

    [Fact]
    public void Direction_FallsBackToNorthWhenPointsCoincide()
    {
        Assert.Equal(Vector3.UnitZ, SwordDanceDashCurve.Direction(new Vector3(3f, 0f, 3f), new Vector3(3.005f, 0f, 3f)));
        Vector3 east = SwordDanceDashCurve.Direction(Vector3.Zero, new Vector3(4f, 7f, 0f));
        Bits.Equal(1f, east.X);
        Bits.Equal(0f, east.Y, "height is ignored");
    }

    [Fact]
    public void Direction_MatchesOriginal()
    {
        Random random = new(4101);
        Mismatches mismatches = new("SwordDanceDashCurve.Direction");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 origin = MapPoint(random);
            Vector3 end = random.Next(8) == 0 ? origin + new Vector3(Samples.Uniform(random, -0.01f, 0.01f), 0f, Samples.Uniform(random, -0.01f, 0.01f)) : MapPoint(random);
            Vector3 actual = SwordDanceDashCurve.Direction(origin, end);
            UVector3 expected = ReferenceDirection(origin.ToUnity(), end.ToUnity());
            CheckVector(mismatches, expected, actual, () => Describe(origin, end));
        }
        mismatches.AssertNone();
    }

    [Fact]
    public void RootPosition_MatchesOriginal()
    {
        Random random = new(4102);
        float[] boundaries = [0f, PauseEnd, RecoilEnd, RushEnd, OvershootEnd, 1f];
        Mismatches mismatches = new("SwordDanceDashCurve.RootPosition");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 origin = MapPoint(random);
            Vector3 destination = MapPoint(random);
            Vector3 direction = SwordDanceDashCurve.Direction(origin, destination);
            float progress = i < boundaries.Length * 50
                ? boundaries[i % boundaries.Length]
                : SwordDanceDashCurve.Progress(random.Next(-2, 46), 42);
            Vector3 actual = SwordDanceDashCurve.RootPosition(origin, destination, direction, progress);
            UVector3 expected = ReferenceRoot(origin.ToUnity(), destination.ToUnity(), direction.ToUnity(), progress);
            CheckVector(mismatches, expected, actual, () => Describe(origin, destination) + " p=" + progress.ToString("R", CultureInfo.InvariantCulture));
        }
        mismatches.AssertNone();
    }

    [Fact]
    public void ImpactProgress_MatchesOriginal()
    {
        Random random = new(4103);
        Mismatches mismatches = new("SwordDanceDashCurve.ImpactProgress");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 origin = MapPoint(random);
            Vector3 destination = random.Next(16) == 0 ? origin : origin + new Vector3(random.Next(-2, 3), 0f, random.Next(-2, 3));
            Vector3 target = (origin + destination) * 0.5f + new Vector3(Samples.Uniform(random, -1f, 1f), 0f, Samples.Uniform(random, -1f, 1f));
            Vector3 direction = SwordDanceDashCurve.Direction(origin, destination);
            float actual = SwordDanceDashCurve.ImpactProgress(origin, destination, direction, target);
            float expected = ReferenceImpact(origin.ToUnity(), destination.ToUnity(), direction.ToUnity(), target.ToUnity());
            mismatches.Check(expected, actual, () => Describe(origin, destination));
        }
        mismatches.AssertNone();
    }

    [Fact]
    public void ImpactProgress_StaysInsideRushPhase()
    {
        Vector3 origin = new(10.5f, 0f, 10.5f);
        Vector3 destination = new(12.5f, 0f, 10.5f);
        Vector3 direction = SwordDanceDashCurve.Direction(origin, destination);
        float early = SwordDanceDashCurve.ImpactProgress(origin, destination, direction, origin - direction * 2f);
        float late = SwordDanceDashCurve.ImpactProgress(origin, destination, direction, destination + direction * 2f);
        Bits.Equal(RecoilEnd, early, "target behind the recoil point resolves when the rush starts");
        Bits.Equal(RushEnd, late, "target past the destination resolves when the rush ends");
    }

    private static Vector3 MapPoint(Random random)
        => new(Samples.Uniform(random, 0f, 300f), 0f, Samples.Uniform(random, 0f, 300f));

    private static void CheckVector(Mismatches mismatches, UVector3 expected, Vector3 actual, Func<string> describe)
    {
        mismatches.Check(expected.x, actual.X, describe);
        mismatches.Check(expected.y, actual.Y, describe);
        mismatches.Check(expected.z, actual.Z, describe);
    }

    private static string Describe(Vector3 a, Vector3 b)
        => string.Create(CultureInfo.InvariantCulture, $"a=({a.X:R}, {a.Z:R}) b=({b.X:R}, {b.Z:R})");

    // ---- 1.6 原实现（CompWeaponWheel.SwordDance.cs），只把字段换成参数 ----

    private static UVector3 ReferenceDirection(UVector3 origin, UVector3 end)
    {
        origin.y = 0f;
        end.y = 0f;
        UVector3 direction = end - origin;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : UVector3.forward;
    }

    private static UVector3 ReferenceRoot(UVector3 origin, UVector3 destination, UVector3 direction, float progress)
    {
        UVector3 recoil = origin - direction * 0.5f;
        UVector3 overshoot = destination + direction * 0.5f;
        if (progress < PauseEnd)
        {
            return origin;
        }
        if (progress < RecoilEnd)
        {
            float phase = UMathf.InverseLerp(PauseEnd, RecoilEnd, progress);
            return UVector3.LerpUnclamped(origin, recoil, ReferenceSmoothStep01(phase));
        }
        if (progress < RushEnd)
        {
            float phase = UMathf.InverseLerp(RecoilEnd, RushEnd, progress);
            return UVector3.LerpUnclamped(recoil, destination, phase * phase);
        }
        if (progress < OvershootEnd)
        {
            float phase = UMathf.InverseLerp(RushEnd, OvershootEnd, progress);
            float decelerating = 1f - (1f - phase) * (1f - phase);
            return UVector3.LerpUnclamped(destination, overshoot, decelerating);
        }

        float returnPhase = UMathf.InverseLerp(OvershootEnd, 1f, progress);
        return UVector3.LerpUnclamped(overshoot, destination, ReferenceSmoothStep01(returnPhase));
    }

    private static float ReferenceSmoothStep01(float value)
    {
        value = UMathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float ReferenceImpact(UVector3 origin, UVector3 end, UVector3 direction, UVector3 targetPosition)
    {
        UVector3 recoil = origin - direction * 0.5f;
        UVector3 rush = end - recoil;
        float targetFraction = rush.sqrMagnitude <= 0.0001f
            ? 0.5f
            : UMathf.Clamp01(UVector3.Dot(targetPosition - recoil, rush) / rush.sqrMagnitude);
        return UMathf.Lerp(RecoilEnd, RushEnd, UMathf.Sqrt(targetFraction));
    }
}
