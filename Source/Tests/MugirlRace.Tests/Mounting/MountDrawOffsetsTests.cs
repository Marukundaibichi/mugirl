using System.Globalization;
using Mugirl.Features.Mounting;
using Mugirl.Tests.Mathematics;
using Verse;

namespace Mugirl.Tests.Mounting;

/// <summary>
/// <see cref="MountDrawOffsets"/>：骑手与武器绘制位置的纯几何计算。期望值用 1.6 的写法（UnityStubs 的 Vector3 运算符与 Verse
/// 扩展方法）在 1.7 上直接算出，逐位比较。
/// </summary>
public sealed class MountDrawOffsetsTests
{
    private static readonly Rot4[] Rotations = [Rot4.North, Rot4.East, Rot4.South, Rot4.West];

    [Fact]
    public void RotationOffset_SelectsTheOffsetOfEachRotation()
    {
        CompProperties_Mount props = new()
        {
            northOffset = new UVector3(1f, 2f, 3f),
            eastOffset = new UVector3(4f, 5f, 6f),
            southOffset = new UVector3(7f, 8f, 9f),
            westOffset = new UVector3(10f, 11f, 12f),
        };

        Assert.Equal(new Vector3(1f, 2f, 3f), MountDrawOffsets.RotationOffset(props, Rot4.North));
        Assert.Equal(new Vector3(4f, 5f, 6f), MountDrawOffsets.RotationOffset(props, Rot4.East));
        Assert.Equal(new Vector3(7f, 8f, 9f), MountDrawOffsets.RotationOffset(props, Rot4.South));
        Assert.Equal(new Vector3(10f, 11f, 12f), MountDrawOffsets.RotationOffset(props, Rot4.West));
        Assert.Equal(Vector3.Zero, MountDrawOffsets.RotationOffset(null, Rot4.North));
    }

    [Fact]
    public void RiderAltitudeOffset_UsesNorthAndSouthOverrides()
    {
        CompProperties_Mount props = new();
        Bits.Equal(props.northRiderAltitudeOffset, MountDrawOffsets.RiderAltitudeOffset(props, Rot4.North));
        Bits.Equal(props.southRiderAltitudeOffset, MountDrawOffsets.RiderAltitudeOffset(props, Rot4.South));
        Bits.Equal(props.riderAltitudeOffset, MountDrawOffsets.RiderAltitudeOffset(props, Rot4.East));
        Bits.Equal(props.riderAltitudeOffset, MountDrawOffsets.RiderAltitudeOffset(props, Rot4.West));
    }

    [Fact]
    public void RiderPosition_MatchesUnityOperatorChain()
    {
        Random random = new(7101);
        Mismatches mismatches = new("RiderPosition vs 1.6 carrier + head + offset");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 carrier = UnityVectorTests.RandomVector3(random);
            Vector3 head = UnityVectorTests.RandomVector3(random);
            Vector3 offset = UnityVectorTests.RandomVector3(random);
            float? altitude = random.Next(4) == 0 ? null : Samples.Next(random, 1f);

            UVector3 expected = carrier.ToUnity() + head.ToUnity() + offset.ToUnity();
            expected.y = altitude is { } a ? carrier.Y + a : carrier.Y;
            Vector3 actual = MountDrawOffsets.RiderPosition(carrier, head, offset, altitude);

            string Input() => string.Create(CultureInfo.InvariantCulture, $"{UnityVectorTests.Describe(carrier)} {UnityVectorTests.Describe(head)} {UnityVectorTests.Describe(offset)} alt={altitude:R}");
            UnityVectorTests.CheckVector(mismatches, expected, actual, Input);
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void WeaponPosition_MatchesUnityRighthandCellOffset()
    {
        Random random = new(7102);
        Mismatches mismatches = new("WeaponPosition vs 1.6 RighthandCell offset");
        for (int i = 0; i < 4000; i++)
        {
            Vector3 rider = UnityVectorTests.RandomVector3(random);
            foreach (Rot4 rotation in Rotations)
            {
                UVector3 expected = rider.ToUnity();
                if (rotation != Rot4.East && rotation != Rot4.West)
                {
                    expected += rotation.RighthandCell.ToVector3() * MountDrawOffsets.WeaponSideDistance;
                }

                expected.y += MountDrawOffsets.WeaponAltitudeOffset;
                Vector3 actual = MountDrawOffsets.WeaponPosition(rider, rotation);
                string Input() => UnityVectorTests.Describe(rider) + " rot=" + rotation.AsInt.ToString(CultureInfo.InvariantCulture);
                UnityVectorTests.CheckVector(mismatches, expected, actual, Input);
            }
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void AimAngle_MirrorsVanillaGuardAndAngleFlat()
    {
        Random random = new(7103);
        Mismatches mismatches = new("AimAngle vs vanilla DrawEquipmentAndApparelExtras");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 target = UnityVectorTests.RandomVector3(random);
            Vector3 rider = random.Next(8) == 0 ? target + new Vector3(0.01f, 5f, -0.01f) : UnityVectorTests.RandomVector3(random);

            UVector3 delta = target.ToUnity() - rider.ToUnity();
            float expected = GenGeo.MagnitudeHorizontalSquared(delta) > 0.001f ? delta.AngleFlat() : 0f;
            float actual = MountDrawOffsets.AimAngle(target, rider);
            mismatches.Check(expected, actual, () => UnityVectorTests.Describe(target) + " " + UnityVectorTests.Describe(rider));
        }

        mismatches.AssertNone();
        Bits.Equal(0f, MountDrawOffsets.AimAngle(new Vector3(1f, 0f, 1f), new Vector3(1.01f, 3f, 1.01f)));
    }

    [Fact]
    public void AimedWeaponPosition_MatchesVanillaFormula()
    {
        Random random = new(7104);
        float[] angles = Samples.Floats(7105, 720f, 4000);
        Mismatches mismatches = new("AimedWeaponPosition vs vanilla drawLoc offset");
        foreach (float angle in angles)
        {
            Vector3 weapon = UnityVectorTests.RandomVector3(random);
            float equippedOffset = Samples.Uniform(random, -0.5f, 0.5f);
            float factor = Samples.Uniform(random, 0.5f, 1.5f);

            UVector3 expected = weapon.ToUnity() + (new UVector3(0f, 0f, 0.4f + equippedOffset).RotatedBy(angle) * factor);
            Vector3 actual = MountDrawOffsets.AimedWeaponPosition(weapon, angle, equippedOffset, factor);
            string Input() => string.Create(CultureInfo.InvariantCulture, $"{UnityVectorTests.Describe(weapon)} angle={angle:R} off={equippedOffset:R} factor={factor:R}");
            UnityVectorTests.CheckVector(mismatches, expected, actual, Input);
        }

        mismatches.AssertNone();
    }
}
