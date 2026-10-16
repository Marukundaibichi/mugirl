namespace Mugirl.Tests.Mathematics;

/// <summary><see cref="UnityMatrix"/>：TRS 与布局转换和 UnityStubs 逐位比较，乘法类成员按 Unity 2022.3 托管公式检查。</summary>
public sealed class UnityMatrixTests
{
    [Fact]
    public void TRS_MatchesUnityStubsBitForBit()
    {
        Random random = new(61);
        Mismatches mismatches = new("UnityMatrix.TRS vs UnityStubs Matrix4x4.TRS");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Vector3 position = UnityVectorTests.RandomVector3(random);
            Vector3 scale = UnityVectorTests.RandomVector3(random);
            Quaternion rotation = random.Next(4) switch
            {
                0 => UnityQuaternion.AngleAxisY(Samples.Uniform(random, -720f, 720f)),
                1 => new Quaternion(Samples.Next(random, 3f), Samples.Next(random, 3f), Samples.Next(random, 3f), Samples.Next(random, 3f)),
                2 => new Quaternion(Samples.Uniform(random, -1E-07f, 1E-07f), 0f, 0f, Samples.Uniform(random, -1E-07f, 1E-07f)),
                _ => UnityQuaternion.Euler(Samples.Uniform(random, -360f, 360f), Samples.Uniform(random, -360f, 360f), Samples.Uniform(random, -360f, 360f)),
            };
            string Input() => UnityVectorTests.Describe(position) + " " + rotation + " " + UnityVectorTests.Describe(scale);
            CheckMatrix(mismatches, UMatrix4x4.TRS(position.ToUnity(), rotation.ToUnity(), scale.ToUnity()), UnityMatrix.TRS(position, rotation, scale), Input);
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void WaylithLayout_MatchesUnityStubsToNumerics_AndRoundTrips()
    {
        UMatrix4x4 m = UnityMatrix.TRS(new Vector3(1f, 2f, 3f), UnityQuaternion.Euler(10f, 20f, 30f), new Vector3(2f, 3f, 4f));
        Assert.Equal(UMatrix4x4.ToNumerics(m), m.ToWaylithLayout());
        Assert.Equal(Matrix4x4.Transpose(m.ToWaylithLayout()), m.ToNumericsConvention());
        UMatrix4x4 back = UnityMatrix.FromWaylithLayout(m.ToWaylithLayout());
        Mismatches mismatches = new("FromWaylithLayout round trip");
        CheckMatrix(mismatches, m, back, () => "TRS sample");
        mismatches.AssertNone();
        Bits.Equal(1f, m.ToWaylithLayout().M14, "translation lives in M14 in the Waylith layout");
    }

    [Fact]
    public void Constructors_FillUnityFields()
    {
        UMatrix4x4 translate = UnityMatrix.Translate(new Vector3(1f, 2f, 3f));
        Assert.Equal(new Vector3(2f, 4f, 6f), UnityMatrix.MultiplyPoint3x4(translate, new Vector3(1f, 2f, 3f)));
        UMatrix4x4 scale = UnityMatrix.Scale(new Vector3(2f, 3f, 4f));
        Assert.Equal(new Vector3(2f, 3f, 4f), UnityMatrix.MultiplyVector(scale, Vector3.One));
        UMatrix4x4 identity = UnityMatrix.Identity;
        Assert.Equal(Matrix4x4.Identity, identity.ToWaylithLayout());
    }

    [Fact]
    public void Multiply_ComposesRightToLeft()
    {
        UMatrix4x4 translate = UnityMatrix.Translate(new Vector3(10f, 0f, 0f));
        UMatrix4x4 scale = UnityMatrix.Scale(new Vector3(2f, 2f, 2f));
        // translate · scale：先缩放再平移。
        UMatrix4x4 combined = UnityMatrix.Multiply(translate, scale);
        Assert.Equal(new Vector3(12f, 2f, 2f), UnityMatrix.MultiplyPoint3x4(combined, Vector3.One));
        Assert.Equal(new Vector4(12f, 2f, 2f, 1f), UnityMatrix.Multiply(combined, new Vector4(1f, 1f, 1f, 1f)));
    }

    [Fact]
    public void MultiplyVector_IgnoresTranslation_MultiplyPointDividesByW()
    {
        UMatrix4x4 m = UnityMatrix.TRS(new Vector3(5f, 6f, 7f), UnityQuaternion.AngleAxisY(90f), Vector3.One);
        Vector3 direction = UnityMatrix.MultiplyVector(m, Vector3.UnitZ);
        Assert.True(UnityVector.ApproximatelyEqual(Vector3.UnitX, direction), UnityVectorTests.Describe(direction));

        UMatrix4x4 projective = UnityMatrix.Identity;
        projective.m33 = 2f;
        Assert.Equal(new Vector3(0.5f, 1f, 1.5f), UnityMatrix.MultiplyPoint(projective, new Vector3(1f, 2f, 3f)));
        Assert.Equal(new Vector3(1f, 2f, 3f), UnityMatrix.MultiplyPoint3x4(projective, new Vector3(1f, 2f, 3f)));
    }

    [Fact]
    public void MultiplyPoint3x4_StaysWithinTwoUlpOfBclTransform()
    {
        Random random = new(62);
        for (int i = 0; i < 2000; i++)
        {
            UMatrix4x4 m = UnityMatrix.TRS(
                new Vector3(Samples.Uniform(random, -300f, 300f), 0f, Samples.Uniform(random, -300f, 300f)),
                UnityQuaternion.AngleAxisY(Samples.Uniform(random, -360f, 360f)),
                new Vector3(Samples.Uniform(random, 0.1f, 4f), 1f, Samples.Uniform(random, 0.1f, 4f)));
            Vector3 p = new(Samples.Uniform(random, -1f, 1f), 0f, Samples.Uniform(random, -1f, 1f));
            Vector3 mine = UnityMatrix.MultiplyPoint3x4(m, p);
            Vector3 bcl = Vector3.Transform(p, m.ToNumericsConvention());
            Assert.True(UnityVector.Distance(mine, bcl) < 1E-03f, UnityVectorTests.Describe(mine) + " vs " + UnityVectorTests.Describe(bcl));
        }
    }

    internal static void CheckMatrix(Mismatches mismatches, UMatrix4x4 expected, UMatrix4x4 actual, Func<string> input)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                mismatches.Check(Element(expected, row, column), Element(actual, row, column), input);
            }
        }
    }

    private static float Element(UMatrix4x4 m, int row, int column) => (row * 4 + column) switch
    {
        0 => m.m00, 1 => m.m01, 2 => m.m02, 3 => m.m03,
        4 => m.m10, 5 => m.m11, 6 => m.m12, 7 => m.m13,
        8 => m.m20, 9 => m.m21, 10 => m.m22, 11 => m.m23,
        12 => m.m30, 13 => m.m31, 14 => m.m32, _ => m.m33,
    };
}
