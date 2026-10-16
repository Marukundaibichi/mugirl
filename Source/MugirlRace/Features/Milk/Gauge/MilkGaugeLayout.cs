namespace Mugirl.Features.Milk;

// 奶量量杯 Gizmo 的纯几何计算（GUI 坐标：原点左上、Y 向下），不访问游戏状态。
internal static class MilkGaugeLayout
{
    internal const float MinMilkThreshold = 0.1f;

    // 美术贴图（1264×919 像素）中奶槽窗口的像素区域；贴图整张参与绘制。
    internal const float SourceTextureWidth = 1264f;
    internal const float SourceTextureHeight = 919f;
    internal const float GaugeDrawScale = 0.92f;

    // 拖动阈值线时，线两侧 8 像素、奶槽上下各 7 像素都算命中。
    private const float ThresholdGrabHalfWidth = 8f;
    private const float SlotGrabMarginY = 7f;

    internal static Rect GaugeSourceRect => new(0f, 0f, SourceTextureWidth, SourceTextureHeight);

    internal static Rect MilkSlotSourceRect => new(180f, 524f, 922f, 151f);

    // 美术量杯在 Gizmo 中的绘制区域：按贴图宽高比铺满后居中缩小到 92%。
    internal static Rect ArtGaugeRect(Rect gizmoRect)
    {
        Rect source = GaugeSourceRect;
        return ScaleRectCentered(FillRectCentered(gizmoRect, source.width / source.height), GaugeDrawScale);
    }

    // 美术量杯中奶槽的绘制区域。
    internal static Rect ArtMilkSlotRect(Rect gaugeRect) => SourceRectToDrawRect(MilkSlotSourceRect, GaugeSourceRect, gaugeRect);

    // 按宽高比铺满 bounds（较短边对齐、另一边溢出），并以 bounds 中心居中。
    internal static Rect FillRectCentered(Rect bounds, float aspect)
    {
        float width = bounds.width;
        float height = width / aspect;
        if (height < bounds.height)
        {
            height = bounds.height;
            width = height * aspect;
        }

        return new Rect(bounds.x + (bounds.width - width) / 2f, bounds.y + (bounds.height - height) / 2f, width, height);
    }

    internal static Rect ScaleRectCentered(Rect rect, float scale)
    {
        float width = rect.width * scale;
        float height = rect.height * scale;
        return new Rect(rect.x + (rect.width - width) / 2f, rect.y + (rect.height - height) / 2f, width, height);
    }

    // 贴图像素区域 sourceRect（相对裁剪区域 sourceCropRect）映射到绘制区域 drawRect 中的位置。
    internal static Rect SourceRectToDrawRect(Rect sourceRect, Rect sourceCropRect, Rect drawRect)
    {
        return new Rect(
            drawRect.x + drawRect.width * ((sourceRect.x - sourceCropRect.x) / sourceCropRect.width),
            drawRect.y + drawRect.height * ((sourceRect.y - sourceCropRect.y) / sourceCropRect.height),
            drawRect.width * (sourceRect.width / sourceCropRect.width),
            drawRect.height * (sourceRect.height / sourceCropRect.height));
    }

    internal static float ThresholdX(Rect milkSlotRect, float threshold)
    {
        return milkSlotRect.x + milkSlotRect.width * UnityMathf.Clamp(threshold, MinMilkThreshold, 1f);
    }

    internal static float ThresholdFromMouse(Rect milkSlotRect, float mouseX)
    {
        return UnityMathf.Clamp((mouseX - milkSlotRect.x) / milkSlotRect.width, MinMilkThreshold, 1f);
    }

    internal static Rect ThresholdLineRect(Rect milkSlotRect, float threshold)
    {
        float thresholdX = ThresholdX(milkSlotRect, threshold);
        return new Rect(thresholdX - 1f, milkSlotRect.y - 2f, 2f, milkSlotRect.height + 4f);
    }

    internal static Rect ThresholdGrabRect(Rect milkSlotRect, float threshold)
    {
        float thresholdX = ThresholdX(milkSlotRect, threshold);
        return new Rect(thresholdX - ThresholdGrabHalfWidth, milkSlotRect.y - SlotGrabMarginY, ThresholdGrabHalfWidth * 2f, milkSlotRect.height + SlotGrabMarginY * 2f);
    }

    internal static Rect SlotGrabRect(Rect milkSlotRect)
    {
        return new Rect(milkSlotRect.x, milkSlotRect.y - SlotGrabMarginY, milkSlotRect.width, milkSlotRect.height + SlotGrabMarginY * 2f);
    }

    // 左右各收窄 inset，高度不变。
    internal static Rect InsetHorizontal(Rect rect, float inset)
    {
        return new Rect(rect.x + inset, rect.y, rect.width - inset * 2f, rect.height);
    }

    // 名字条可滚动的最大距离：各单元格（宽度不超过名字条）加间距的总宽减去名字条宽度，不小于 0。
    internal static float MaxNameScroll(List<float> cellWidths, float stripWidth, float cellSpacing)
    {
        float totalWidth = 0f;
        for (int i = 0; i < cellWidths.Count; i++)
        {
            totalWidth += UnityMathf.Min(cellWidths[i], stripWidth) + cellSpacing;
        }

        return UnityMathf.Max(0f, totalWidth - cellSpacing - stripWidth);
    }
}
