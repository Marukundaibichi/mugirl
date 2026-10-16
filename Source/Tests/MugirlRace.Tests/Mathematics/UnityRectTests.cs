using System.Globalization;

namespace Mugirl.Tests.Mathematics;

/// <summary><see cref="UnityRect"/> 与 UnityStubs（同为 Unity 2022.3 公式）的 <c>Rect</c> 判断逐个比较。</summary>
public sealed class UnityRectTests
{
    [Fact]
    public void Contains_IsHalfOpen()
    {
        Rect rect = new(10f, 20f, 30f, 40f);
        Assert.True(UnityRect.Contains(rect, new Vector2(10f, 20f)));
        Assert.False(UnityRect.Contains(rect, new Vector2(40f, 30f)), "right edge is excluded");
        Assert.False(UnityRect.Contains(rect, new Vector2(20f, 60f)), "bottom edge is excluded");
        Assert.False(UnityRect.Contains(rect, new Vector2(float.NaN, 30f)));
        Assert.False(UnityRect.Contains(new Rect(10f, 10f, -5f, 5f), new Vector2(7f, 12f)), "negative width contains nothing");
        Assert.True(UnityRect.Contains(rect, new UVector2(39.9f, 59.9f)));
    }

    [Fact]
    public void Overlaps_ExcludesSharedEdges()
    {
        Rect rect = new(0f, 0f, 10f, 10f);
        Assert.True(UnityRect.Overlaps(rect, new Rect(9f, 9f, 5f, 5f)));
        Assert.False(UnityRect.Overlaps(rect, new Rect(10f, 0f, 5f, 5f)));
    }

    [Fact]
    public void Center_MatchesUnity()
    {
        Vector2 center = UnityRect.Center(new Rect(1f, 2f, 3f, 5f));
        Bits.Equal(2.5f, center.X);
        Bits.Equal(4.5f, center.Y);
    }

    [Fact]
    public void AllMembers_MatchUnityStubs()
    {
        Random random = new(81);
        Mismatches mismatches = new("UnityRect vs UnityStubs Rect");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            Rect rect = RandomRect(random);
            Rect other = RandomRect(random);
            Vector2 point = random.Next(4) == 0
                ? new Vector2(rect.x + rect.width, rect.y + Samples.Uniform(random, 0f, Math.Abs(rect.height)))
                : new Vector2(Samples.Next(random, 200f), Samples.Next(random, 200f));
            string Input() => string.Create(CultureInfo.InvariantCulture, $"{Describe(rect)} {Describe(other)} ({point.X:R}, {point.Y:R})");
            mismatches.Check(rect.Contains(point.ToUnity()), UnityRect.Contains(rect, point), Input);
            mismatches.Check(rect.Overlaps(other), UnityRect.Overlaps(rect, other), Input);
            UVector2 center = rect.center;
            Vector2 mine = UnityRect.Center(rect);
            mismatches.Check(center.x, mine.X, Input);
            mismatches.Check(center.y, mine.Y, Input);
        }

        mismatches.AssertNone();
    }

    private static Rect RandomRect(Random random)
    {
        float x = random.Next(32) == 0 ? float.NaN : Samples.Next(random, 200f);
        return new Rect(x, Samples.Next(random, 200f), Samples.Next(random, 100f), Samples.Next(random, 100f));
    }

    private static string Describe(Rect r)
        => string.Create(CultureInfo.InvariantCulture, $"[{r.x:R}, {r.y:R}, {r.width:R}, {r.height:R}]");
}
