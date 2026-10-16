using Mugirl.Platform;
using Mugirl.Platform.Ui;

namespace Mugirl.Tests.Platform;

/// <summary>Platform 层中不依赖引擎运行状态的部分：帧时长截断、滚轮换算与模组自己的可交互开关。</summary>
public sealed class PlatformTests
{
    [Theory]
    [InlineData(0.016f, 0.05f, 0.016f)]
    [InlineData(0.5f, 0.05f, 0.05f)]
    [InlineData(-0.1f, 0.05f, 0f)]
    [InlineData(float.NaN, 0.05f, 0f)]
    [InlineData(float.PositiveInfinity, 1f / 3f, 1f / 3f)]
    public void ClampDelta_ClampsIntoZeroToMax(float delta, float max, float expected)
        => Bits.Equal(expected, EngineClock.ClampDelta(delta, max));

    [Fact]
    public void ScrollDelta_FollowsHostImguiConversion()
    {
        // 宿主 ImguiDispatch.RunScrollPass：Shift 且 x == 0 时改为 (y, 0)，再逐分量 IL neg 并乘 3。
        Vector2 down = EngineInput.ToImguiScrollDelta(new Vector2(0f, -1f), shift: false);
        Bits.Equal(-0f, down.X, "neg keeps the sign of zero");
        Bits.Equal(3f, down.Y, "scrolling down gives a positive IMGUI delta");

        Vector2 shifted = EngineInput.ToImguiScrollDelta(new Vector2(0f, 1f), shift: true);
        Bits.Equal(-3f, shifted.X);
        Bits.Equal(-0f, shifted.Y);

        Vector2 horizontal = EngineInput.ToImguiScrollDelta(new Vector2(0.5f, 1f), shift: true);
        Bits.Equal(-1.5f, horizontal.X, "a horizontal component disables the shift swap");
        Bits.Equal(-3f, horizontal.Y);
    }

    [Fact]
    public void UiEnabled_ScopesNestAndRestore()
    {
        Assert.True(UiEnabled.IsEnabled);
        using (UiEnabled.DisableIf(true))
        {
            Assert.False(UiEnabled.IsEnabled);
            using (UiEnabled.DisableIf(false))
            {
                Assert.False(UiEnabled.IsEnabled, "DisableIf(false) keeps the outer value");
            }

            using (UiEnabled.Set(true))
            {
                Assert.True(UiEnabled.IsEnabled, "Set(true) re-enables like GUI.enabled = true");
            }

            Assert.False(UiEnabled.IsEnabled);
        }

        Assert.True(UiEnabled.IsEnabled);
        default(UiEnabled.Scope).Dispose();
        Assert.True(UiEnabled.IsEnabled, "a default scope does nothing");
    }
}
