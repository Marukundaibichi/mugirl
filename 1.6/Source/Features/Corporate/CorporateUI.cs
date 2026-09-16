using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // Scopes are temporary; all interaction animation state belongs to the window.
    internal static class CorporateUI
    {
        internal static readonly Color Background = Gray(0.032f);
        internal static readonly Color Surface = Gray(0.070f);
        internal static readonly Color Raised = Gray(0.135f);
        internal static readonly Color Ink = Gray(0.950f);
        internal static readonly Color Muted = Gray(0.650f);
        internal static readonly Color Accent = Gray(0.980f);
        internal static readonly Color Border = Gray(0.220f);
        internal static readonly Color Danger = Gray(0.850f);
        private static FrameScope current;
        private static GUIStyle inputStyle;

        private sealed class ScrollFrame
        {
            internal Rect Rect, View;
            internal CorporateMotion.ScrollState State;
            internal bool MouseInside, Enabled;
            internal int PreviousPath;
            internal Vector2 Maximum;
        }

        private sealed class FrameScope : IDisposable
        {
            internal readonly CorporateMotion Motion;
            internal readonly float Opacity;
            internal readonly bool Interactive;
            internal readonly Stack<ScrollFrame> Scrolls = new Stack<ScrollFrame>();
            internal int Path;
            private readonly FrameScope previous;
            private readonly Color color, backgroundColor, contentColor;
            private readonly Matrix4x4 matrix;
            private readonly bool enabled, wordWrap;
            private readonly GameFont font;
            private readonly TextAnchor anchor;
            private bool disposed;

            internal FrameScope(CorporateMotion motion, string scope, float opacity, bool interactive)
            {
                previous = current;
                Motion = motion;
                Path = Hash(previous != null ? previous.Path : 17, scope ?? "corporate");
                Opacity = Mathf.Clamp01(opacity) * (previous != null ? previous.Opacity : 1f);
                Interactive = interactive && (previous == null || previous.Interactive);
                color = GUI.color; backgroundColor = GUI.backgroundColor; contentColor = GUI.contentColor;
                matrix = GUI.matrix; enabled = GUI.enabled;
                font = Text.Font; anchor = Text.Anchor; wordWrap = Text.WordWrap;
                current = this;
                Motion?.BeginFrame();
                if (!Interactive)
                {
                    Motion?.ReleaseInput();
                    // External IMGUI widgets inside a scope obey the same input gate;
                    // their normal Repaint colors remain independent of that gate.
                    if (Event.current.type != EventType.Repaint) GUI.enabled = false;
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                while (Scrolls.Count > 0) EndScrollView();
                GUI.color = color; GUI.backgroundColor = backgroundColor; GUI.contentColor = contentColor;
                GUI.matrix = matrix; GUI.enabled = enabled;
                Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wordWrap;
                current = previous;
            }
        }

        internal static IDisposable BeginFrame(CorporateMotion motion, string scope, float opacity = 1f, bool interactive = true)
        {
            return new FrameScope(motion, scope, opacity, interactive);
        }
        internal static IDisposable Frame(CorporateMotion motion, string scope, float opacity = 1f, bool interactive = true)
        {
            return BeginFrame(motion, scope, opacity, interactive);
        }
        internal static void EndFrame() { current?.Dispose(); }
        internal static Color Gray(float value, float alpha = 1f) { return new Color(value, value, value, alpha); }

        private static Color Tint(Color color)
        {
            color.a *= GUI.color.a * (current != null ? current.Opacity : 1f);
            return color;
        }

        internal static void Fill(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f) return;
            Color saved = GUI.color;
            try
            {
                Color tinted = Tint(color);
                GUI.color = Color.white;
                Widgets.DrawBoxSolid(rect, tinted);
            }
            finally { GUI.color = saved; }
        }

        internal static void Label(Rect rect, string text, GameFont font = GameFont.Small,
            Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            Color previousColor = GUI.color;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            try
            {
                GUI.color = Tint(color ?? Ink); Text.Font = font; Text.Anchor = anchor;
                Widgets.Label(rect, text ?? string.Empty);
            }
            finally { GUI.color = previousColor; Text.Font = previousFont; Text.Anchor = previousAnchor; }
        }

        internal static void ThingIcon(Rect rect, Thing thing)
        {
            if (thing == null) return;
            Color saved = GUI.color;
            try { GUI.color = Tint(Color.white); Widgets.ThingIcon(rect, thing); }
            finally { GUI.color = saved; }
        }

        internal static void ThingIcon(Rect rect, ThingDef def)
        {
            if (def == null) return;
            Color saved = GUI.color;
            try { GUI.color = Tint(Color.white); Widgets.ThingIcon(rect, def); }
            finally { GUI.color = saved; }
        }

        internal static void Texture(Rect rect, UnityEngine.Texture texture)
        {
            if (texture == null) return;
            Color saved = GUI.color;
            try { GUI.color = Tint(Color.white); GUI.DrawTexture(rect, texture); }
            finally { GUI.color = saved; }
        }

        internal static void Panel(Rect rect)
        {
            Fill(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), Gray(0f, 0.20f));
            Fill(rect, Surface); Outline(rect, Border);
        }
        internal static void Rule(Rect rect) { Fill(rect, Border); }
        private static void Outline(Rect rect, Color color, float thickness = 1f)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static int Hash(int seed, string value)
        {
            unchecked { for (int i = 0; i < value.Length; i++) seed = seed * 31 + value[i]; return seed; }
        }
        private static int ControlKey(Rect rect, string kind, string id)
        {
            int key = Hash(current != null ? current.Path : 17, kind);
            if (id != null) return Hash(key, id);
            unchecked
            {
                key = key * 31 + Mathf.RoundToInt(rect.x * 2f);
                key = key * 31 + Mathf.RoundToInt(rect.y * 2f);
                key = key * 31 + Mathf.RoundToInt(rect.width * 2f);
                return key * 31 + Mathf.RoundToInt(rect.height * 2f);
            }
        }

        private static bool InsideScrollClips()
        {
            if (current == null) return true;
            foreach (ScrollFrame frame in current.Scrolls)
                if (!frame.MouseInside) return false;
            return true;
        }

        private static bool Interactive => current == null || current.Interactive;

        private static CorporateMotion.ControlState Interaction(Rect rect, string kind, string id,
            bool enabled, bool selected, out bool clicked)
        {
            bool active = enabled && GUI.enabled;
            bool acceptsInput = active && Interactive;
            bool insideClips = InsideScrollClips();
            bool hover = acceptsInput && insideClips && Mouse.IsOver(rect);
            CorporateMotion.ControlState state = current?.Motion?.Control(ControlKey(rect, kind, id), hover, active, selected);
            Event ev = Event.current;
            if (state != null)
            {
                if (!acceptsInput || (ev.rawType == EventType.MouseUp && ev.button == 0)) state.Held = false;
                if (hover && ev.type == EventType.MouseDown && ev.button == 0)
                {
                    state.Held = true;
                    state.RippleOrigin = ev.mousePosition - rect.position;
                    state.RippleStarted = Time.realtimeSinceStartup;
                }
            }
            // Never invoke ButtonInvisible on disabled controls: it can consume the event.
            clicked = acceptsInput && insideClips && Widgets.ButtonInvisible(rect);
            if (clicked && state != null)
            {
                state.RippleOrigin = ev.mousePosition - rect.position;
                state.RippleStarted = Time.realtimeSinceStartup;
            }
            return state;
        }

        internal static bool Button(Rect rect, string label, bool enabled = true, bool primary = false, string id = null)
        {
            bool clicked;
            CorporateMotion.ControlState state = Interaction(rect, "button", id, enabled, primary, out clicked);
            bool active = enabled && GUI.enabled;
            float hover = state != null ? state.Hover : active && Interactive && InsideScrollClips() && Mouse.IsOver(rect) ? 1f : 0f;
            float press = state != null ? state.Press : 0f;
            float selection = state != null ? state.Selection : primary ? 1f : 0f;
            float availability = state != null ? state.Enabled : active ? 1f : 0f;
            Rect face = rect.ContractedBy(press * 1.4f);
            Color secondary = Gray(Mathf.Lerp(0.115f, 0.225f, hover));
            Color fill = Color.Lerp(Gray(0.09f), Color.Lerp(secondary, Gray(Mathf.Lerp(0.94f, 1f, hover)), selection), availability);
            Fill(new Rect(face.x, face.yMax, face.width, 2f), Gray(0f, 0.26f * hover));
            Fill(face, fill);
            Outline(face, Color.Lerp(Gray(0.22f + 0.24f * hover), Gray(1f), selection));
            DrawRipple(face, state, selection > 0.5f ? Gray(0f, 0.13f) : Gray(1f, 0.12f));
            // Preserve contrast while a selected action transitions through mid-gray.
            Color text = Color.Lerp(Muted, fill.r > 0.48f ? Gray(0.035f) : Ink, availability);
            Label(face.ContractedBy(4f), label, color: text, anchor: TextAnchor.MiddleCenter);
            return clicked;
        }

        internal static bool Row(Rect rect, bool selected, string id = null)
        {
            bool clicked;
            CorporateMotion.ControlState state = Interaction(rect, "row", id, true, selected, out clicked);
            float hover = state != null ? state.Hover : GUI.enabled && Interactive && InsideScrollClips() && Mouse.IsOver(rect) ? 1f : 0f;
            float selection = state != null ? state.Selection : selected ? 1f : 0f;
            Fill(rect, Gray(0.07f + hover * 0.035f + selection * 0.065f));
            Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Gray(0.21f, 0.6f));
            if (selection > 0.001f)
            {
                float height = Mathf.Lerp(8f, rect.height - 12f, CorporateMotion.Ease(selection));
                Fill(new Rect(rect.x, rect.center.y - height / 2f, 3f, height), Gray(1f, selection));
                Outline(rect, Gray(0.72f, selection * 0.35f));
            }
            DrawRipple(rect, state, Gray(1f, 0.09f));
            return clicked;
        }

        private static void DrawRipple(Rect rect, CorporateMotion.ControlState state, Color color)
        {
            if (state == null || Event.current.type != EventType.Repaint) return;
            float elapsed = Time.realtimeSinceStartup - state.RippleStarted;
            if (elapsed < 0f || elapsed > 0.52f) return;
            float progress = Mathf.Clamp01(elapsed / 0.52f);
            float radius = Mathf.Max(rect.width, rect.height) * 1.15f * CorporateMotion.Ease(progress);
            color.a *= 1f - progress;
            Vector2 center = rect.position + state.RippleOrigin;
            Rect square = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
            float left = Mathf.Max(square.x, rect.x), top = Mathf.Max(square.y, rect.y);
            float right = Mathf.Min(square.xMax, rect.xMax), bottom = Mathf.Min(square.yMax, rect.yMax);
            if (right > left && bottom > top) Fill(new Rect(left, top, right - left, bottom - top), color);
            Color edge = color; edge.a *= 1.6f;
            if (square.x > rect.x) Fill(new Rect(square.x, top, 1f, Mathf.Max(0f, bottom - top)), edge);
            if (square.xMax < rect.xMax) Fill(new Rect(square.xMax - 1f, top, 1f, Mathf.Max(0f, bottom - top)), edge);
        }

        internal static string Input(Rect rect, string value, string placeholder = null, string id = null)
        {
            EnsureInputStyle();
            string name = "corporate/input/" + ControlKey(rect, "input", id);
            bool focused = GUI.GetNameOfFocusedControl() == name;
            bool hovered = GUI.enabled && Interactive && InsideScrollClips() && Mouse.IsOver(rect);
            CorporateMotion.ControlState state = current?.Motion?.Control(ControlKey(rect, "input", id), hovered, GUI.enabled, focused);
            float focus = state != null ? state.Selection : focused ? 1f : 0f;
            float hover = state != null ? state.Hover : hovered ? 1f : 0f;
            Fill(rect, Gray(0.045f + hover * 0.015f));
            Outline(rect, Gray(0.24f + hover * 0.12f + focus * 0.40f));
            if (focus > 0.001f)
            {
                float width = rect.width * CorporateMotion.Ease(focus);
                Fill(new Rect(rect.center.x - width * 0.5f, rect.yMax - 2f, width, 2f), Gray(0.96f, focus));
            }
            Color saved = GUI.color, content = GUI.contentColor;
            bool enabled = GUI.enabled;
            try
            {
                GUI.color = Tint(Color.white); GUI.contentColor = Color.white;
                if (Event.current.type != EventType.Repaint) GUI.enabled = enabled && Interactive;
                GUI.SetNextControlName(name);
                string result = GUI.TextField(rect.ContractedBy(1f), value ?? string.Empty, 1000, inputStyle);
                if (string.IsNullOrEmpty(result) && !focused && !string.IsNullOrEmpty(placeholder))
                {
                    GUI.color = saved;
                    Label(rect.ContractedBy(9f, 3f), placeholder, color: Muted, anchor: TextAnchor.MiddleLeft);
                }
                return result;
            }
            finally { GUI.color = saved; GUI.contentColor = content; GUI.enabled = enabled; }
        }

        internal static bool InputFocused(Rect rect, string id)
        {
            return GUI.GetNameOfFocusedControl() == "corporate/input/" + ControlKey(rect, "input", id);
        }

        private static void EnsureInputStyle()
        {
            if (inputStyle != null) return;
            inputStyle = new GUIStyle(GUI.skin.textField);
            inputStyle.normal.background = null; inputStyle.hover.background = null;
            inputStyle.focused.background = null; inputStyle.active.background = null;
            inputStyle.normal.textColor = Ink; inputStyle.hover.textColor = Ink;
            inputStyle.focused.textColor = Ink; inputStyle.active.textColor = Ink;
            inputStyle.padding = new RectOffset(9, 9, 4, 4);
            inputStyle.alignment = TextAnchor.MiddleLeft;
        }

        internal static void BeginScrollView(Rect rect, ref Vector2 position, Rect view, string id = null)
        {
            if (current == null || current.Motion == null) { Widgets.BeginScrollView(rect, ref position, view); return; }
            int key = ControlKey(rect, "scroll", id);
            CorporateMotion.ScrollState state = current.Motion.Scroll(key);
            Vector2 maximum = new Vector2(Mathf.Max(0f, view.width - rect.width), Mathf.Max(0f, view.height - rect.height));
            if (!state.Initialized || (position - state.LastReturned).sqrMagnitude > 0.01f)
            { state.Position = position; state.Target = position; state.Initialized = true; }
            state.Target.x = Mathf.Clamp(state.Target.x, 0f, maximum.x);
            state.Target.y = Mathf.Clamp(state.Target.y, 0f, maximum.y);
            if (Event.current.type == EventType.Repaint && state.LastFrame != current.Motion.Frame)
            {
                state.LastFrame = current.Motion.Frame;
                state.Position.x = CorporateMotion.Approach(state.Position.x, state.Target.x, 18f, current.Motion.Delta);
                state.Position.y = CorporateMotion.Approach(state.Position.y, state.Target.y, 18f, current.Motion.Delta);
            }
            state.Position.x = Mathf.Clamp(state.Position.x, 0f, maximum.x);
            state.Position.y = Mathf.Clamp(state.Position.y, 0f, maximum.y);
            ScrollFrame frame = new ScrollFrame { Rect = rect, View = view, State = state, Maximum = maximum,
                MouseInside = InsideScrollClips() && Mouse.IsOver(rect), Enabled = GUI.enabled && Interactive, PreviousPath = current.Path };
            DrawScrollBar(frame, key);
            position = state.Position; state.LastReturned = position;
            current.Scrolls.Push(frame); current.Path = key;
            GUI.BeginGroup(rect);
            GUI.BeginGroup(new Rect(-position.x - view.x, -position.y - view.y,
                Mathf.Max(view.width + view.x, rect.width), Mathf.Max(view.height + view.y, rect.height)));
        }

        private static void DrawScrollBar(ScrollFrame frame, int key)
        {
            if (frame.Maximum.y <= 0f)
            {
                current.Motion.ReleaseScrollInput(frame.State);
                return;
            }
            Rect track = new Rect(frame.Rect.xMax - 9f, frame.Rect.y + 2f, 7f, Mathf.Max(1f, frame.Rect.height - 4f));
            float thumbHeight = Mathf.Min(track.height, Mathf.Max(26f, track.height * frame.Rect.height / frame.View.height));
            float travel = Mathf.Max(1f, track.height - thumbHeight);
            CorporateMotion.ScrollState state = frame.State;
            Rect thumb = new Rect(track.x + 2f, track.y + state.Position.y / frame.Maximum.y * travel, 3f, thumbHeight);
            Event ev = Event.current;
            int control = GUIUtility.GetControlID(key, FocusType.Passive, track);
            if (!frame.Enabled || state.Dragging && GUIUtility.hotControl != control)
                current.Motion.ReleaseScrollInput(state);
            if (frame.Enabled && frame.MouseInside && ev.type == EventType.MouseDown && ev.button == 0 && Mouse.IsOver(track))
            {
                state.DragOffset = thumb.Contains(ev.mousePosition) ? ev.mousePosition.y - thumb.y : thumbHeight * 0.5f;
                current.Motion.CaptureInput(state, control);
                state.Position.y = state.Target.y = Mathf.Clamp01((ev.mousePosition.y - track.y - state.DragOffset) / travel) * frame.Maximum.y;
                ev.Use();
            }
            if (state.Dragging && GUIUtility.hotControl == control)
            {
                if (ev.type == EventType.MouseDrag)
                {
                    state.Position.y = state.Target.y = Mathf.Clamp01((ev.mousePosition.y - track.y - state.DragOffset) / travel) * frame.Maximum.y;
                    ev.Use();
                }
                if (ev.rawType == EventType.MouseUp)
                {
                    current.Motion.ReleaseScrollInput(state);
                    if (ev.type == EventType.MouseUp) ev.Use();
                }
            }
            Fill(new Rect(track.center.x, track.y, 1f, track.height), Gray(0.2f));
            Fill(thumb, Gray(state.Dragging || frame.Enabled && frame.MouseInside && Mouse.IsOver(track) ? 0.92f : 0.48f));
        }

        internal static void EndScrollView()
        {
            if (current == null || current.Motion == null) { Widgets.EndScrollView(); return; }
            ScrollFrame frame = current.Scrolls.Pop();
            GUI.EndGroup(); GUI.EndGroup(); current.Path = frame.PreviousPath;
            // Children handle wheel input first, so nested lists do not scroll their parent page.
            Event ev = Event.current;
            if (frame.Enabled && frame.MouseInside && ev.type == EventType.ScrollWheel)
            {
                Vector2 target = frame.State.Target;
                target.y = Mathf.Clamp(target.y + ev.delta.y * 32f, 0f, frame.Maximum.y);
                target.x = Mathf.Clamp(target.x + ev.delta.x * 32f, 0f, frame.Maximum.x);
                bool changed = (target - frame.State.Target).sqrMagnitude > 0.01f;
                bool settling = ev.delta.y > 0f && frame.State.Position.y < frame.Maximum.y - 0.1f
                    || ev.delta.y < 0f && frame.State.Position.y > 0.1f
                    || ev.delta.x > 0f && frame.State.Position.x < frame.Maximum.x - 0.1f
                    || ev.delta.x < 0f && frame.State.Position.x > 0.1f;
                if (changed || settling) { frame.State.Target = target; ev.Use(); }
            }
        }

        internal static void Heading(ref float y, float width, string title, string subtitle = null)
        {
            Fill(new Rect(0f, y + 5f, 3f, 24f), Accent);
            Label(new Rect(13f, y, Mathf.Max(0f, width - 13f), 36f), title, GameFont.Medium);
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
            Fill(rect, warning ? Gray(0.14f) : Gray(0.10f));
            Outline(rect, warning ? Gray(0.43f) : Border);
            Fill(new Rect(rect.x, rect.y, warning ? 4f : 2f, rect.height), warning ? Ink : Gray(0.60f));
            if (warning)
                for (int i = 0; i < Mathf.Min(8, Mathf.FloorToInt(rect.width / 16f)); i++)
                    Fill(new Rect(rect.xMax - 7f - i * 8f, rect.y + 3f, 3f, 3f), Gray(0.68f, 0.55f));
            Label(rect.ContractedBy(12f, 10f), text, color: warning ? Ink : Gray(0.82f));
        }

        internal static void DrawGeometry(Rect rect, float opacity, bool light = false)
        {
            if (Event.current.type != EventType.Repaint || opacity <= 0f) return;
            float time = Time.realtimeSinceStartup;
            Color line = Gray(light ? 0f : 1f, opacity * 0.15f);
            float step = Mathf.Max(36f, Mathf.Min(64f, rect.width / 12f));
            float drift = time * 3f % step;
            GUI.BeginGroup(rect);
            try
            {
                for (float x = -step + drift; x < rect.width; x += step) Fill(new Rect(x, 0f, 1f, rect.height), line);
                for (float y = -step + drift * 0.5f; y < rect.height; y += step) Fill(new Rect(0f, y, rect.width, 1f), line);
                for (int i = 0; i < 5; i++)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(time * 0.65f + i * 1.4f);
                    float size = Mathf.Lerp(28f, 110f, (i + 1) / 5f) + pulse * 12f;
                    float x = rect.width * (0.17f + i * 0.17f) + Mathf.Sin(time * 0.14f + i) * 12f;
                    float y = rect.height * (0.25f + (i % 3) * 0.22f) + Mathf.Cos(time * 0.18f + i) * 15f;
                    Outline(new Rect(x - size / 2f, y - size / 2f, size, size), Gray(light ? 0f : 1f, opacity * (0.12f + pulse * 0.18f)));
                    Fill(new Rect(x - 2f, y - 2f, 4f, 4f), Gray(light ? 0f : 1f, opacity * 0.4f));
                }
                DrawOrbit(new Vector2(rect.width * 0.79f, rect.height * 0.50f),
                    Mathf.Clamp(Mathf.Min(rect.width, rect.height) * 0.27f, 18f, 145f), time, opacity, light);
                float sweep = time * 24f % (rect.width + 120f) - 60f;
                Fill(new Rect(sweep, 0f, 1f, rect.height), Gray(light ? 0f : 1f, opacity * 0.40f));
            }
            finally { GUI.EndGroup(); }
        }

        private static void DrawOrbit(Vector2 center, float radius, float time, float opacity, bool light)
        {
            Matrix4x4 matrix = GUI.matrix;
            try
            {
                for (int ring = 0; ring < 3; ring++)
                {
                    GUI.matrix = matrix;
                    float size = radius * (1f - ring * 0.24f);
                    float angle = time * (ring % 2 == 0 ? 3f : -4.2f) + ring * 30f;
                    GUIUtility.RotateAroundPivot(angle, center);
                    Outline(new Rect(center.x - size, center.y - size, size * 2f, size * 2f),
                        Gray(light ? 0f : 1f, opacity * (0.20f - ring * 0.035f)));
                    Fill(new Rect(center.x + size - 2f, center.y - size - 2f, 4f, 4f),
                        Gray(light ? 0f : 1f, opacity * 0.38f));
                }
            }
            finally { GUI.matrix = matrix; }
        }

        internal static string Money(float value) { return value.ToString("N0") + " " + "Mugirl.CorporateUI.Silver".Translate(); }
    }
}
