using System.Diagnostics.CodeAnalysis;
using Mugirl.Platform.Ui;

namespace Mugirl.Features.Milk;

// 奶量量杯 Gizmo：选中雪牛娘时显示乳汁饱满度和自动挤奶阈值，可拖动阈值线。
// 多选雪牛娘时通过 GroupsWith/MergeWith 由原版 GizmoGridDrawer 合并为一格，
// 悬停后在其上方弹出与量杯等宽的单行名字条，可切换查看对象或统一调整阈值（见 Gizmo_MilkGauge.Popup.cs）。
public sealed partial class Gizmo_MilkGauge : Gizmo
{
    private const int MainTooltipSeed = 129734551;
    private const int ThresholdDragControlSeed = 129734552;

    private const float GizmoWidth = 158f;
    private const float GizmoHeight = 75f;

    private static readonly Color EmptySlotColor = new(0.03f, 0.03f, 0.03f, 0.72f);
    private static readonly Color MilkFillColor = UnityColor.White;
    private static readonly Color ThresholdColor = new(1f, 0.28f, 0.22f);

    private readonly CompMilkProduction comp;
    private readonly string label;
    private readonly string desc;

    // 多选合并成员：仅组头实例（本帧分组中的第一个 Gizmo）在 MergeWith 时收集。Gizmo 由 GetGizmos 每帧重建，列表不跨帧缓存。
    private List<CompMilkProduction>? mergedComps;

    public Gizmo_MilkGauge(CompMilkProduction comp, string label, string desc)
    {
        this.comp = comp;
        this.label = label;
        this.desc = desc;
    }

    private static bool UseVanillaMilkGauge => MugirlMod.Settings?.enableVanillaMilkGauge == true;

    [MemberNotNullWhen(true, nameof(mergedComps))]
    private bool Merged => mergedComps != null && mergedComps.Count > 1;

    // 弹窗当前查看的成员；未选择或已不在选中集时回退为组头自身。
    private CompMilkProduction DisplayComp
    {
        get
        {
            CompMilkProduction? selected = MilkGaugePopupSession.DisplayComp;
            return selected != null && mergedComps != null && mergedComps.Contains(selected) ? selected : comp;
        }
    }

    // 同类奶量 Gizmo 归入一组，原版网格每组合并为一个绘制代表。
    public override bool GroupsWith(Gizmo other) => other is Gizmo_MilkGauge;

    // 组头实例收集其余成员的组件，供名字条切换与全选阈值使用。
    public override void MergeWith(Gizmo other)
    {
        if (other is not Gizmo_MilkGauge milkGauge)
        {
            return;
        }

        mergedComps ??= [comp];
        if (!mergedComps.Contains(milkGauge.comp))
        {
            mergedComps.Add(milkGauge.comp);
        }
    }

    public override float GetWidth(float maxWidth) => GizmoWidth;

    public override GizmoResult GizmoOnGUI(UVector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        Rect totalRect = new(topLeft.x, topLeft.y, GetWidth(maxWidth), GizmoHeight);
        Widgets.DrawWindowBackground(totalRect);

        float fullness = UnityMathf.Clamp01(DisplayComp.Fullness);
        bool interacted = UseVanillaMilkGauge
            ? DrawVanillaGauge(totalRect, fullness)
            : DrawArtGauge(MilkGaugeLayout.ArtGaugeRect(totalRect), fullness);

        // Tooltip 文本会随奶量变化，必须使用稳定 ID 避免悬浮提示闪烁。
        TooltipHandler.TipRegion(totalRect, new TipSignal(GetMainTooltip, StableControlId(MainTooltipSeed)));

        UpdateMergedPopup(totalRect);

        if (interacted)
        {
            return new GizmoResult(GizmoState.Interacted, UiEvent.CurrentForGameApi);
        }

        return new GizmoResult(Mouse.IsOver(totalRect) ? GizmoState.Mouseover : GizmoState.Clear);
    }

    private bool DrawArtGauge(Rect gaugeRect, float fullness)
    {
        Rect milkSlotRect = MilkGaugeLayout.ArtMilkSlotRect(gaugeRect);
        Widgets.DrawBoxSolid(milkSlotRect, EmptySlotColor);

        float fillWidth = milkSlotRect.width * fullness;
        if (fillWidth > 0f)
        {
            Widgets.DrawBoxSolid(new Rect(milkSlotRect.x, milkSlotRect.y, fillWidth, milkSlotRect.height), MilkFillColor);
        }

        // 量杯贴图盖在奶液上，奶槽窗口透明；贴图整张绘制并强制白色，不受外层 GUI 颜色影响。
        Texture2D? texture = MilkGaugeTextures.Gauge;
        if (texture is not null)
        {
            UiDraw.Texture(gaugeRect, texture, UnityColor.White);
        }
        else
        {
            Widgets.DrawBox(gaugeRect);
        }

        bool interacted = HandleThresholdDrag(milkSlotRect);
        DrawThresholdLine(milkSlotRect);
        return interacted;
    }

    private bool DrawVanillaGauge(Rect totalRect, float fullness)
    {
        Rect innerRect = totalRect.ContractedBy(6f);
        Rect labelRect = new(innerRect.x, innerRect.y, innerRect.width, 24f);
        Rect milkSlotRect = new(innerRect.x, innerRect.yMax - 28f, innerRect.width, 24f);

        using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft))
        {
            Widgets.Label(labelRect, label ?? string.Empty);
        }

        Rect interactiveRect = Widgets.FillableBar(milkSlotRect, fullness);
        using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
        {
            Widgets.Label(milkSlotRect, fullness.ToStringPercent());
        }

        bool interacted = HandleThresholdDrag(interactiveRect);
        DrawThresholdLine(interactiveRect);
        return interacted;
    }

    private string GetMainTooltip()
    {
        CompMilkProduction displayComp = DisplayComp;
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
        if (!MilkGaugeThresholdDrag.TryHandle(milkSlotRect, DisplayComp.MilkThreshold, StableControlId(ThresholdDragControlSeed), out float threshold))
        {
            return false;
        }

        if (Merged && MilkGaugePopupSession.ApplyToAll)
        {
            // 全选模式：一次拖动统一所有选中雪牛娘的阈值。
            for (int i = 0; i < mergedComps.Count; i++)
            {
                mergedComps[i].MilkThreshold = threshold;
            }

            return true;
        }

        DisplayComp.MilkThreshold = threshold;
        return true;
    }

    private void DrawThresholdLine(Rect milkSlotRect)
    {
        Widgets.DrawBoxSolid(MilkGaugeLayout.ThresholdLineRect(milkSlotRect, DisplayComp.MilkThreshold), ThresholdColor);
    }

    private int StableControlId(int seed) => Gen.HashCombineInt(DisplayComp.parent.thingIDNumber, seed);
}
