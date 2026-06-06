using UnityEngine;
using Verse;

namespace MooGirl
{
    // 奶量量杯自定义 Gizmo：在选中雪牛娘时显示乳汁饱满度和自动挤奶阈值
    public class Gizmo_MilkGauge : Gizmo
    {
        private const int MainTooltipSeed = 129734551;
        private const int IncreaseTooltipSeed = 129734552;
        private const int DecreaseTooltipSeed = 129734553;

        private CompMooHasBodyResource comp;
        private string label;
        private string desc;

        private static readonly Color CupBackColor = new Color(0.06f, 0.13f, 0.15f, 0.92f);
        private static readonly Color CupLineColor = new Color(0.72f, 0.93f, 1f);
        private static readonly Color CupLineDimColor = new Color(0.43f, 0.68f, 0.76f);
        private static readonly Color MilkFillColor = Color.white;
        private static readonly Color MilkSurfaceColor = Color.white;
        private static readonly Color TickColor = new Color(0.78f, 0.91f, 0.96f);
        private static readonly Color ThresholdColor = new Color(1f, 0.28f, 0.22f);
        private static readonly Color ButtonBgColor = new Color(0.10f, 0.12f, 0.13f);
        private static readonly Color ButtonHoverColor = new Color(0.17f, 0.21f, 0.23f);
        private static readonly Color ButtonBorderColor = new Color(0.54f, 0.72f, 0.78f);

        public Gizmo_MilkGauge(CompMooHasBodyResource comp, string label, string desc)
        {
            this.comp = comp;
            this.label = label;
            this.desc = desc;
        }

