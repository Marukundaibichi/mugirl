using Mugirl.Platform;
using Mugirl.Platform.Ui;

namespace Mugirl.Features.Milk;

// 多选合并时的名字条弹窗：与量杯等宽的单行分段框，“全选”固定最左，名字放不下时左右边缘悬停自动滚动。
// 点击名字切换查看对象；开启“全选”后拖动阈值统一生效。会话状态见 MilkGaugePopupSession。
public sealed partial class Gizmo_MilkGauge
{
    // 名字条弹窗固定 ID；同屏同一时刻只存在一个奶量 Gizmo 组，不会与其他 ImmediateWindow 冲突。
    private const int MergedPopupWindowId = 613107;

    // 截图模式或主标签页遮挡会让 Gizmo 停止绘制；超过这么多引擎帧未请求弹窗即结束会话。
    private const int PopupStaleFrames = 2;

    private const float PopupPadding = 4f;
    private const float PopupGap = 8f;
    private const float PopupRowHeight = 24f;
    private const float PopupCellSpacing = 4f;
    private const float PopupCellPaddingX = 6f;
    private const float PopupMinCellWidth = 28f;
    private const float PopupArrowWidth = 12f;
    private const float PopupScrollSpeed = 120f;
    private const float PopupMaxScrollDeltaSeconds = 0.05f;

    private static readonly Color ScrollTrackColor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color ScrollThumbColor = new(1f, 1f, 1f, 0.35f);
    private static readonly Color PopupAccentColor = new(0.96f, 0.72f, 0.77f);
    private static readonly Color PopupDisabledArrowColor = new(1f, 1f, 1f, 0.18f);
    private static readonly Color PopupSelectedColor = new(0.48f, 0.32f, 0.40f, 0.65f);
    private static readonly Color PopupDividerColor = new(0.70f, 0.78f, 0.84f, 0.22f);
    private static readonly Color PopupActiveTextColor = new(1f, 0.89f, 0.91f);
    private static readonly Color PopupInactiveTextColor = new(0.85f, 0.89f, 0.92f);

    // 悬停弹出名字条；鼠标离开 Gizmo 与弹窗之间的悬停走廊时结束会话。
    private void UpdateMergedPopup(Rect gizmoRect)
    {
        if (!Merged)
        {
            MilkGaugePopupSession.End();
            return;
        }

        int frame = EngineClock.FrameCount;
        if (MilkGaugePopupSession.IsOpen && frame - MilkGaugePopupSession.LastRequestFrame > PopupStaleFrames)
        {
            MilkGaugePopupSession.End();
        }

        Rect popupRect = CalcPopupRect(gizmoRect);

        // 留白也属于已打开弹窗的悬停走廊，跨过间隙时不会关闭或重置选择。不能改用 Mouse.IsOver：
        // 鼠标进入 Super 层弹窗后它对窗口外区域返回 false，弹窗会立刻关闭。
        Rect hoverRect = new(gizmoRect.x, popupRect.y, gizmoRect.width, gizmoRect.yMax - popupRect.y);
        bool hovered = Mouse.IsOver(gizmoRect) || (MilkGaugePopupSession.IsOpen && UnityRect.Contains(hoverRect, UiEvent.MousePosition));
        if (!hovered)
        {
            MilkGaugePopupSession.End();
            return;
        }

        if (!MilkGaugePopupSession.IsOpen)
        {
            MilkGaugePopupSession.Begin();
        }

        MilkGaugePopupSession.LastRequestFrame = frame;
        float popupWidth = popupRect.width;
        GameUtility.Windows?.ImmediateWindow(MergedPopupWindowId, popupRect, WindowLayer.Super, () => DrawPopupContent(popupWidth));
    }

    // 与量杯等宽、左缘对齐；视觉留白与悬停判定分开处理。
    private static Rect CalcPopupRect(Rect gizmoRect)
    {
        float height = PopupRowHeight + PopupPadding * 2f;
        return new Rect(gizmoRect.x, gizmoRect.y - height - PopupGap, GizmoWidth, height);
    }

