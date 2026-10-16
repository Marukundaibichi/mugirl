using System.Globalization;

namespace Mugirl.Tests.Mathematics;

/// <summary>
/// <see cref="UnityQuaternion"/>：Unity 中为原生实现的成员跟随 UnityStubs，与 UnityStubs/Verse 逐位比较；
/// Unity 中为托管实现的成员（乘积、旋转向量）按 Unity 公式检查。
/// </summary>
public sealed class UnityQuaternionTests
{
    [Fact]
    public void NativeMembers_MatchUnityStubsBitForBit()
    {
        float[] angles = Samples.Floats(51, 720f);
        Random random = new(52);
        Mismatches mismatches = new("UnityQuaternion vs UnityStubs Quaternion");
        foreach (float angle in angles)
        {
            Vector3 axis = UnityVectorTests.RandomVector3(random);
            float y = Samples.Uniform(random, -720f, 720f);
            float z = Samples.Uniform(random, -720f, 720f);
            string Input() => UnityVectorTests.Describe(axis) + string.Create(CultureInfo.InvariantCulture, $" angles=({angle:R}, {y:R}, {z:R})");

            CheckQuaternion(mismatches, UQuaternion.AngleAxis(angle, axis.ToUnity()), UnityQuaternion.AngleAxis(angle, axis), Input);
            CheckQuaternion(mismatches, UQuaternion.Euler(angle, y, z), UnityQuaternion.Euler(angle, y, z), Input);
            CheckQuaternion(mismatches, UQuaternion.Euler(new UVector3(z, angle, y)), UnityQuaternion.Euler(new Vector3(z, angle, y)), Input);

            Quaternion rotation = UnityQuaternion.Euler(angle, y, z);
            UVector3 stubsEuler = rotation.ToUnity().eulerAngles;
            UnityVectorTests.CheckVector(mismatches, stubsEuler, UnityQuaternion.EulerAngles(rotation), Input);

            Vector3 up = random.Next(8) == 0 ? axis : UnityVectorTests.RandomVector3(random);
            CheckQuaternion(mismatches, UQuaternion.LookRotation(axis.ToUnity(), up.ToUnity()), UnityQuaternion.LookRotation(axis, up), Input);
            CheckQuaternion(mismatches, UQuaternion.LookRotation(axis.ToUnity()), UnityQuaternion.LookRotation(axis), Input);
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void AngleAxisY_MatchesVerseToQuat()
    {
        float[] angles = Samples.Floats(53, 1080f);
        Mismatches mismatches = new("AngleAxisY vs Verse GenMath.ToQuat");
        foreach (float angle in angles)
        {
            CheckQuaternion(mismatches, Verse.GenMath.ToQuat(angle), UnityQuaternion.AngleAxisY(angle), () => angle.ToString("R", CultureInfo.InvariantCulture));
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void AngleAxis_DegenerateAxisGivesIdentity()
    {
        Assert.Equal(Quaternion.Identity, UnityQuaternion.AngleAxis(45f, new Vector3(1E-03f, 0f, 0f)));
        Assert.Equal(Quaternion.Identity, UnityQuaternion.NormalizeSafe(new Quaternion(1E-07f, 0f, 0f, 0f)));
    }

    [Fact]
    public void Multiply_UsesUnityHamiltonProduct_WithoutNormalizing()
    {
        Quaternion a = new(1f, 2f, 3f, 4f);
        Quaternion b = new(-2f, 0.5f, 1f, 3f);
        Quaternion product = UnityQuaternion.Multiply(a, b);
        Bits.Equal(4f * -2f + 1f * 3f + 2f * 1f - 3f * 0.5f, product.X);
        Bits.Equal(4f * 0.5f + 2f * 3f + 3f * -2f - 1f * 1f, product.Y);
        Bits.Equal(4f * 1f + 3f * 3f + 1f * 0.5f - 2f * -2f, product.Z);
        Bits.Equal(4f * 3f - 1f * -2f - 2f * 0.5f - 3f * 1f, product.W);
        Assert.Equal(a, UnityQuaternion.Multiply(Quaternion.Identity, a));
    }

    [Fact]
    public void Rotate_TurnsNorthToEastForPositiveYaw()
    {
        Vector3 east = UnityQuaternion.Rotate(UnityQuaternion.AngleAxisY(90f), Vector3.UnitZ);
        Assert.True(UnityVector.ApproximatelyEqual(Vector3.UnitX, east), UnityVectorTests.Describe(east));
        // Unity 的旋转矩阵形式：非单位四元数时不按 |q|² 缩放（与夹心积不同）。
        Quaternion doubled = new(0f, 0f, 0f, 2f);
        Assert.Equal(new Vector3(1f, 2f, 3f), UnityQuaternion.Rotate(doubled, new Vector3(1f, 2f, 3f)));
    }

    [Fact]
    public void EulerAngles_WrapsInto0To360()
    {
        Vector3 euler = UnityQuaternion.EulerAngles(UnityQuaternion.AngleAxisY(-90f));
        Assert.InRange(euler.Y, 269.999f, 270.001f);
        Assert.InRange(euler.X, 0f, 360f);
    }

    internal static void CheckQuaternion(Mismatches mismatches, UQuaternion expected, Quaternion actual, Func<string> input)
    {
        mismatches.Check(expected.x, actual.X, input);
        mismatches.Check(expected.y, actual.Y, input);
        mismatches.Check(expected.z, actual.Z, input);
        mismatches.Check(expected.w, actual.W, input);
    }
}
