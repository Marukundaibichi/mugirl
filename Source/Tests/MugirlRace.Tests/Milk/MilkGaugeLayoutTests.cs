using System.Collections.Generic;
using Mugirl.Features.Milk;

namespace Mugirl.Tests.Milk;

/// <summary>奶量量杯 Gizmo 的几何：美术贴图铺放、奶槽映射、阈值线与名字条滚动范围。</summary>
public sealed class MilkGaugeLayoutTests
{
    private static readonly Rect GizmoRect = new(100f, 200f, 158f, 75f);

    [Fact]
    public void FillRectCentered_CoversBoundsAndKeepsAspect()
    {
        Rect bounds = new(0f, 0f, 158f, 75f);
        float aspect = 1264f / 919f;
        Rect filled = MilkGaugeLayout.FillRectCentered(bounds, aspect);
        Assert.True(filled.width >= bounds.width - 1e-4f && filled.height >= bounds.height - 1e-4f, "covers the bounds");
        Assert.Equal(aspect, filled.width / filled.height, 4);
        Assert.Equal(bounds.x + bounds.width / 2f, filled.x + filled.width / 2f, 4);
        Assert.Equal(bounds.y + bounds.height / 2f, filled.y + filled.height / 2f, 4);
    }

    [Fact]
    public void ArtGaugeRect_IsCenteredAt92Percent()
    {
        Rect filled = MilkGaugeLayout.FillRectCentered(GizmoRect, MilkGaugeLayout.SourceTextureWidth / MilkGaugeLayout.SourceTextureHeight);
        Rect art = MilkGaugeLayout.ArtGaugeRect(GizmoRect);
        Assert.Equal(filled.width * MilkGaugeLayout.GaugeDrawScale, art.width, 3);
        Assert.Equal(filled.height * MilkGaugeLayout.GaugeDrawScale, art.height, 3);
        Assert.Equal(GizmoRect.x + GizmoRect.width / 2f, art.x + art.width / 2f, 3);
        Assert.Equal(GizmoRect.y + GizmoRect.height / 2f, art.y + art.height / 2f, 3);
    }

    [Fact]
    public void ArtMilkSlotRect_MapsTexturePixelsProportionally()
    {
        Rect gauge = new(10f, 20f, 1264f / 4f, 919f / 4f);
        Rect slot = MilkGaugeLayout.ArtMilkSlotRect(gauge);
        Assert.Equal(10f + 180f / 4f, slot.x, 3);
        Assert.Equal(20f + 524f / 4f, slot.y, 3);
        Assert.Equal(922f / 4f, slot.width, 3);
        Assert.Equal(151f / 4f, slot.height, 3);
    }

    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(2f, 1f)]
    public void ThresholdX_ClampsToMinimumAndFull(float threshold, float expectedFraction)
    {
        Rect slot = new(40f, 10f, 200f, 20f);
        Bits.Equal(40f + 200f * expectedFraction, MilkGaugeLayout.ThresholdX(slot, threshold));
    }

    [Fact]
    public void ThresholdFromMouse_IsInverseOfThresholdXInsideRange()
    {
        Rect slot = new(40f, 10f, 200f, 20f);
        for (float threshold = 0.1f; threshold <= 1f; threshold += 0.05f)
        {
            float x = MilkGaugeLayout.ThresholdX(slot, threshold);
            Assert.Equal(threshold, MilkGaugeLayout.ThresholdFromMouse(slot, x), 5);
        }

        Bits.Equal(0.1f, MilkGaugeLayout.ThresholdFromMouse(slot, -500f));
        Bits.Equal(1f, MilkGaugeLayout.ThresholdFromMouse(slot, 5000f));
    }

    [Fact]
    public void GrabRects_ExtendAroundSlotAndLine()
    {
        Rect slot = new(40f, 10f, 200f, 20f);
        Rect line = MilkGaugeLayout.ThresholdLineRect(slot, 0.5f);
        Rect grab = MilkGaugeLayout.ThresholdGrabRect(slot, 0.5f);
        Rect slotGrab = MilkGaugeLayout.SlotGrabRect(slot);
        Assert.Equal(new Rect(139f, 8f, 2f, 24f), line);
        Assert.Equal(new Rect(132f, 3f, 16f, 34f), grab);
        Assert.Equal(new Rect(40f, 3f, 200f, 34f), slotGrab);
        Assert.True(UnityRect.Contains(grab, new Vector2(140f, 5f)));
        Assert.False(UnityRect.Contains(slotGrab, new Vector2(240f, 20f)), "right edge is excluded");
    }

    [Fact]
    public void InsetHorizontal_KeepsHeight()
    {
        Assert.Equal(new Rect(22f, 0f, 76f, 24f), MilkGaugeLayout.InsetHorizontal(new Rect(10f, 0f, 100f, 24f), 12f));
    }

    [Fact]
    public void MaxNameScroll_CountsCellsCappedAtStripWidth()
    {
        var widths = new List<float> { 40f, 60f, 300f };
        // 40 + 60 + min(300, 120) + 两个间距 4 = 228，减去名字条 120。
        Bits.Equal(108f, MilkGaugeLayout.MaxNameScroll(widths, 120f, 4f));
        Bits.Equal(0f, MilkGaugeLayout.MaxNameScroll(new List<float> { 30f, 30f }, 120f, 4f));
        Bits.Equal(0f, MilkGaugeLayout.MaxNameScroll(new List<float>(), 120f, 4f));
    }
}
