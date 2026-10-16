using System.Globalization;
using Mugirl.Features.WeaponWheel;
using Verse;

namespace Mugirl.Tests.WeaponWheel;

/// <summary>
/// <see cref="WeaponWheelAnimationCurves"/> 与 1.6 的 WeaponWheelAnimationRenderer.Draw 逐位比较。参照实现照抄 1.6 代码，
/// 向量运算、Lerp、LerpAngle、Clamp01 用 UnityStubs（在这些输入上与 Unity 逐位相同），RotatedBy 用 1.7 Verse；
/// UnityStubs 与 Unity 不同的 SmoothStep、Sin、Pow 用 <see cref="UnityMathf"/>（Unity 公式，见 Mathematics 测试）。
/// </summary>
public sealed class WeaponWheelAnimationCurvesTests
{
    [Fact]
    public void HeldPosition_MatchesOriginal()
    {
        Random random = new(4201);
        Mismatches mismatches = new("WeaponWheelAnimationCurves.HeldPosition");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 pawn = PawnPosition(random);
            float aim = Samples.Uniform(random, 0f, 360f);
            float offset = Samples.Uniform(random, -0.3f, 0.3f);
            float factor = random.Next(4) == 0 ? 1f : Samples.Uniform(random, 0.5f, 1.2f);
            Vector3 actual = WeaponWheelAnimationCurves.HeldPosition(pawn, aim, offset, factor);
            UVector3 expected = pawn.ToUnity() + new UVector3(0f, 0f, 0.4f + offset).RotatedBy(aim) * factor;
            expected.y += 0.040f;
            CheckVector(mismatches, expected, actual, () => Describe(aim, offset));
        }
        mismatches.AssertNone();
    }

    [Fact]
    public void Outgoing_MatchesOriginal()
    {
        Random random = new(4202);
        Mismatches mismatches = new("WeaponWheelAnimationCurves.Outgoing");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 pawn = PawnPosition(random);
            float aim = Samples.Uniform(random, 0f, 360f);
            float progress = Samples.Uniform(random, 0f, 1f);
            float offset = Samples.Uniform(random, -0.3f, 0.3f);
            WeaponWheelWeaponPose actual = WeaponWheelAnimationCurves.Outgoing(pawn, aim, progress, offset, 1f);

            UVector3 expected = pawn.ToUnity() + new UVector3(0f, 0f, 0.4f + offset).RotatedBy(aim) * 1f;
            expected += new UVector3(0f, 0f, -0.62f * progress);
            expected.y += 0.039f;
            CheckVector(mismatches, expected, actual.Position, () => Describe(aim, progress));
            mismatches.Check(aim + 145f * progress, actual.Angle, () => Describe(aim, progress));
            mismatches.Check(1f - progress, actual.Alpha, () => Describe(aim, progress));
        }
        mismatches.AssertNone();
    }

    [Fact]
    public void Flash_MatchesOriginal()
    {
        Random random = new(4203);
        Mismatches mismatches = new("WeaponWheelAnimationCurves.Flash");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 hold = PawnPosition(random);
            float aim = Samples.Uniform(random, 0f, 360f);
            float snap = Samples.Uniform(random, 0f, 1f);
            WeaponWheelWeaponPose actual = WeaponWheelAnimationCurves.Flash(hold, aim, snap);

            float expansion = UnityMathf.Pow(snap, 1.35f);
            float fade = UnityMathf.SmoothStep(0f, 1f, snap);
            mismatches.Check(hold.Y - 0.004f, actual.Position.Y, () => Describe(aim, snap));
            mismatches.Check(UMathf.Lerp(1.04f, 2f, expansion), actual.Scale, () => Describe(aim, snap));
            mismatches.Check(0.72f * (1f - fade), actual.Alpha, () => Describe(aim, snap));
        }
        mismatches.AssertNone();
    }

    [Fact]
    public void Incoming_MatchesOriginal()
    {
        Random random = new(4204);
        Mismatches mismatches = new("WeaponWheelAnimationCurves.Incoming");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 pawn = PawnPosition(random);
            float aim = Samples.Uniform(random, 0f, 360f);
            Vector3 hold = WeaponWheelAnimationCurves.HeldPosition(pawn, aim, Samples.Uniform(random, -0.2f, 0.2f), 1f);
            bool switching = random.Next(2) == 0;
            float progress = random.Next(10) == 0 ? (random.Next(2) == 0 ? 0f : 1f) : Samples.Uniform(random, 0f, 1f);
            float snapStart = UMathf.Clamp01(random.Next(0, 43) / 42f);
            float snap = random.Next(3) == 0 ? 0f : Samples.Uniform(random, 0f, 1f);
            WeaponWheelWeaponPose actual = WeaponWheelAnimationCurves.Incoming(pawn, hold, aim, progress, snap, snapStart, switching);
            (UVector3 position, float angle, float alpha) = ReferenceIncoming(pawn.ToUnity(), hold.ToUnity(), aim, progress, snap, snapStart, switching);

            string Input() => Describe(aim, progress) + string.Create(CultureInfo.InvariantCulture, $" snap={snap:R} start={snapStart:R} switching={switching}");
            CheckVector(mismatches, position, actual.Position, Input);
            mismatches.Check(angle, actual.Angle, Input);
            mismatches.Check(alpha, actual.Alpha, Input);
        }
        mismatches.AssertNone();
    }

    [Fact]
    public void Incoming_EndsAtHoldPosition()
    {
        Vector3 pawn = new(20.5f, 4f, 30.5f);
        Vector3 hold = WeaponWheelAnimationCurves.HeldPosition(pawn, 90f, 0f, 1f);
        WeaponWheelWeaponPose equip = WeaponWheelAnimationCurves.Incoming(pawn, hold, 90f, 1f, 0f, 1f, isSwitching: false);
        Assert.True(UnityVector.ApproximatelyEqual(hold, equip.Position));
        Bits.Equal(1f, equip.Alpha);
    }

    [Fact]
    public void FadeStep_MatchesFadedMaterialPool()
    {
        Random random = new(4205);
        Mismatches mismatches = new("WeaponWheelAnimationCurves.FadeStep");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            float alpha = i < 31 ? i / 30f : Samples.Uniform(random, 0f, 1f);
            // FadedMaterialPool.IndexFromAlpha（1.7 rw17-game）：FloorToInt(alpha * 30)，30 并入 29。
            int expected = UMathf.FloorToInt(alpha * 30f);
            if (expected == 30)
            {
                expected = 29;
            }
            mismatches.Check(expected, WeaponWheelAnimationCurves.FadeStep(alpha), () => alpha.ToString("R", CultureInfo.InvariantCulture));
        }
        mismatches.AssertNone();
        Assert.Equal(0, WeaponWheelAnimationCurves.FadeStep(-1f));
        Assert.Equal(29, WeaponWheelAnimationCurves.FadeStep(2f));
    }

    private static Vector3 PawnPosition(Random random)
        => new(Samples.Uniform(random, 0f, 250f), Samples.Uniform(random, 0f, 10f), Samples.Uniform(random, 0f, 250f));

    private static void CheckVector(Mismatches mismatches, UVector3 expected, Vector3 actual, Func<string> describe)
    {
        mismatches.Check(expected.x, actual.X, describe);
        mismatches.Check(expected.y, actual.Y, describe);
        mismatches.Check(expected.z, actual.Z, describe);
    }

    private static string Describe(float aim, float progress)
        => string.Create(CultureInfo.InvariantCulture, $"aim={aim:R} p={progress:R}");

    // ---- 1.6 原实现（WeaponWheelAnimationRenderer.Draw 中新武器的轨迹） ----
    private static (UVector3 Position, float Angle, float Alpha) ReferenceIncoming(
        UVector3 pawnDrawPosition,
        UVector3 holdPosition,
        float aimAngle,
        float progress,
        float snapProgress,
        float snapStartProgress,
        bool isSwitching)
    {
        float incomingAlpha = UMathf.Clamp01(progress / 0.24f);
        UVector3 incomingPosition;
        float incomingAngle;
        if (isSwitching)
        {
            UVector3 offsetPosition = pawnDrawPosition + new UVector3(0f, 0f, 0.72f).RotatedBy(aimAngle - 58f);
            offsetPosition.z += 0.22f;
            offsetPosition.y = holdPosition.y;

            UVector3 preSnapPosition = holdPosition + new UVector3(0f, 0f, 0.16f).RotatedBy(aimAngle - 90f);
            preSnapPosition.z += 0.06f;
            preSnapPosition.y = holdPosition.y;

            float fastSnapStart = UMathf.Max(0.01f, snapStartProgress - 0.16f);
            if (progress < fastSnapStart)
            {
                float approach = UnityMathf.SmoothStep(0f, 1f, UMathf.Clamp01(progress / fastSnapStart));
                incomingPosition = UVector3.Lerp(offsetPosition, preSnapPosition, approach);
                incomingAngle = UMathf.LerpAngle(aimAngle - 78f, aimAngle - 14f, approach);
            }
            else if (snapProgress <= 0f)
            {
                float snap = UMathf.Clamp01((progress - fastSnapStart) / UMathf.Max(0.01f, snapStartProgress - fastSnapStart));
                snap = UnityMathf.SmoothStep(0f, 1f, snap);
                incomingPosition = UVector3.Lerp(preSnapPosition, holdPosition, snap);
                incomingAngle = UMathf.LerpAngle(aimAngle - 14f, aimAngle, snap);
            }
            else
            {
                incomingPosition = holdPosition;
                incomingAngle = aimAngle;
                float impact = UMathf.Clamp01(snapProgress / 0.46f);
                float kick = UnityMathf.Sin(impact * UMathf.PI);
                incomingPosition += new UVector3(0f, 0f, 0.03f * kick).RotatedBy(aimAngle);
                incomingAngle += 2.5f * kick;
            }
        }
        else if (progress < 0.72f)
        {
            float slow = UnityMathf.SmoothStep(0f, 1f, progress / 0.72f);
            float orbitAngle = UMathf.Lerp(aimAngle - 115f, aimAngle - 32f, slow);
            float radius = UMathf.Lerp(0.92f, 0.62f, slow);
            incomingPosition = pawnDrawPosition + new UVector3(0f, 0f, radius).RotatedBy(orbitAngle);
            incomingPosition.z += UMathf.Lerp(0.58f, 0.16f, slow);
            incomingAngle = UMathf.Lerp(aimAngle - 165f, aimAngle + 38f, slow);
        }
        else
        {
            float fast = UMathf.Clamp01((progress - 0.72f) / 0.28f);
            fast *= fast;
            UVector3 arcEnd = pawnDrawPosition + new UVector3(0f, 0f, 0.62f).RotatedBy(aimAngle - 32f);
            arcEnd.z += 0.16f;
            incomingPosition = UVector3.Lerp(arcEnd, holdPosition, fast);
            incomingAngle = UMathf.Lerp(aimAngle + 38f, aimAngle + 398f, fast);
        }
        incomingPosition.y = holdPosition.y;
        return (incomingPosition, incomingAngle, incomingAlpha);
    }
}
