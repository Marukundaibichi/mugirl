namespace Mugirl.Tests.Mathematics;

/// <summary><see cref="UnityInterop"/> 只搬运分量：往返转换逐位不变，包括 -0 与 NaN。</summary>
public sealed class UnityInteropTests
{
    [Fact]
    public void Conversions_RoundTripBitForBit()
    {
        Vector3 v3 = new(-0f, float.NaN, 3.25f);
        Vector3 back3 = v3.ToUnity().ToNumerics();
        Bits.Equal(-0f, back3.X);
        Assert.True(float.IsNaN(back3.Y));
        Bits.Equal(3.25f, back3.Z);

        Vector2 v2 = new(1.5f, -2f);
        Assert.Equal(v2, v2.ToUnity().ToNumerics());

        Vector4 v4 = new(1f, 2f, 3f, 4f);
        Assert.Equal(v4, v4.ToUnity().ToNumerics());

        Quaternion q = new(0.1f, 0.2f, 0.3f, 0.9f);
        Assert.Equal(q, q.ToUnity().ToNumerics());

        Color color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f).ToUnityColor();
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), color.ToNumerics());
    }

    [Fact]
    public void Projections_SelectTheDocumentedComponents()
    {
        Assert.Equal(new Vector2(1f, 3f), new UVector3(1f, 2f, 3f).ToNumericsXZ());
        UVector4 withDefaultW = new Vector3(1f, 2f, 3f).ToUnity4();
        Assert.Equal((1f, 2f, 3f, 0f), (withDefaultW.x, withDefaultW.y, withDefaultW.z, withDefaultW.w));
        UVector4 withW = new Vector3(1f, 2f, 3f).ToUnity4(1f);
        Bits.Equal(1f, withW.w);
    }
}