        public override float GetWidth(float maxWidth)
        {
            return 142f;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            float width = GetWidth(maxWidth);
            Rect totalRect = new Rect(topLeft.x, topLeft.y, width, 75f);
            bool interacted = false;

            // 背景
            Widgets.DrawWindowBackground(totalRect);

            // 标题
            Rect titleRect = new Rect(totalRect.x + 7f, totalRect.y + 1f, 76f, 17f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(titleRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            Rect cupOuterRect = new Rect(totalRect.x + 18f, totalRect.y + 16f, 58f, 51f);
            Rect cupInnerRect = new Rect(cupOuterRect.x + 7f, cupOuterRect.y + 8f, cupOuterRect.width - 14f, cupOuterRect.height - 13f);
            float fullness = comp.Fullness;
            float threshold = comp.MilkThreshold;

            DrawCup(cupOuterRect, cupInnerRect, fullness, threshold);

            GUI.color = Color.white;
            Rect controlsRect = new Rect(totalRect.x + 101f, totalRect.y + 16f, 26f, 51f);
            bool overThresholdControls;
            interacted = DrawThresholdControls(controlsRect, out overThresholdControls) || interacted;

            GUI.color = Color.white;

            // 百分比文字
            Rect pctRect = new Rect(cupOuterRect.x - 1f, totalRect.yMax - 17f, cupOuterRect.width + 2f, 16f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(pctRect, fullness.ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            // Tooltip 文本会随奶量变化，必须使用稳定 ID 避免悬浮提示闪烁。
            if (!overThresholdControls)
            {
                TooltipHandler.TipRegion(totalRect, new TipSignal(GetMainTooltip, StableTooltipId(MainTooltipSeed)));
            }

            if (interacted)
            {
                return new GizmoResult(GizmoState.Interacted, Event.current);
            }

            return new GizmoResult(Mouse.IsOver(totalRect) ? GizmoState.Mouseover : GizmoState.Clear);
        }

        private void DrawCup(Rect cupOuterRect, Rect cupInnerRect, float fullness, float threshold)
        {
            DrawSolidRect(cupInnerRect, CupBackColor);

            float fillHeight = cupInnerRect.height * Mathf.Clamp01(fullness);
            if (fillHeight > 0f)
            {
                Rect fillRect = new Rect(cupInnerRect.x + 1f, cupInnerRect.yMax - fillHeight, cupInnerRect.width - 2f, fillHeight);
                DrawSolidRect(fillRect, MilkFillColor);

                Rect surfaceRect = new Rect(fillRect.x, fillRect.y, fillRect.width, 2f);
                DrawSolidRect(surfaceRect, MilkSurfaceColor);
            }

            DrawCupOutline(cupOuterRect, cupInnerRect);
            DrawGraduationTicks(cupInnerRect);

            float thresholdY = cupInnerRect.y + cupInnerRect.height * (1f - Mathf.Clamp01(threshold));
            DrawSolidRect(new Rect(cupInnerRect.x - 3f, thresholdY - 1f, cupInnerRect.width + 6f, 2f), ThresholdColor);
        }

        private void DrawCupOutline(Rect cupOuterRect, Rect cupInnerRect)
        {
            const float thickness = 2f;
            Rect outlineRect = new Rect(cupInnerRect.x - 3f, cupInnerRect.y - 3f, cupInnerRect.width + 6f, cupInnerRect.height + 6f);

            DrawSolidRect(new Rect(outlineRect.x, outlineRect.y, outlineRect.width, thickness), CupLineColor);
            DrawSolidRect(new Rect(outlineRect.x, outlineRect.yMax - thickness, outlineRect.width, thickness), CupLineColor);
            DrawSolidRect(new Rect(outlineRect.x, outlineRect.y, thickness, outlineRect.height), CupLineColor);
            DrawSolidRect(new Rect(outlineRect.xMax - thickness, outlineRect.y, thickness, outlineRect.height), CupLineColor);

            DrawSolidRect(new Rect(cupInnerRect.x, cupInnerRect.y, cupInnerRect.width, 1f), CupLineDimColor);
            DrawSolidRect(new Rect(cupInnerRect.x, cupInnerRect.yMax, cupInnerRect.width, 1f), CupLineDimColor);
        }

        private void DrawGraduationTicks(Rect cupInnerRect)
        {
            for (int i = 1; i <= 4; i++)
            {
                float y = cupInnerRect.yMax - cupInnerRect.height * i / 4f;
                float tickWidth = i % 2 == 0 ? 18f : 10f;
                DrawSolidRect(new Rect(cupInnerRect.x + 4f, y, tickWidth, 1f), TickColor);
            }
        }

        private bool DrawThresholdControls(Rect controlsRect, out bool mouseOverControls)
        {
            bool interacted = false;

            Rect plusRect = new Rect(controlsRect.x, controlsRect.y + 3f, 24f, 20f);
            Rect minusRect = new Rect(controlsRect.x, controlsRect.y + 28f, 24f, 20f);
            mouseOverControls = Mouse.IsOver(plusRect) || Mouse.IsOver(minusRect);

            if (DrawThresholdButton(plusRect, true))
            {
                comp.MilkThreshold = Mathf.Min(1f, comp.MilkThreshold + 0.1f);
                interacted = true;
            }

            if (DrawThresholdButton(minusRect, false))
            {
                comp.MilkThreshold = Mathf.Max(0.1f, comp.MilkThreshold - 0.1f);
                interacted = true;
            }

            if (Mouse.IsOver(plusRect))
            {
                TooltipHandler.TipRegion(plusRect, new TipSignal(GetIncreaseThresholdTooltip, StableTooltipId(IncreaseTooltipSeed)));
            }

            if (Mouse.IsOver(minusRect))
            {
                TooltipHandler.TipRegion(minusRect, new TipSignal(GetDecreaseThresholdTooltip, StableTooltipId(DecreaseTooltipSeed)));
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            return interacted;
        }

        private string GetMainTooltip()
        {
            return "MooGirl.Milk.Gauge.Tooltip".Translate(
                desc,
                comp.Fullness.ToStringPercent(),
                comp.MilkThreshold.ToStringPercent(),
                comp.GetResourceAmountForNextGather(),
                comp.GetProductionRateExplanation());
        }

        private string GetIncreaseThresholdTooltip()
        {
            return "MooGirl.Milk.Gauge.IncreaseThresholdTooltip".Translate(
                comp.MilkThreshold.ToStringPercent(),
                Mathf.Min(1f, comp.MilkThreshold + 0.1f).ToStringPercent());
        }

        private string GetDecreaseThresholdTooltip()
        {
            return "MooGirl.Milk.Gauge.DecreaseThresholdTooltip".Translate(
                comp.MilkThreshold.ToStringPercent(),
                Mathf.Max(0.1f, comp.MilkThreshold - 0.1f).ToStringPercent());
        }

        private int StableTooltipId(int seed)
        {
            return Gen.HashCombineInt(comp.parent.thingIDNumber, seed);
        }

        private bool DrawThresholdButton(Rect rect, bool plus)
        {
            bool mouseOver = Mouse.IsOver(rect);
            DrawSolidRect(rect, mouseOver ? ButtonHoverColor : ButtonBgColor);

            GUI.color = mouseOver ? CupLineColor : ButtonBorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float centerX = rect.center.x;
            float centerY = rect.center.y;
            DrawSolidRect(new Rect(centerX - 5f, centerY - 1f, 10f, 2f), CupLineColor);
            if (plus)
            {
                DrawSolidRect(new Rect(centerX - 1f, centerY - 5f, 2f, 10f), CupLineColor);
            }

            return Widgets.ButtonInvisible(rect);
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
