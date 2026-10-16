namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企终端的纯布局计算：按钮流式排布、目录列表与详情的分栏、卡片网格与可见行范围。
/// 不访问游戏状态，可离线测试；数值与 1.6 的同名局部方法相同。
/// </summary>
internal static class CorporateLayout
{
    /// <summary>流式按钮的高度。</summary>
    internal const float FlowButtonHeight = 30f;

    /// <summary>流式按钮换行时的行距。</summary>
    internal const float FlowRowStride = 36f;

    /// <summary>流式按钮之间的水平间距。</summary>
    internal const float FlowGap = 6f;

    /// <summary>卡片内容的左右边距（按钮从 x = 12 开始，右侧留 12）。</summary>
    internal const float FlowMargin = 12f;

    /// <summary>
    /// 在宽 <paramref name="width"/> 的卡片中放置下一个按钮：宽度不超过可用宽度，放不下时换行到 x = 12；
    /// 返回按钮矩形并推进 <paramref name="x"/>（换行时也推进 <paramref name="y"/>）。
    /// </summary>
    internal static Rect FlowNext(ref float x, ref float y, float width, float buttonWidth)
    {
        buttonWidth = UnityMathf.Min(buttonWidth, width - 2f * FlowMargin);
        if (x > FlowMargin && x + buttonWidth > width - FlowMargin)
        {
            x = FlowMargin;
            y += FlowRowStride;
        }

        Rect rect = new(x, y, buttonWidth, FlowButtonHeight);
        x += buttonWidth + FlowGap;
        return rect;
    }

    /// <summary>按 <see cref="FlowNext"/> 的规则排布 <paramref name="widths"/> 后的总高度；<paramref name="width"/> 为去掉边距后的可用宽度。</summary>
    internal static float FlowHeight(float width, ReadOnlySpan<float> widths)
    {
        float x = 0f;
        float height = FlowButtonHeight;
        foreach (float requested in widths)
        {
            float button = UnityMathf.Min(requested, width);
            if (x > 0f && x + button > width)
            {
                x = 0f;
                height += FlowRowStride;
            }

            x += button + FlowGap;
        }

        return height;
    }

    /// <summary>
    /// 目录页的列表与详情分栏：宽度至少 720 时左右排列（详情宽 272–340），否则上下排列
    /// （详情高度：订购页 602、交易页 364；列表至少 132 高）。
    /// </summary>
    internal static void CatalogSplit(Rect rect, bool ordering, out Rect listing, out Rect detail)
    {
        if (rect.width >= 720f)
        {
            float detailWidth = UnityMathf.Clamp(rect.width * 0.34f, 272f, 340f);
            listing = new Rect(rect.x, rect.y, rect.width - detailWidth - 16f, rect.height);
            detail = new Rect(listing.xMax + 16f, rect.y, detailWidth, rect.height);
        }
        else
        {
            float detailHeight = ordering ? 602f : 364f;
            listing = new Rect(rect.x, rect.y, rect.width, UnityMathf.Max(132f, rect.height - detailHeight - 14f));
            detail = new Rect(rect.x, listing.yMax + 14f, rect.width, detailHeight);
        }
    }

    /// <summary>
    /// 目录卡片网格：列表模式一列、行距 80；网格模式按 184 宽分 1–4 列、行距 173，列间距 10。
    /// <paramref name="view"/> 是滚动内容区（至少与视口同高）。
    /// </summary>
    internal static void CatalogGrid(Rect rect, int count, bool list, out int columns, out float cardWidth, out float stride, out Rect view)
    {
        float viewWidth = UnityMathf.Max(120f, rect.width - 16f);
        columns = list ? 1 : UnityMathf.Clamp(UnityMathf.FloorToInt((viewWidth + 10f) / 184f), 1, 4);
        cardWidth = (viewWidth - (columns - 1) * 10f) / columns;
        stride = list ? 80f : 173f;
        view = new Rect(0f, 0f, viewWidth, UnityMathf.Max(rect.height, RowCount(count, columns) * stride));
    }

    /// <summary><paramref name="count"/> 项按 <paramref name="columns"/> 列排布的行数（向上取整，整数运算）。</summary>
    internal static int RowCount(int count, int columns) => count <= 0 ? 0 : ((count - 1) / columns) + 1;

    /// <summary>
    /// 滚动位置 <paramref name="scrollY"/> 下需要绘制的项范围 [first, last)，上下各多留一行，
    /// 滚动位置先钳制到有效范围。
    /// </summary>
    internal static void VisibleRange(int count, int columns, float stride, float scrollY, float viewportHeight, out int first, out int last)
    {
        if (count <= 0)
        {
            first = last = 0;
            return;
        }

        int rows = RowCount(count, columns);
        float maximumScroll = UnityMathf.Max(0f, rows * stride - viewportHeight);
        scrollY = UnityMathf.Clamp(scrollY, 0f, maximumScroll);
        int firstRow = Math.Max(0, UnityMathf.FloorToInt(scrollY / stride) - 1);
        int lastRow = Math.Min(rows, UnityMathf.CeilToInt((scrollY + viewportHeight) / stride) + 1);
        first = Math.Min(count, firstRow * columns);
        last = Math.Min(count, Math.Max(first, lastRow * columns));
    }
}
