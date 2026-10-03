using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // 奶量量杯自定义 Gizmo：在选中雪牛娘时显示乳汁饱满度和自动挤奶阈值。
    // 多选雪牛娘时通过 GroupsWith/MergeWith 由原版 GizmoGridDrawer 合并为一格，
    // 悬停后在其上方弹出与量杯等宽的单行名字条，可切换查看对象或统一调整阈值。
    public class Gizmo_MilkGauge : Gizmo
    {
        private const int MainTooltipSeed = 129734551;
        private const int ThresholdDragControlSeed = 129734552;
        // 名字条弹窗固定 ID；同屏同一时刻只存在一个奶量 Gizmo 组，不会与其他 ImmediateWindow 冲突。
        private const int MergedPopupWindowId = 613107;
        private const string GaugeTexturePath = "UI/Mugirl_MilkGauge";

        private const float GizmoWidth = 158f;
        private const float GizmoHeight = 75f;
        private const float GaugeDrawScale = 0.92f;
        private const float MinMilkThreshold = 0.1f;

        // 名字条弹窗：与量杯等宽的单行分段框；“全选”固定最左，名字放不下时左右边缘悬停自动滚动。
        private const float PopupPadding = 4f;
        private const float PopupGap = 8f;
        private const float PopupRowHeight = 24f;
        private const float PopupCellSpacing = 4f;
        private const float PopupCellPaddingX = 6f;
        private const float PopupMinCellWidth = 28f;
        private const float PopupArrowWidth = 12f;
        private const float PopupScrollSpeed = 120f;

        private const float SourceTextureWidth = 1264f;
        private const float SourceTextureHeight = 919f;
        private static readonly Rect GaugeSourceCropRect = new Rect(0f, 0f, SourceTextureWidth, SourceTextureHeight);
        private static readonly Rect MilkSlotSourceRect = new Rect(180f, 524f, 922f, 151f);
        private static readonly Rect GaugeTextureCoords = SourceRectToTextureCoords(GaugeSourceCropRect);

        private CompMooHasBodyResource comp;
        private string label;
        private string desc;

        // 多选合并成员列表：仅组头实例（本帧分组中的第一个 Gizmo）在 MergeWith 时收集。
        // Gizmo 由 GetGizmos 每帧重建，列表随帧更新，不跨帧缓存。
        private List<CompMooHasBodyResource> mergedComps;

        // StaticCacheLifecycle: 名字条弹窗的瞬态 UI 状态。Gizmo 实例每帧重建，无法保存跨帧状态，
        // 因此当前查看对象、全选开关、滚动偏移与弹窗会话标记放静态字段；不进入存档。
        // 鼠标离开 Gizmo 与弹窗区域（EndPopupSession）或弹窗连续两帧未被请求时复位；
        // popupDisplayComp 若已不在当前选中集，DisplayComp 会自动回退为组头自身。
        private static CompMooHasBodyResource popupDisplayComp;
        private static bool popupApplyToAll;
        private static float popupNameScroll;
        private static bool popupSessionOpen;
        private static int popupLastRequestFrame = -1;

        private static Texture2D gaugeTexture;
        private static bool triedLoadGaugeTexture;

        private static readonly Color EmptySlotColor = new Color(0.03f, 0.03f, 0.03f, 0.72f);
        private static readonly Color MilkFillColor = Color.white;
        private static readonly Color ThresholdColor = new Color(1f, 0.28f, 0.22f);
        private static readonly Color ScrollTrackColor = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ScrollThumbColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color PopupAccentColor = new Color(0.96f, 0.72f, 0.77f);
        private static readonly Color PopupSelectedColor = new Color(0.48f, 0.32f, 0.40f, 0.65f);
        private static readonly Color PopupDividerColor = new Color(0.70f, 0.78f, 0.84f, 0.22f);

        public Gizmo_MilkGauge(CompMooHasBodyResource comp, string label, string desc)
        {
            this.comp = comp;
            this.label = label;
            this.desc = desc;
        }

        // 同类奶量 Gizmo 归入一组，原版网格每组合并为一个绘制代表。
        public override bool GroupsWith(Gizmo other)
        {
            return other is Gizmo_MilkGauge;
        }

        // 组头实例收集其余成员的组件，供名字条切换与全选阈值使用。
        public override void MergeWith(Gizmo other)
        {
            if (other is Gizmo_MilkGauge milkGauge)
            {
                if (mergedComps == null)
                {
                    mergedComps = new List<CompMooHasBodyResource> { comp };
                }
                if (!mergedComps.Contains(milkGauge.comp))
                {
                    mergedComps.Add(milkGauge.comp);
                }
            }
        }

        private bool Merged => mergedComps != null && mergedComps.Count > 1;

        // 弹窗当前查看的成员；未选择或已不在选中集时回退为组头自身。
        private CompMooHasBodyResource DisplayComp
        {
            get
            {
                if (popupDisplayComp != null && mergedComps != null && mergedComps.Contains(popupDisplayComp))
                {
                    return popupDisplayComp;
                }
                return comp;
            }
        }

        public override float GetWidth(float maxWidth)
        {
            return GizmoWidth;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            float width = GetWidth(maxWidth);
            Rect totalRect = new Rect(topLeft.x, topLeft.y, width, GizmoHeight);
            bool interacted = false;

            // 背景
            Widgets.DrawWindowBackground(totalRect);

            float fullness = Mathf.Clamp01(DisplayComp.Fullness);

            if (UseVanillaMilkGauge)
            {
                interacted = DrawVanillaGauge(totalRect, fullness) || interacted;
            }
            else
            {
                Rect gaugeRect = FillRectCentered(totalRect, GaugeSourceCropRect.width / GaugeSourceCropRect.height);
                gaugeRect = ScaleRectCentered(gaugeRect, GaugeDrawScale);
                interacted = DrawGauge(gaugeRect, fullness) || interacted;
            }

            // Tooltip 文本会随奶量变化，必须使用稳定 ID 避免悬浮提示闪烁。
            TooltipHandler.TipRegion(totalRect, new TipSignal(GetMainTooltip, StableTooltipId(MainTooltipSeed)));

            UpdateMergedPopup(totalRect);

            if (interacted)
            {
                return new GizmoResult(GizmoState.Interacted, Event.current);
            }

            return new GizmoResult(Mouse.IsOver(totalRect) ? GizmoState.Mouseover : GizmoState.Clear);
        }

        private bool DrawGauge(Rect gaugeRect, float fullness)
        {
            Rect milkSlotRect = SourceRectToDrawRect(MilkSlotSourceRect, GaugeSourceCropRect, gaugeRect);
            DrawSolidRect(milkSlotRect, EmptySlotColor);

            float fillWidth = milkSlotRect.width * fullness;
            if (fillWidth > 0f)
            {
                Rect fillRect = new Rect(milkSlotRect.x, milkSlotRect.y, fillWidth, milkSlotRect.height);
                DrawSolidRect(fillRect, MilkFillColor);
            }

            Texture2D texture = GaugeTexture;
            if (texture != null)
            {
                Color oldColor = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTextureWithTexCoords(gaugeRect, texture, GaugeTextureCoords);
                GUI.color = oldColor;
            }
            else
            {
                Widgets.DrawBox(gaugeRect);
            }

            bool interacted = HandleThresholdDrag(milkSlotRect);
            DrawThresholdLine(milkSlotRect, DisplayComp.MilkThreshold);

            return interacted;
        }

        private bool DrawVanillaGauge(Rect totalRect, float fullness)
        {
            Rect innerRect = totalRect.ContractedBy(6f);
            Rect labelRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 24f);
            Rect milkSlotRect = new Rect(innerRect.x, innerRect.yMax - 28f, innerRect.width, 24f);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(labelRect, label ?? string.Empty);

            Rect interactiveRect = Widgets.FillableBar(milkSlotRect, fullness);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(milkSlotRect, fullness.ToStringPercent());

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            bool interacted = HandleThresholdDrag(interactiveRect);
            DrawThresholdLine(interactiveRect, DisplayComp.MilkThreshold);

            return interacted;
        }

        private string GetMainTooltip()
        {
            CompMooHasBodyResource displayComp = DisplayComp;
            string text = "Mugirl.Milk.Gauge.Tooltip".Translate(
                desc,
                displayComp.Fullness.ToStringPercent(),
                displayComp.MilkThreshold.ToStringPercent(),
                displayComp.GetResourceAmountForNextGather(),
                displayComp.GetProductionRateExplanation());
            if (Merged)
            {
                text += "\n\n" + "Mugirl.Milk.Gauge.MultiHint".Translate();
            }
            return text;
        }

        private bool HandleThresholdDrag(Rect milkSlotRect)
        {
            Event current = Event.current;
            float thresholdX = ThresholdX(milkSlotRect, DisplayComp.MilkThreshold);
            Rect hitRect = new Rect(thresholdX - 8f, milkSlotRect.y - 7f, 16f, milkSlotRect.height + 14f);
            Rect clickRect = new Rect(milkSlotRect.x, milkSlotRect.y - 7f, milkSlotRect.width, milkSlotRect.height + 14f);
            int controlId = GUIUtility.GetControlID(StableTooltipId(ThresholdDragControlSeed), FocusType.Passive, milkSlotRect);

            bool overThresholdLine = hitRect.Contains(current.mousePosition);

            if (current.type == EventType.MouseDown && current.button == 0 && (overThresholdLine || clickRect.Contains(current.mousePosition)))
            {
                GUIUtility.hotControl = controlId;
                SetThresholdFromMouse(milkSlotRect, current.mousePosition.x);
                current.Use();
                return true;
            }

            if (GUIUtility.hotControl == controlId && current.type == EventType.MouseDrag)
            {
                SetThresholdFromMouse(milkSlotRect, current.mousePosition.x);
                current.Use();
                return true;
            }

            if (GUIUtility.hotControl == controlId && current.type == EventType.MouseUp && current.button == 0)
            {
                SetThresholdFromMouse(milkSlotRect, current.mousePosition.x);
                GUIUtility.hotControl = 0;
                current.Use();
                return true;
            }

            return false;
        }

        private void SetThresholdFromMouse(Rect milkSlotRect, float mouseX)
        {
            float threshold = Mathf.Clamp((mouseX - milkSlotRect.x) / milkSlotRect.width, MinMilkThreshold, 1f);
            if (Merged && popupApplyToAll)
            {
                // 全选模式：一次拖动统一所有选中雪牛娘的阈值。
                for (int i = 0; i < mergedComps.Count; i++)
                {
                    mergedComps[i].MilkThreshold = threshold;
                }
                return;
            }

            DisplayComp.MilkThreshold = threshold;
        }

        // 悬停弹出名字条：点击名字切换查看对象，最左“全选”开启后阈值统一生效。
        private void UpdateMergedPopup(Rect gizmoRect)
        {
            if (!Merged)
            {
                EndPopupSession();
                return;
            }

            // 截图模式或主标签页遮挡会让 Gizmo 停止绘制，弹窗消失后丢弃上一轮会话状态。
            if (popupSessionOpen && Time.frameCount - popupLastRequestFrame > 2)
            {
                EndPopupSession();
            }

            Rect popupRect = CalcPopupRect(gizmoRect);
            // 留白也属于已打开弹窗的悬停走廊，跨过间隙时不会关闭或重置选择。
            Rect hoverRect = new Rect(gizmoRect.x, popupRect.y, gizmoRect.width, gizmoRect.yMax - popupRect.y);
            bool hovered = Mouse.IsOver(gizmoRect) || (popupSessionOpen && hoverRect.Contains(Event.current.mousePosition));
            if (!hovered)
            {
                EndPopupSession();
                return;
            }

            if (!popupSessionOpen)
            {
                popupSessionOpen = true;
                popupApplyToAll = false;
                popupNameScroll = 0f;
                popupDisplayComp = null;
            }

            popupLastRequestFrame = Time.frameCount;
            MugirlGameUtility.Windows.ImmediateWindow(MergedPopupWindowId, popupRect, WindowLayer.Super, delegate
            {
                DrawPopupContent(popupRect.width);
            });
        }

        private void EndPopupSession()
        {
            if (!popupSessionOpen)
            {
                return;
            }

            popupSessionOpen = false;
            popupApplyToAll = false;
            popupNameScroll = 0f;
        }

        private Rect CalcPopupRect(Rect gizmoRect)
        {
            float height = PopupRowHeight + PopupPadding * 2f;

            // 与量杯等宽、左缘对齐；视觉留白与悬停判定分开处理。
            return new Rect(gizmoRect.x, gizmoRect.y - height - PopupGap, GizmoWidth, height);
        }

        // 在 ImmediateWindow 内绘制，坐标系为弹窗局部（原版 Gizmo_RoomStats 同款做法）。
        private void DrawPopupContent(float popupWidth)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            float innerWidth = popupWidth - PopupPadding * 2f;
            GUI.BeginGroup(new Rect(PopupPadding, PopupPadding, innerWidth, PopupRowHeight));

            // “全选”固定最左，不随名字滚动。
            string applyAllLabel = (string)"Mugirl.Milk.Gauge.ApplyAll.Label".Translate();
            Rect applyAllRect = new Rect(0f, 0f, PopupCellWidth(applyAllLabel), PopupRowHeight);
            DrawPopupCell(applyAllRect, applyAllLabel, popupApplyToAll);
            TooltipHandler.TipRegion(applyAllRect, "Mugirl.Milk.Gauge.ApplyAll.Desc".Translate());
            if (Widgets.ButtonInvisible(applyAllRect))
            {
                popupApplyToAll = !popupApplyToAll;
            }

            Widgets.DrawBoxSolid(new Rect(applyAllRect.xMax + PopupCellSpacing / 2f, 5f, 1f, PopupRowHeight - 10f), PopupDividerColor);

            Rect namesRect = new Rect(applyAllRect.xMax + PopupCellSpacing, 0f,
                innerWidth - applyAllRect.xMax - PopupCellSpacing, PopupRowHeight);
            // 仅溢出时预留独立箭头，避免点击名字时触发边缘滚动。
            if (MaxNameScroll(namesRect.width) > 0f)
            {
                Rect leftArrow = new Rect(namesRect.x, 0f, PopupArrowWidth, PopupRowHeight);
                Rect rightArrow = new Rect(namesRect.xMax - PopupArrowWidth, 0f, PopupArrowWidth, PopupRowHeight);
                namesRect.xMin += PopupArrowWidth;
                namesRect.xMax -= PopupArrowWidth;
                float maxScroll = MaxNameScroll(namesRect.width);
                DrawScrollArrow(leftArrow, -1, maxScroll);
                DrawScrollArrow(rightArrow, 1, maxScroll);
            }
            popupNameScroll = Mathf.Clamp(popupNameScroll, 0f, MaxNameScroll(namesRect.width));

            // BeginGroup 自带裁剪，滚动时越界名字不会外漏。
            GUI.BeginGroup(namesRect);
            DrawNameStrip(namesRect.width);
            GUI.EndGroup();

            if (MaxNameScroll(namesRect.width) > 0f)
            {
                DrawScrollProgressLine(namesRect);
            }
            GUI.EndGroup();
            Text.WordWrap = oldWordWrap;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private void DrawNameStrip(float stripWidth)
        {
            float cursorX = -Mathf.Clamp(popupNameScroll, 0f, MaxNameScroll(stripWidth));
            Rect lastVisibleRect = default;
            bool hasLastVisible = false;
            for (int i = 0; i < mergedComps.Count; i++)
            {
                string name = MemberName(mergedComps[i]);
                float cellWidth = Mathf.Min(PopupCellWidth(name), stripWidth);
                Rect cellRect = new Rect(cursorX, 0f, cellWidth, PopupRowHeight);
                if (cellRect.xMax >= 0f && cellRect.x <= stripWidth)
                {
                    if (hasLastVisible)
                    {
                        float dividerX = (lastVisibleRect.xMax + cellRect.x) / 2f;
                        Widgets.DrawBoxSolid(new Rect(dividerX, 5f, 1f, PopupRowHeight - 10f), PopupDividerColor);
                    }

                    bool active = mergedComps[i] == DisplayComp;
                    string shown = DrawPopupCell(cellRect, name, active);
                    if (shown != name)
                    {
                        TooltipHandler.TipRegion(cellRect, name);
                    }
                    if (Widgets.ButtonInvisible(cellRect))
                    {
                        popupDisplayComp = mergedComps[i];
                    }

                    lastVisibleRect = cellRect;
                    hasLastVisible = true;
                }

                cursorX += cellWidth + PopupCellSpacing;
            }
        }

        // 箭头可点击翻动，也可悬停缓慢滚动；只在 Repaint 事件累加时间。
        private void DrawScrollArrow(Rect rect, int direction, float maxScroll)
        {
            bool enabled = direction < 0 ? popupNameScroll > 0f : popupNameScroll < maxScroll;
            Color oldColor = GUI.color;
            GUI.color = enabled ? PopupAccentColor : new Color(1f, 1f, 1f, 0.18f);
            Widgets.Label(rect, direction < 0 ? "‹" : "›");
            GUI.color = oldColor;
            if (enabled)
            {
                if (Widgets.ButtonInvisible(rect))
                {
                    popupNameScroll += direction * PopupMinCellWidth;
                }
                if (Event.current.type == EventType.Repaint && Mouse.IsOver(rect))
                {
                    popupNameScroll += direction * PopupScrollSpeed * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                }
            }
            popupNameScroll = Mathf.Clamp(popupNameScroll, 0f, maxScroll);
        }

        // 可滚动时在名字条底部画一条细进度线，提示右侧还有更多名字。
        private void DrawScrollProgressLine(Rect stripRect)
        {
            float maxScroll = MaxNameScroll(stripRect.width);
            float ratio = Mathf.Clamp01(popupNameScroll / maxScroll);
            float thumbWidth = Mathf.Max(12f, stripRect.width * (stripRect.width / (stripRect.width + maxScroll)));
            float thumbX = stripRect.x + (stripRect.width - thumbWidth) * ratio;
            Rect trackRect = new Rect(stripRect.x, stripRect.yMax - 2f, stripRect.width, 2f);
            Rect thumbRect = new Rect(thumbX, stripRect.yMax - 2f, thumbWidth, 2f);
            Widgets.DrawBoxSolid(trackRect, ScrollTrackColor);
            Widgets.DrawBoxSolid(thumbRect, ScrollThumbColor);
        }

        private float MaxNameScroll(float stripWidth)
        {
            float totalWidth = 0f;
            for (int i = 0; i < mergedComps.Count; i++)
            {
                totalWidth += Mathf.Min(PopupCellWidth(MemberName(mergedComps[i])), stripWidth) + PopupCellSpacing;
            }
            return Mathf.Max(0f, totalWidth - PopupCellSpacing - stripWidth);
        }

        // 单元格外观：悬停淡蓝高亮、选中/开启态选中高亮，文字居中，必要时截断。返回实际显示文本。
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
            Color oldColor = GUI.color;
            GUI.color = active ? new Color(1f, 0.89f, 0.91f) : new Color(0.85f, 0.89f, 0.92f);
            Widgets.Label(cellRect, shown);
            GUI.color = oldColor;
            return shown;
        }

        private static float PopupCellWidth(string label)
        {
            return Mathf.Max(PopupMinCellWidth, Text.CalcSize(label).x + PopupCellPaddingX * 2f);
        }

        private static string MemberName(CompMooHasBodyResource memberComp)
        {
            Pawn pawn = memberComp.parent as Pawn;
            if (pawn != null)
            {
                return pawn.LabelShortCap;
            }
            return memberComp.parent?.LabelCap ?? "?";
        }

        private int StableTooltipId(int seed)
        {
            return Gen.HashCombineInt(DisplayComp.parent.thingIDNumber, seed);
        }

        private void DrawThresholdLine(Rect milkSlotRect, float threshold)
        {
            float thresholdX = ThresholdX(milkSlotRect, threshold);
            DrawSolidRect(new Rect(thresholdX - 1f, milkSlotRect.y - 2f, 2f, milkSlotRect.height + 4f), ThresholdColor);
        }

        private static float ThresholdX(Rect milkSlotRect, float threshold)
        {
            return milkSlotRect.x + milkSlotRect.width * Mathf.Clamp(threshold, MinMilkThreshold, 1f);
        }

        private static Rect SourceRectToDrawRect(Rect sourceRect, Rect sourceCropRect, Rect drawRect)
        {
            return new Rect(
                drawRect.x + drawRect.width * ((sourceRect.x - sourceCropRect.x) / sourceCropRect.width),
                drawRect.y + drawRect.height * ((sourceRect.y - sourceCropRect.y) / sourceCropRect.height),
                drawRect.width * (sourceRect.width / sourceCropRect.width),
                drawRect.height * (sourceRect.height / sourceCropRect.height));
        }

        private static Rect FillRectCentered(Rect bounds, float aspect)
        {
            float width = bounds.width;
            float height = width / aspect;
            if (height < bounds.height)
            {
                height = bounds.height;
                width = height * aspect;
            }

            return new Rect(
                bounds.x + (bounds.width - width) / 2f,
                bounds.y + (bounds.height - height) / 2f,
                width,
                height);
        }

        private static Rect ScaleRectCentered(Rect rect, float scale)
        {
            float width = rect.width * scale;
            float height = rect.height * scale;
            return new Rect(
                rect.x + (rect.width - width) / 2f,
                rect.y + (rect.height - height) / 2f,
                width,
                height);
        }

        private static Rect SourceRectToTextureCoords(Rect sourceRect)
        {
            return new Rect(
                sourceRect.x / SourceTextureWidth,
                1f - ((sourceRect.y + sourceRect.height) / SourceTextureHeight),
                sourceRect.width / SourceTextureWidth,
                sourceRect.height / SourceTextureHeight);
        }

        private static Texture2D GaugeTexture
        {
            get
            {
                if (!triedLoadGaugeTexture)
                {
                    gaugeTexture = ContentFinder<Texture2D>.Get(GaugeTexturePath, false);
                    triedLoadGaugeTexture = true;
                }

                return gaugeTexture;
            }
        }

        private static bool UseVanillaMilkGauge
        {
            get
            {
                return MugirlMod.Settings != null && MugirlMod.Settings.enableVanillaMilkGauge;
            }
        }

        private void DrawSolidRect(Rect rect, Color color)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = oldColor;
        }
    }
}
