using UnityEngine;
using Verse;

namespace Mugirl
{
    // 奶量量杯自定义 Gizmo：在选中雪牛娘时显示乳汁饱满度和自动挤奶阈值
    public class Gizmo_MilkGauge : Gizmo
    {
        private const int MainTooltipSeed = 129734551;
        private const int ThresholdDragControlSeed = 129734552;
        private const string GaugeTexturePath = "UI/Mugirl_MilkGauge";

        private const float GizmoWidth = 158f;
        private const float GizmoHeight = 75f;
        private const float GaugeDrawScale = 0.92f;
        private const float MinMilkThreshold = 0.1f;

        private const float SourceTextureWidth = 1264f;
        private const float SourceTextureHeight = 919f;
        private static readonly Rect GaugeSourceCropRect = new Rect(0f, 0f, SourceTextureWidth, SourceTextureHeight);
        private static readonly Rect MilkSlotSourceRect = new Rect(180f, 524f, 922f, 151f);
        private static readonly Rect GaugeTextureCoords = SourceRectToTextureCoords(GaugeSourceCropRect);

        private CompMooHasBodyResource comp;
        private string desc;

        private static Texture2D gaugeTexture;
        private static bool triedLoadGaugeTexture;

        private static readonly Color EmptySlotColor = new Color(0.03f, 0.03f, 0.03f, 0.72f);
        private static readonly Color MilkFillColor = Color.white;
        private static readonly Color ThresholdColor = new Color(1f, 0.28f, 0.22f);

        public Gizmo_MilkGauge(CompMooHasBodyResource comp, string label, string desc)
        {
            this.comp = comp;
            this.desc = desc;
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

            float fullness = Mathf.Clamp01(comp.Fullness);

            Rect gaugeRect = FillRectCentered(totalRect, GaugeSourceCropRect.width / GaugeSourceCropRect.height);
            gaugeRect = ScaleRectCentered(gaugeRect, GaugeDrawScale);
            interacted = DrawGauge(gaugeRect, fullness) || interacted;

            // Tooltip 文本会随奶量变化，必须使用稳定 ID 避免悬浮提示闪烁。
            TooltipHandler.TipRegion(totalRect, new TipSignal(GetMainTooltip, StableTooltipId(MainTooltipSeed)));

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
            DrawThresholdLine(milkSlotRect, comp.MilkThreshold);

            return interacted;
        }

        private string GetMainTooltip()
        {
            return "Mugirl.Milk.Gauge.Tooltip".Translate(
                desc,
                comp.Fullness.ToStringPercent(),
                comp.MilkThreshold.ToStringPercent(),
                comp.GetResourceAmountForNextGather(),
                comp.GetProductionRateExplanation());
        }

        private bool HandleThresholdDrag(Rect milkSlotRect)
        {
            Event current = Event.current;
            float thresholdX = ThresholdX(milkSlotRect, comp.MilkThreshold);
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
            float threshold = (mouseX - milkSlotRect.x) / milkSlotRect.width;
            comp.MilkThreshold = Mathf.Clamp(threshold, MinMilkThreshold, 1f);
        }

        private int StableTooltipId(int seed)
        {
            return Gen.HashCombineInt(comp.parent.thingIDNumber, seed);
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

        private void DrawSolidRect(Rect rect, Color color)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = oldColor;
        }
    }
}
