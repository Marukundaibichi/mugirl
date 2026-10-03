using System;
using System.Collections.Generic;
using System.Globalization;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // 暂停游戏并在地图上的真实 Render Tree 预览两名 Pawn；滑杆变化立即重建喂奶姿态。
    internal sealed class Dialog_MilkFeedingLayoutDebug : Window
    {
        private readonly Pawn source;
        private readonly Rot4 originalSourceFacing;
        private Pawn recipient;
        private Pawn previewedRecipient;
        private Rot4 originalRecipientFacing;
        private bool previewDirty = true;
        private Vector2 scroll;
        private string statusText;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(510f, UI.screenWidth - 40f), Mathf.Min(710f, UI.screenHeight - 40f));

        [DebugAction("Mugirl", "Feeding layout / 喂奶姿态微调", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void OpenDebugEditor()
        {
            if (!Prefs.DevMode) return;
            List<Map> maps = MugirlGameUtility.LoadedMaps;
            if (maps == null) return;
            foreach (Map map in maps)
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (CanSource(pawn) && MugirlSelectionUtility.IsSelectedInPlaying(pawn))
                    {
                        Open(pawn);
                        return;
                    }
        }

        internal static void Open(Pawn source)
        {
            if (Prefs.DevMode && CanSource(source))
                MugirlGameUtility.TryAddWindow(new Dialog_MilkFeedingLayoutDebug(source));
        }

        private Dialog_MilkFeedingLayoutDebug(Pawn source)
        {
            this.source = source;
            originalSourceFacing = source.Rotation;
            doCloseX = true;
            draggable = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            foreach (Pawn candidate in Recipients())
            {
                recipient = candidate;
                break;
            }
        }

        public override void PostClose()
        {
            ReleasePreview();
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!Prefs.DevMode || !CanSource(source)) { Close(); return; }
            if (!CanRecipient(recipient)) { ReleasePreview(); recipient = null; }

            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "Mugirl.Milk.FeedingDebug.Title".Translate());
                Text.Font = GameFont.Small;

                string recipientName = recipient?.LabelShortCap.ToString() ?? "Mugirl.Milk.FeedingDebug.Choose".Translate().ToString();
                if (Widgets.ButtonText(new Rect(0f, 40f, inRect.width, 32f), recipientName))
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (Pawn candidate in Recipients())
                    {
                        Pawn choice = candidate;
                        options.Add(new FloatMenuOption(choice.LabelShortCap, () =>
                        {
                            ReleasePreview();
                            recipient = choice;
                            previewDirty = true;
                        }));
                    }
                    if (options.Count > 0) MugirlGameUtility.TryAddWindow(new FloatMenu(options));
                    else statusText = "Mugirl.Milk.FeedingDebug.NoRecipient".Translate();
                }

                Widgets.Label(new Rect(0f, 80f, inRect.width, 64f), "Mugirl.Milk.FeedingDebug.Hint".Translate());
                Rect view = new Rect(0f, 0f, inRect.width - 18f, 590f);
                Widgets.BeginScrollView(new Rect(0f, 148f, inRect.width, inRect.height - 148f), ref scroll, view);
                DrawControls(view);
                Widgets.EndScrollView();

                if (previewDirty && CanRecipient(recipient))
                {
                    if (previewedRecipient != recipient)
                    {
                        ReleasePreview();
                        originalRecipientFacing = recipient.Rotation;
                        previewedRecipient = recipient;
                    }
                    MugirlMilkingAnimation.StartFeeding(source, recipient);
                    previewDirty = false;
                }
            }
            finally
            {
                Text.Font = oldFont;
                GUI.color = oldColor;
            }
        }

        private void DrawControls(Rect rect)
        {
            MilkFeedingLayoutEntry value = MilkFeedingLayout.Current;
            bool changed = false;
            changed |= SliderRow(0f, rect.width, "Mugirl.Milk.FeedingDebug.SourceX", ref value.SourceX, -1f, 1f, 0.005f);
            changed |= SliderRow(59f, rect.width, "Mugirl.Milk.FeedingDebug.SourceZ", ref value.SourceZ, -1f, 1f, 0.005f);
            changed |= SliderRow(118f, rect.width, "Mugirl.Milk.FeedingDebug.RecipientX", ref value.RecipientX, -1f, 1f, 0.005f);
            changed |= SliderRow(177f, rect.width, "Mugirl.Milk.FeedingDebug.RecipientZ", ref value.RecipientZ, -1f, 1f, 0.005f);
            changed |= SliderRow(236f, rect.width, "Mugirl.Milk.FeedingDebug.SourceAngle", ref value.SourceAngle, -45f, 45f, 0.5f);
            changed |= SliderRow(295f, rect.width, "Mugirl.Milk.FeedingDebug.RecipientAngle", ref value.RecipientAngle, -45f, 45f, 0.5f);
            if (changed) { MilkFeedingLayout.Current = value; previewDirty = true; }

            float half = rect.width / 2f;
            ActionButton(new Rect(0f, 365f, half - 4f, 32f), "Mugirl.Milk.FeedingDebug.Reset", () =>
            { MilkFeedingLayout.ResetToDefaults(); previewDirty = true; });
            ActionButton(new Rect(half, 365f, half - 4f, 32f), "Mugirl.Milk.FeedingDebug.Jump", () => CameraJumper.TryJumpAndSelect(source));
            ActionButton(new Rect(0f, 405f, half - 4f, 32f), "Mugirl.Milk.FeedingDebug.Copy", () =>
            { GUIUtility.systemCopyBuffer = MilkFeedingLayout.ExportXml(); statusText = "Mugirl.Milk.FeedingDebug.Copied".Translate(); });
            ActionButton(new Rect(half, 405f, half - 4f, 32f), "Mugirl.Milk.FeedingDebug.Save", () =>
            { statusText = "Mugirl.Milk.FeedingDebug.Saved".Translate(MilkFeedingLayout.WriteOverrideFile()); });
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, 455f, rect.width, 120f), MilkFeedingLayout.OverrideFilePath + "\n" + statusText);
            Text.Font = GameFont.Small;
        }

        private static bool SliderRow(float y, float width, string key, ref float value, float min, float max, float step)
        {
            Widgets.Label(new Rect(0f, y, width, 26f), key.Translate(value.ToString("0.###", CultureInfo.InvariantCulture)));
            float result = value;
            if (Widgets.ButtonText(new Rect(0f, y + 27f, 30f, 25f), "−")) result -= step;
            result = Widgets.HorizontalSlider(new Rect(38f, y + 28f, width - 84f, 24f), result, min, max);
            if (Widgets.ButtonText(new Rect(width - 38f, y + 27f, 30f, 25f), "+")) result += step;
            result = Mathf.Clamp((float)Math.Round(result, 3), min, max);
            if (result == value) return false;
            value = result;
            return true;
        }

        private void ActionButton(Rect rect, string key, Action action)
        {
            if (!Widgets.ButtonText(rect, key.Translate())) return;
            try { action(); }
            catch (Exception ex) { statusText = "Mugirl.Milk.FeedingDebug.Failed".Translate(ex.Message); }
        }

        private void ReleasePreview()
        {
            if (previewedRecipient == null) return;
            MugirlMilkingAnimation.EndFeeding(source, previewedRecipient);
            if (CanSource(source)) source.Rotation = originalSourceFacing;
            if (!previewedRecipient.Destroyed && previewedRecipient.Spawned)
                previewedRecipient.Rotation = originalRecipientFacing;
            previewedRecipient = null;
        }

        private static bool CanSource(Pawn pawn) => pawn != null && pawn.Spawned && !pawn.Dead
            && pawn.TryGetComp<CompMooMilkable>() != null;

        private bool CanRecipient(Pawn pawn) => pawn != null && pawn != source && pawn.Spawned && !pawn.Dead
            && pawn.Map == source.Map && pawn.RaceProps?.Humanlike == true
            && Vector3.Distance(pawn.DrawPos, source.DrawPos) <= 2.5f;

        private IEnumerable<Pawn> Recipients()
        {
            if (!CanSource(source)) yield break;
            foreach (Pawn pawn in source.Map.mapPawns.AllPawnsSpawned)
                if (CanRecipient(pawn)) yield return pawn;
        }
    }
}
