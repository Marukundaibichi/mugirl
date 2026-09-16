using System.Collections.Generic;
using UnityEngine;

namespace Mugirl
{
    // Transient presentation state belongs to the window, never to a saved game or a static cache.
    internal sealed class CorporateMotion
    {
        internal sealed class ControlState
        {
            internal float Hover;
            internal float Press;
            internal float Selection;
            internal float Enabled;
            internal bool Held;
            internal float RippleStarted = -100f;
            internal Vector2 RippleOrigin;
            internal float LastSeen;
            internal int LastFrame = -1;
        }

        internal sealed class ScrollState
        {
            internal Vector2 Position;
            internal Vector2 Target;
            internal Vector2 LastReturned;
            internal float LastSeen;
            internal float DragOffset;
            internal int LastFrame = -1;
            internal bool Initialized;
            internal bool Dragging;
        }

        private const int ControlLimit = 2048;
        private const int ScrollLimit = 96;
        private readonly Dictionary<int, ControlState> controls = new Dictionary<int, ControlState>();
        private readonly Dictionary<int, ScrollState> scrolls = new Dictionary<int, ScrollState>();
        private readonly List<int> expired = new List<int>();
        private int lastUnityFrame = -1;
        private float lastRepaint;
        private float lastCleanup;
        private int ownedHotControl;
        private ScrollState ownedScroll;

        internal float Now { get; private set; }
        internal float Delta { get; private set; }
        internal int Frame { get; private set; }

        internal void CaptureInput(ScrollState state, int control)
        {
            ReleaseInput();
            ownedScroll = state;
            ownedHotControl = control;
            state.Dragging = true;
            GUIUtility.hotControl = control;
        }

        internal void ReleaseInput()
        {
            if (ownedHotControl != 0 && GUIUtility.hotControl == ownedHotControl)
                GUIUtility.hotControl = 0;
            if (ownedScroll != null) ownedScroll.Dragging = false;
            ownedHotControl = 0;
            ownedScroll = null;
            foreach (ControlState state in controls.Values) state.Held = false;
        }

        internal void ReleaseScrollInput(ScrollState state)
        {
            if (ownedScroll == state) ReleaseInput();
            else state.Dragging = false;
        }

        internal void BeginFrame()
        {
            Now = Time.realtimeSinceStartup;
            if (Event.current != null && Event.current.rawType == EventType.MouseUp && Event.current.button == 0)
                foreach (ControlState state in controls.Values) state.Held = false;
            if (Event.current == null || Event.current.type != EventType.Repaint || lastUnityFrame == Time.frameCount)
                return;
            lastUnityFrame = Time.frameCount;
            Delta = lastRepaint > 0f ? Mathf.Clamp(Now - lastRepaint, 0f, 0.05f) : 1f / 60f;
            lastRepaint = Now;
            Frame++;
            // A list can disappear during a tab change before receiving MouseUp.
            if (ownedScroll != null && ownedScroll.LastFrame < Frame - 1) ReleaseInput();
            if (Now - lastCleanup < 5f) return;
            lastCleanup = Now;
            expired.Clear();
            foreach (KeyValuePair<int, ControlState> pair in controls)
                if (Now - pair.Value.LastSeen > 25f) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) controls.Remove(expired[i]);
            expired.Clear();
            foreach (KeyValuePair<int, ScrollState> pair in scrolls)
                if (Now - pair.Value.LastSeen > 45f) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++)
            {
                ReleaseScrollInput(scrolls[expired[i]]);
                scrolls.Remove(expired[i]);
            }
            expired.Clear();
        }

        internal ControlState Control(int id, bool hovered, bool enabled, bool selected)
        {
            ControlState state;
            if (!controls.TryGetValue(id, out state))
            {
                if (controls.Count >= ControlLimit) EvictOldestControl();
                state = new ControlState { Enabled = enabled ? 1f : 0f, Selection = selected ? 1f : 0f };
                controls.Add(id, state);
            }
            state.LastSeen = Now;
            // Layout/input events may intentionally disable GUI input. Seed the first
            // visible value from Repaint so that input locking never looks disabled.
            if (state.LastFrame < 0)
            {
                state.Enabled = enabled ? 1f : 0f;
                state.Selection = selected ? 1f : 0f;
            }
            if (Event.current != null && Event.current.type == EventType.Repaint && state.LastFrame != Frame)
            {
                state.LastFrame = Frame;
                state.Hover = Approach(state.Hover, hovered ? 1f : 0f, 17f, Delta);
                state.Press = Approach(state.Press, state.Held && hovered && enabled ? 1f : 0f, 25f, Delta);
                state.Selection = Approach(state.Selection, selected ? 1f : 0f, 15f, Delta);
                state.Enabled = Approach(state.Enabled, enabled ? 1f : 0f, 13f, Delta);
            }
            return state;
        }

        internal ScrollState Scroll(int id)
        {
            ScrollState state;
            if (!scrolls.TryGetValue(id, out state))
            {
                if (scrolls.Count >= ScrollLimit)
                {
                    int oldest = 0;
                    float time = float.MaxValue;
                    foreach (KeyValuePair<int, ScrollState> pair in scrolls)
                        if (pair.Value.LastSeen < time) { time = pair.Value.LastSeen; oldest = pair.Key; }
                    ReleaseScrollInput(scrolls[oldest]);
                    scrolls.Remove(oldest);
                }
                state = new ScrollState();
                scrolls.Add(id, state);
            }
            state.LastSeen = Now;
            return state;
        }

        private void EvictOldestControl()
        {
            int oldest = 0;
            float time = float.MaxValue;
            foreach (KeyValuePair<int, ControlState> pair in controls)
                if (pair.Value.LastSeen < time) { time = pair.Value.LastSeen; oldest = pair.Key; }
            controls.Remove(oldest);
        }

        internal static float Approach(float value, float target, float speed, float delta)
        {
            float result = Mathf.Lerp(value, target, 1f - Mathf.Exp(-speed * delta));
            return Mathf.Abs(result - target) < 0.001f ? target : result;
        }

        internal static float Ease(float value)
        {
            value = Mathf.Clamp01(value);
            return 1f - Mathf.Pow(1f - value, 3f);
        }
    }
}