    // 在 ImmediateWindow 内绘制，坐标系为弹窗局部（原版 Gizmo_RoomStats 同款做法）。
    private void DrawPopupContent(float popupWidth)
    {
        if (!Merged)
        {
            return;
        }

        using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter, false))
        {
            float innerWidth = popupWidth - PopupPadding * 2f;
            Widgets.BeginGroup(new Rect(PopupPadding, PopupPadding, innerWidth, PopupRowHeight));
            try
            {
                DrawPopupRow(mergedComps, innerWidth);
            }
            finally
            {
                Widgets.EndGroup();
            }
        }
    }

    private void DrawPopupRow(List<CompMilkProduction> members, float innerWidth)
    {
        // “全选”固定最左，不随名字滚动。
        string applyAllLabel = "Mugirl.Milk.Gauge.ApplyAll.Label".Translate();
        Rect applyAllRect = new(0f, 0f, PopupCellWidth(applyAllLabel), PopupRowHeight);
        DrawPopupCell(applyAllRect, applyAllLabel, MilkGaugePopupSession.ApplyToAll);
        TooltipHandler.TipRegion(applyAllRect, "Mugirl.Milk.Gauge.ApplyAll.Desc".Translate());
        if (Widgets.ButtonInvisible(applyAllRect))
        {
            MilkGaugePopupSession.ApplyToAll = !MilkGaugePopupSession.ApplyToAll;
        }

        Widgets.DrawBoxSolid(new Rect(applyAllRect.xMax + PopupCellSpacing / 2f, 5f, 1f, PopupRowHeight - 10f), PopupDividerColor);

        // 每次绘制只测量一遍名字宽度，滚动范围、箭头与名字条共用。
        MeasureMemberCells(members);
        Rect namesRect = new(applyAllRect.xMax + PopupCellSpacing, 0f, innerWidth - applyAllRect.xMax - PopupCellSpacing, PopupRowHeight);

        // 仅溢出时预留独立箭头，避免点击名字时触发边缘滚动。
        if (MaxNameScroll(namesRect.width) > 0f)
        {
            Rect leftArrow = new(namesRect.x, 0f, PopupArrowWidth, PopupRowHeight);
            Rect rightArrow = new(namesRect.xMax - PopupArrowWidth, 0f, PopupArrowWidth, PopupRowHeight);
            namesRect = MilkGaugeLayout.InsetHorizontal(namesRect, PopupArrowWidth);
            float maxScroll = MaxNameScroll(namesRect.width);
            DrawScrollArrow(leftArrow, -1, maxScroll);
            DrawScrollArrow(rightArrow, 1, maxScroll);
        }

        float stripMaxScroll = MaxNameScroll(namesRect.width);
        MilkGaugePopupSession.NameScroll = UnityMathf.Clamp(MilkGaugePopupSession.NameScroll, 0f, stripMaxScroll);

        // 分组自带裁剪，滚动时越界名字不会外漏。
        Widgets.BeginGroup(namesRect);
        try
        {
            DrawNameStrip(members, namesRect.width, stripMaxScroll);
        }
        finally
        {
            Widgets.EndGroup();
        }

        if (stripMaxScroll > 0f)
        {
            DrawScrollProgressLine(namesRect, stripMaxScroll);
        }
    }

    private void DrawNameStrip(List<CompMilkProduction> members, float stripWidth, float maxScroll)
    {
        List<string> names = MilkGaugePopupSession.MemberNames;
        List<float> cellWidths = MilkGaugePopupSession.MemberCellWidths;
        CompMilkProduction displayComp = DisplayComp;
        float cursorX = -UnityMathf.Clamp(MilkGaugePopupSession.NameScroll, 0f, maxScroll);
        Rect lastVisibleRect = default;
        bool hasLastVisible = false;
        for (int i = 0; i < members.Count && i < names.Count; i++)
        {
            string name = names[i];
            float cellWidth = UnityMathf.Min(cellWidths[i], stripWidth);
            Rect cellRect = new(cursorX, 0f, cellWidth, PopupRowHeight);
            if (cellRect.xMax >= 0f && cellRect.x <= stripWidth)
            {
                if (hasLastVisible)
                {
                    float dividerX = (lastVisibleRect.xMax + cellRect.x) / 2f;
                    Widgets.DrawBoxSolid(new Rect(dividerX, 5f, 1f, PopupRowHeight - 10f), PopupDividerColor);
                }

                string shown = DrawPopupCell(cellRect, name, members[i] == displayComp);
                if (shown != name)
                {
                    TooltipHandler.TipRegion(cellRect, name);
                }

                if (Widgets.ButtonInvisible(cellRect))
                {
                    MilkGaugePopupSession.DisplayComp = members[i];
                }

                lastVisibleRect = cellRect;
                hasLastVisible = true;
            }

            cursorX += cellWidth + PopupCellSpacing;
        }
    }

    // 箭头可点击翻动，也可悬停缓慢滚动；只在 Repaint 阶段按帧时长累加，每帧一次。
    private static void DrawScrollArrow(Rect rect, int direction, float maxScroll)
    {
        bool enabled = direction < 0 ? MilkGaugePopupSession.NameScroll > 0f : MilkGaugePopupSession.NameScroll < maxScroll;
        UiDraw.Label(rect, direction < 0 ? "‹" : "›", enabled ? PopupAccentColor : PopupDisabledArrowColor);
        if (enabled)
        {
            if (Widgets.ButtonInvisible(rect))
            {
                MilkGaugePopupSession.NameScroll += direction * PopupMinCellWidth;
            }

            if (GuiPhase.IsRepaint && Mouse.IsOver(rect))
            {
                MilkGaugePopupSession.NameScroll += direction * PopupScrollSpeed * EngineClock.ClampedDeltaTime(PopupMaxScrollDeltaSeconds);
            }
        }

        MilkGaugePopupSession.NameScroll = UnityMathf.Clamp(MilkGaugePopupSession.NameScroll, 0f, maxScroll);
    }

    // 可滚动时在名字条底部画一条细进度线，提示右侧还有更多名字。
    private static void DrawScrollProgressLine(Rect stripRect, float maxScroll)
    {
        float ratio = UnityMathf.Clamp01(MilkGaugePopupSession.NameScroll / maxScroll);
        float thumbWidth = UnityMathf.Max(12f, stripRect.width * (stripRect.width / (stripRect.width + maxScroll)));
        float thumbX = stripRect.x + (stripRect.width - thumbWidth) * ratio;
        Widgets.DrawBoxSolid(new Rect(stripRect.x, stripRect.yMax - 2f, stripRect.width, 2f), ScrollTrackColor);
        Widgets.DrawBoxSolid(new Rect(thumbX, stripRect.yMax - 2f, thumbWidth, 2f), ScrollThumbColor);
    }

    // 单元格外观：悬停高亮、选中/开启态底色与下划线，文字居中，必要时截断。返回实际显示文本。
    private static string DrawPopupCell(Rect cellRect, string label, bool active)
    {
        if (Mouse.IsOver(cellRect))
        {
            Widgets.DrawHighlight(cellRect);
        }

        if (active)
        {
            Widgets.DrawBoxSolid(cellRect, PopupSelectedColor);
            Widgets.DrawBoxSolid(new Rect(cellRect.x + 4f, cellRect.yMax - 3f, cellRect.width - 8f, 2f), PopupAccentColor);
        }

        string shown = label.Truncate(cellRect.width - PopupCellPaddingX * 2f) ?? label;
        UiDraw.Label(cellRect, shown, active ? PopupActiveTextColor : PopupInactiveTextColor);
        return shown;
    }

    private static float PopupCellWidth(string label)
    {
        return UnityMathf.Max(PopupMinCellWidth, Text.CalcSize(label).x + PopupCellPaddingX * 2f);
    }

    private static void MeasureMemberCells(List<CompMilkProduction> members)
    {
        List<string> names = MilkGaugePopupSession.MemberNames;
        List<float> cellWidths = MilkGaugePopupSession.MemberCellWidths;
        names.Clear();
        cellWidths.Clear();
        for (int i = 0; i < members.Count; i++)
        {
            string name = MemberName(members[i]);
            names.Add(name);
            cellWidths.Add(PopupCellWidth(name));
        }
    }

    private static float MaxNameScroll(float stripWidth)
    {
        return MilkGaugeLayout.MaxNameScroll(MilkGaugePopupSession.MemberCellWidths, stripWidth, PopupCellSpacing);
    }

    private static string MemberName(CompMilkProduction memberComp)
    {
        if (memberComp.parent is Pawn pawn)
        {
            return pawn.LabelShortCap;
        }

        return memberComp.parent?.LabelCap ?? "?";
    }
}
