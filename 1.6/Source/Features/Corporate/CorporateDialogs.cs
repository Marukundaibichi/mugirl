using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // WindowStack asks OnCloseRequest before removing a window. Keep the modal in the
    // stack until its exit finishes, so focus and paused time cannot leak through it.
    public abstract class CorporateAnimatedWindow : Window
    {
        private const float OpenDuration = 0.32f;
        private const float CloseDuration = 0.22f;
        internal readonly CorporateMotion motion = new CorporateMotion();
        private float openedAt = -1f;
        private float closedAt;
        private float closingVisibility;
        private bool closing;
        private bool committingClose;
        private bool closeSound = true;
        private Action afterClose;

        internal bool IsClosing => closing;
        protected bool Closing => closing;
        protected bool Interactive => !closing && Visibility > 0.12f;
        protected internal float Visibility
        {
            get
            {
                if (closing)
                {
                    float progress = Mathf.Clamp01((Time.realtimeSinceStartup - closedAt) / CloseDuration);
                    return closingVisibility * (1f - progress * progress * (3f - 2f * progress));
                }
                return openedAt < 0f ? 1f : CorporateMotion.Ease((Time.realtimeSinceStartup - openedAt) / OpenDuration);
            }
        }

        protected override float Margin => 0f;

        protected CorporateAnimatedWindow()
        {
            forcePause = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = true;
            closeOnAccept = false;
            closeOnCancel = true;
            doCloseX = false;
            doCloseButton = false;
            doWindowBackground = false;
            drawShadow = false;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            openedAt = Time.realtimeSinceStartup;
            closing = false;
            committingClose = false;
            afterClose = null;
        }

        public override void Close(bool doCloseSound = true)
        {
            if (closing) return;
            motion.ReleaseInput();
            closingVisibility = Visibility;
            closedAt = Time.realtimeSinceStartup;
            closeSound = doCloseSound;
            closing = true;
        }

        public override bool OnCloseRequest()
        {
            if (committingClose) return true;
            Close();
            return false;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (!closing || committingClose || Time.realtimeSinceStartup - closedAt < CloseDuration) return;
            committingClose = true;
            base.Close(closeSound);
        }

        // Queue before starting the exit and consume after actual removal. A repeated
        // click, Escape, or outside click cannot confirm twice or replace this action.
        protected void CloseThen(Action action)
        {
            if (closing) return;
            afterClose = action;
            Close();
        }

        public override void PostClose()
        {
            motion.ReleaseInput();
            base.PostClose();
            Action action = afterClose;
            afterClose = null;
            if (action == null) return;
            try { action(); }
            catch (Exception exception) { MugirlLog.WarningOnce("Corporate.DialogAction." + GetType().Name, "Corporate dialog action failed: " + exception); }
        }

        public sealed override void DoWindowContents(Rect inRect)
        {
            Color color = GUI.color;
            Color background = GUI.backgroundColor;
            Color content = GUI.contentColor;
            bool enabled = GUI.enabled;
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            bool wrap = Text.WordWrap;
            float visibility = Visibility;
            try
            {
                // CorporateUI applies the frame alpha to its primitives; leave the
                // incoming alpha intact so nested windows never multiply it twice.
                GUI.color = new Color(1f, 1f, 1f, color.a);
                GUI.enabled = enabled;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                Rect shifted = new Rect(inRect.x, inRect.y + (1f - visibility) * 14f, inRect.width, inRect.height);
                GUI.BeginGroup(shifted);
                try
                {
                    using (CorporateUI.BeginFrame(motion, "window", visibility, Interactive))
                        DrawContents(new Rect(0f, 0f, shifted.width, shifted.height));
                }
                finally { GUI.EndGroup(); }
            }
            finally
            {
                GUI.color = color;
                GUI.backgroundColor = background;
                GUI.contentColor = content;
                GUI.enabled = enabled;
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = wrap;
            }
        }

        protected abstract void DrawContents(Rect inRect);

        protected void DrawDialogFrame(Rect rect, string title, string subtitle)
        {
            CorporateUI.Fill(rect, CorporateUI.Background);
            CorporateUI.Panel(rect.ContractedBy(1f));
            CorporateUI.Fill(new Rect(1f, 1f, rect.width - 2f, 84f), CorporateUI.Background);
            CorporateUI.DrawGeometry(new Rect(rect.xMax - 144f, 4f, 140f, 77f), 0.32f);
            CorporateUI.Fill(new Rect(22f, 24f, 3f, 40f), CorporateUI.Accent);
            CorporateUI.Label(new Rect(37f, 17f, rect.width - 70f, 23f), subtitle,
                GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Label(new Rect(36f, 39f, rect.width - 70f, 34f), title, GameFont.Medium);
            CorporateUI.Rule(new Rect(22f, 85f, rect.width - 44f, 1f));
        }

        protected static float MeasureText(string text, float width)
        {
            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                return Text.CalcHeight(text ?? string.Empty, Mathf.Max(40f, width));
            }
            finally { Text.Font = font; Text.WordWrap = wrap; }
        }
    }

    internal sealed class CorporateConfirmWindow : CorporateAnimatedWindow
    {
        private readonly string text;
        private readonly Action confirmed;
        private readonly bool destructive;
        private Vector2 scroll;

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(590f, UI.screenWidth - 32f);
                float height = Mathf.Clamp(MeasureText(text, width - 76f) + 213f, 325f, 590f);
                return new Vector2(width, Mathf.Min(height, UI.screenHeight - 32f));
            }
        }

        internal CorporateConfirmWindow(string text, Action confirmed, bool destructive = false)
        {
            this.text = text ?? string.Empty;
            this.confirmed = confirmed;
            this.destructive = destructive;
            closeOnClickedOutside = false;
        }

        protected override void DrawContents(Rect inRect)
        {
            DrawDialogFrame(inRect, "Mugirl.CorporateUI.Dialog.ConfirmTitle".Translate(),
                (destructive ? "Mugirl.CorporateUI.Dialog.ReviewRequired" : "Mugirl.CorporateUI.Dialog.Authorization").Translate());
            Rect body = new Rect(24f, 105f, inRect.width - 48f, Mathf.Max(30f, inRect.height - 197f));
            Rect view = new Rect(0f, 0f, body.width - 20f, Mathf.Max(body.height, MeasureText(text, body.width - 20f) + 6f));
            CorporateUI.BeginScrollView(body, ref scroll, view);
            try { CorporateUI.Label(new Rect(0f, 0f, view.width, view.height), text); }
            finally { CorporateUI.EndScrollView(); }
            CorporateUI.Rule(new Rect(24f, inRect.height - 74f, inRect.width - 48f, 1f));
            float buttonWidth = (inRect.width - 60f) * 0.5f;
            Rect cancel = new Rect(24f, inRect.height - 57f, buttonWidth, 36f);
            Rect accept = new Rect(cancel.xMax + 12f, cancel.y, buttonWidth, 36f);
            if (CorporateUI.Button(cancel, "Cancel".Translate(), id: "cancel")) Close();
            if (CorporateUI.Button(accept, "Confirm".Translate(), enabled: confirmed != null,
                    primary: true, id: "confirm")) CloseThen(confirmed);
        }
    }

    internal sealed class CorporateChoiceWindow : CorporateAnimatedWindow
    {
        private readonly List<FloatMenuOption> options;
        private Vector2 scroll;

        public override Vector2 InitialSize
        {
            get
            {
                float width = Mathf.Min(540f, UI.screenWidth - 32f);
                float contentHeight = 0f;
                for (int i = 0; i < options.Count; i++) contentHeight += OptionHeight(options[i], width - 68f) + 8f;
                return new Vector2(width, Mathf.Min(Mathf.Clamp(contentHeight + 188f, 285f, 620f), UI.screenHeight - 32f));
            }
        }

        internal CorporateChoiceWindow(List<FloatMenuOption> options)
        {
            this.options = options == null ? new List<FloatMenuOption>() : options.FindAll(option => option != null);
            closeOnClickedOutside = true;
        }

        private static float OptionHeight(FloatMenuOption option, float width)
        {
            return Mathf.Max(46f, MeasureText(option.Label, width - 56f) + 20f);
        }

        protected override void DrawContents(Rect inRect)
        {
            DrawDialogFrame(inRect, "Mugirl.CorporateUI.Dialog.ChooseTitle".Translate(),
                "Mugirl.CorporateUI.Dialog.AvailableOptions".Translate());
            Rect body = new Rect(24f, 102f, inRect.width - 48f, Mathf.Max(30f, inRect.height - 180f));
            float width = body.width - 20f;
            float height = 0f;
            for (int i = 0; i < options.Count; i++) height += OptionHeight(options[i], width) + 8f;
            Rect view = new Rect(0f, 0f, width, Mathf.Max(body.height, height));
            CorporateUI.BeginScrollView(body, ref scroll, view);
            try
            {
                float y = 0f;
                for (int i = 0; i < options.Count; i++)
                {
                    FloatMenuOption option = options[i];
                    Rect row = new Rect(0f, y, width, OptionHeight(option, width));
                    bool available = !option.Disabled && option.action != null;
                    if (CorporateUI.Button(row, string.Empty, available, id: "option:" + i)) CloseThen(option.action);
                    CorporateUI.Label(new Rect(row.x + 12f, row.y, 28f, row.height), (i + 1).ToString("00"),
                        GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleLeft);
                    CorporateUI.Label(new Rect(row.x + 44f, row.y + 7f, row.width - 56f, row.height - 14f), option.Label,
                        color: available ? CorporateUI.Ink : CorporateUI.Muted, anchor: TextAnchor.MiddleLeft);
                    y = row.yMax + 8f;
                }
                if (options.Count == 0)
                    CorporateUI.Label(new Rect(0f, 8f, width, 50f), "Mugirl.CorporateUI.Dialog.NoOptions".Translate(), color: CorporateUI.Muted);
            }
            finally { CorporateUI.EndScrollView(); }
            CorporateUI.Rule(new Rect(24f, inRect.height - 69f, inRect.width - 48f, 1f));
            if (CorporateUI.Button(new Rect(inRect.width - 174f, inRect.height - 54f, 150f, 34f),
                    "Cancel".Translate(), id: "cancel")) Close();
        }
    }
}
