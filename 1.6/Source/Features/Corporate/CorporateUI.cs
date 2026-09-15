using UnityEngine;
using Verse;

namespace Mugirl
{
    // 白色巨企主题；绘制完成后恢复 IMGUI 全局状态，避免污染原版窗口。
    internal static class CorporateUI
    {
        internal static readonly Color Background = new Color(0.953f, 0.965f, 0.980f);
        internal static readonly Color Surface = Color.white;
        internal static readonly Color Raised = new Color(0.918f, 0.941f, 0.965f);
        internal static readonly Color Ink = new Color(0.125f, 0.200f, 0.282f);
        internal static readonly Color Muted = new Color(0.337f, 0.420f, 0.502f);
        internal static readonly Color Accent = new Color(0.031f, 0.486f, 0.569f);
        internal static readonly Color Border = new Color(0.808f, 0.855f, 0.898f);
        internal static readonly Color Danger = new Color(0.718f, 0.227f, 0.286f);

        internal static void Label(Rect rect, string text, GameFont font = GameFont.Small,
            Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            try
            {
                GUI.color = color ?? Ink;
                Text.Font = font;
                Text.Anchor = anchor;
                Widgets.Label(rect, text ?? string.Empty);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
            }
        }

        internal static void Panel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Surface);
            Rule(new Rect(rect.x, rect.y, rect.width, 1f));
            Rule(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f));
            Rule(new Rect(rect.x, rect.y, 1f, rect.height));
            Rule(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height));
        }

        internal static void Rule(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Border);
        }

        internal static bool Button(Rect rect, string label, bool enabled = true, bool primary = false)
        {
            bool active = enabled && GUI.enabled;
            bool hover = active && Mouse.IsOver(rect);
            Color fill = primary ? Accent : Raised;
            if (!active) fill = new Color(0.89f, 0.91f, 0.93f);
            else if (hover) fill = primary ? new Color(0.025f, 0.40f, 0.48f) : new Color(0.83f, 0.89f, 0.93f);
            Widgets.DrawBoxSolid(rect, fill);
            Label(rect.ContractedBy(4f), label, color: active && primary ? Color.white : active ? Ink : Muted,
                anchor: TextAnchor.MiddleCenter);
            return Widgets.ButtonInvisible(rect) && active;
        }

        internal static void Heading(ref float y, float width, string title, string subtitle = null)
        {
            Label(new Rect(0f, y, width, 36f), title, GameFont.Medium);
            y += 40f;
            if (!string.IsNullOrEmpty(subtitle))
            {
                float height = Mathf.Max(38f, Text.CalcHeight(subtitle, width));
                Label(new Rect(0f, y, width, height), subtitle, color: Muted);
                y += height + 12f;
            }
        }

        internal static void Notice(Rect rect, string text, bool warning = false)
        {
            Widgets.DrawBoxSolid(rect, warning ? new Color(1f, 0.945f, 0.91f) : Raised);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), warning ? Danger : Accent);
            Label(rect.ContractedBy(10f), text, color: warning ? Danger : Ink);
        }

        internal static string Money(float value)
        {
            return value.ToString("N0") + " " + "Mugirl.CorporateUI.Silver".Translate();
        }
    }
}
