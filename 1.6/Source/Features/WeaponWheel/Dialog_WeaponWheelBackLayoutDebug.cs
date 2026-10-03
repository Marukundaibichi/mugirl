using System;
using System.Collections.Generic;
using System.Globalization;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    // 参考 MRK WeaponBackEditor：角色离屏预览、朝向切换、独立参数面板和显式导出。
    // 直接预览当前 Pawn 的正式 Render Node，避免另外手绘一套不一致的遮挡关系。
    internal sealed class Dialog_WeaponWheelBackLayoutDebug : Window
    {
        // StaticCacheLifecycle: 固定翻译键，不引用 Pawn 或存档对象。
        private static readonly string[] FacingKeys = { "North", "East", "South", "West" };
        private static readonly string[] WeaponKeys =
        {
            "Mugirl.WeaponWheel.BackLayoutDebug.Weapon0",
            "Mugirl.WeaponWheel.BackLayoutDebug.Weapon1"
        };
        private Pawn previewPawn;
        private RenderTexture previewTexture;
        private bool previewDirty = true;
        private int selectedFacing = 2;
        private int selectedWeapon;
        private float zoom = 0.82f;
        private Vector2 controlsScroll;
        private string statusText;
        private readonly Dictionary<string, NumericInputState> numericInputs = new Dictionary<string, NumericInputState>();

        private sealed class NumericInputState
        {
            internal int Context;
            internal float AppliedValue;
            internal string Text;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(940f, UI.screenWidth - 40f), Mathf.Min(720f, UI.screenHeight - 40f));

        [DebugAction("Mugirl", "Back weapon layout / 背负武器微调", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void OpenDebugEditor()
        {
            if (Prefs.DevMode) MugirlGameUtility.TryAddWindow(new Dialog_WeaponWheelBackLayoutDebug());
        }

        public Dialog_WeaponWheelBackLayoutDebug()
        {
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
            foreach (Pawn pawn in PreviewCandidates())
            {
                if (previewPawn == null) previewPawn = pawn;
                if (MugirlSelectionUtility.IsSelectedInPlaying(pawn))
                {
                    previewPawn = pawn;
                    break;
                }
            }
        }

        public override void PostClose()
        {
            if (previewTexture != null)
            {
                previewTexture.Release();
                UnityEngine.Object.Destroy(previewTexture);
                previewTexture = null;
            }
            previewPawn = null;
            base.PostClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!Prefs.DevMode) { Close(); return; }
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Widgets.Label(new Rect(0f, 0f, inRect.width - 30f, 34f), "Mugirl.WeaponWheel.BackLayoutDebug.Title".Translate());
                Text.Font = GameFont.Small;
                float leftWidth = inRect.width * 0.43f;
                Rect left = new Rect(0f, 44f, leftWidth, inRect.height - 44f);
                Rect right = new Rect(leftWidth + 16f, 44f, inRect.width - leftWidth - 16f, inRect.height - 44f);
                DrawPreviewPanel(left);
                Rect view = new Rect(0f, 0f, right.width - 18f, 720f);
                Widgets.BeginScrollView(right, ref controlsScroll, view);
                DrawControls(view);
                Widgets.EndScrollView();
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
            }
        }

        private void DrawPreviewPanel(Rect rect)
        {
            if (previewPawn != null && !CanPreview(previewPawn)) { previewPawn = null; previewDirty = true; }
            string pawnLabel = previewPawn?.LabelShortCap.ToString() ?? "Mugirl.WeaponWheel.BackLayoutDebug.ChoosePawn".Translate().ToString();
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, rect.width, 32f), pawnLabel))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (Pawn pawn in PreviewCandidates())
                {
                    Pawn candidate = pawn;
                    options.Add(new FloatMenuOption(candidate.LabelShortCap, () => { previewPawn = candidate; previewDirty = true; }));
                }
                if (options.Count > 0) MugirlGameUtility.TryAddWindow(new FloatMenu(options));
                else statusText = "Mugirl.WeaponWheel.BackLayoutDebug.NoPawn".Translate();
            }
            float size = Mathf.Min(rect.width, Mathf.Max(80f, rect.height - 185f));
            Rect preview = new Rect(rect.x + (rect.width - size) / 2f, rect.y + 42f, size, size);
            Widgets.DrawMenuSection(preview);
            if (previewPawn == null)
                Widgets.Label(preview.ContractedBy(12f), "Mugirl.WeaponWheel.BackLayoutDebug.NoPawn".Translate());
            else
            {
                if (Event.current.type == EventType.Repaint && previewDirty) RenderPreview();
                if (previewTexture != null) GUI.DrawTexture(preview.ContractedBy(2f), previewTexture, ScaleMode.ScaleToFit, true);
            }
            float y = preview.yMax + 10f;
            for (int facing = 0; facing < 4; facing++)
            {
                Rect button = new Rect(rect.x + facing * rect.width / 4f, y, rect.width / 4f - 4f, 30f);
                if (facing == selectedFacing) Widgets.DrawHighlightSelected(button);
                if (Widgets.ButtonText(button, FacingKeys[facing].Translate()))
                {
                    selectedFacing = facing;
                    previewDirty = true;
                }
            }
            float newZoom = Widgets.HorizontalSlider(new Rect(rect.x, y + 42f, rect.width, 32f), zoom, 0.5f, 1.4f,
                false, "Mugirl.WeaponWheel.BackLayoutDebug.Zoom".Translate(zoom.ToString("0.00")));
            if (newZoom != zoom) { zoom = newZoom; previewDirty = true; }
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, y + 80f, rect.width, 65f), "Mugirl.WeaponWheel.BackLayoutDebug.PreviewHint".Translate());
            Text.Font = GameFont.Small;
        }

        private void RenderPreview()
        {
            previewDirty = false;
            Comp_WeaponWheel comp = previewPawn?.TryGetComp<Comp_WeaponWheel>();
            if (comp == null) return;
            if (previewTexture == null)
            {
                previewTexture = new RenderTexture(512, 512, 24) { name = "Mugirl Back Layout Preview", useMipMap = false, filterMode = FilterMode.Bilinear };
                previewTexture.Create();
            }
            bool wasActive = comp.BackLayoutPreviewActive;
            try
            {
                comp.BackLayoutPreviewActive = true;
                RequestPreviewRecache();
                if (!MugirlGameUtility.TryRenderPawnPreview(previewPawn, previewTexture, new Rot4(selectedFacing), zoom))
                    statusText = "Mugirl.WeaponWheel.BackLayoutDebug.NoPawn".Translate();
            }
            catch (Exception ex)
            {
                statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusFailed".Translate(ex.Message);
                MugirlLog.WarningOnce("BackLayoutPreview.Failed", "Back weapon preview failed: " + ex.Message);
            }
            finally
            {
                comp.BackLayoutPreviewActive = wasActive;
                // 预览与普通肖像可能同向且 flags 相同，必须让下一次绘制重新判定可见节点。
                RequestPreviewRecache();
            }
        }

        private void RequestPreviewRecache()
        {
            PawnRenderNode root = previewPawn?.Drawer?.renderer?.renderTree?.rootNode;
            if (root != null) root.requestRecache = true;
        }

        private void DrawControls(Rect rect)
        {
            for (int weapon = 0; weapon < 2; weapon++)
            {
                Rect button = new Rect(weapon * rect.width / 2f, 0f, rect.width / 2f - 4f, 32f);
                if (weapon == selectedWeapon) Widgets.DrawHighlightSelected(button);
                if (Widgets.ButtonText(button, WeaponKeys[weapon].Translate())) selectedWeapon = weapon;
            }
            ThingWithComps shown = previewPawn?.TryGetComp<Comp_WeaponWheel>()?.GetBackWeapon(selectedWeapon);
            Widgets.Label(new Rect(0f, 40f, rect.width, 48f), shown?.LabelCap.ToString() ?? "Mugirl.WeaponWheel.BackLayoutDebug.EmptyWeapon".Translate().ToString());
            int index = selectedWeapon * 4 + selectedFacing;
            WeaponWheelBackLayoutEntry entry = WeaponWheelBackLayout.Entries[index];
            bool changed = false;
            changed |= SliderRow(new Rect(0f, 96f, rect.width, 55f), "Mugirl.WeaponWheel.BackLayoutDebug.OffsetX", ref entry.OffsetX, -1.5f, 1.5f, 0.005f);
            changed |= SliderRow(new Rect(0f, 154f, rect.width, 55f), "Mugirl.WeaponWheel.BackLayoutDebug.OffsetZ", ref entry.OffsetZ, -1.5f, 1.5f, 0.005f);
            changed |= SliderRow(new Rect(0f, 212f, rect.width, 55f), "Mugirl.WeaponWheel.BackLayoutDebug.Angle", ref entry.Angle, -180f, 180f, 1f);
            changed |= SliderRow(new Rect(0f, 270f, rect.width, 55f), "Mugirl.WeaponWheel.BackLayoutDebug.Layer", ref entry.Layer, -50f, 150f, 0.5f);
            bool horizontal = entry.FlipHorizontal;
            bool vertical = entry.FlipVertical;
            Widgets.CheckboxLabeled(new Rect(0f, 328f, rect.width / 2f - 8f, 28f),
                "Mugirl.WeaponWheel.BackLayoutDebug.FlipHorizontal".Translate(), ref entry.FlipHorizontal);
            Widgets.CheckboxLabeled(new Rect(rect.width / 2f, 328f, rect.width / 2f - 8f, 28f),
                "Mugirl.WeaponWheel.BackLayoutDebug.FlipVertical".Translate(), ref entry.FlipVertical);
            TooltipHandler.TipRegion(new Rect(0f, 328f, rect.width, 28f), "Mugirl.WeaponWheel.BackLayoutDebug.MirrorHint".Translate());
            changed |= horizontal != entry.FlipHorizontal || vertical != entry.FlipVertical;
            if (changed) { WeaponWheelBackLayout.Entries[index] = entry; previewDirty = true; }
            if (selectedFacing == 2)
            {
                float layer = WeaponWheelBackLayout.SouthLayerWithAutoloading;
                if (SliderRow(new Rect(0f, 368f, rect.width, 55f), "Mugirl.WeaponWheel.BackLayoutDebug.SouthAutoloadingLayer", ref layer, -50f, 150f, 0.5f))
                { WeaponWheelBackLayout.SouthLayerWithAutoloading = layer; previewDirty = true; }
            }
            float half = rect.width / 2f;
            ActionButton(new Rect(0f, 438f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.ResetPose", () =>
            { WeaponWheelBackLayout.Entries[index] = WeaponWheelBackLayout.Defaults[index]; previewDirty = true; });
            ActionButton(new Rect(half, 438f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.Reset", () =>
            { WeaponWheelBackLayout.ResetToDefaults(); previewDirty = true; statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusReset".Translate(); });
            ActionButton(new Rect(0f, 478f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.CopyXml", () => Copy(WeaponWheelBackLayout.ExportXml()));
            ActionButton(new Rect(half, 478f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.CopyCSharp", () => Copy(WeaponWheelBackLayout.ExportCSharp()));
            ActionButton(new Rect(0f, 518f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.WriteFile", () =>
            { statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusWritten".Translate(WeaponWheelBackLayout.WriteOverrideFile()); });
            ActionButton(new Rect(half, 518f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.OpenFolder", () =>
            { System.IO.Directory.CreateDirectory(WeaponWheelBackLayout.OverrideDirectory); Application.OpenURL(new Uri(WeaponWheelBackLayout.OverrideDirectory + System.IO.Path.DirectorySeparatorChar).AbsoluteUri); });
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, 562f, rect.width, 150f), "Mugirl.WeaponWheel.BackLayoutDebug.Hint".Translate(WeaponWheelBackLayout.OverrideFilePath) + "\n" + statusText);
            Text.Font = GameFont.Small;
        }

        private bool SliderRow(Rect rect, string key, ref float value, float min, float max, float step)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), key.Translate(value.ToString("0.####", CultureInfo.InvariantCulture)));
            int context = selectedWeapon * 4 + selectedFacing;
            if (!numericInputs.TryGetValue(key, out NumericInputState input))
            {
                input = new NumericInputState { Context = -1 };
                numericInputs.Add(key, input);
            }
            // 切换武器/朝向或恢复默认后同步文本；单独输入 '-' 或小数点时保留未完成输入。
            if (input.Context != context || input.AppliedValue != value)
            {
                input.Context = context;
                input.AppliedValue = value;
                input.Text = value.ToString("0.####", CultureInfo.InvariantCulture);
            }
            float result = value;
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 26f, 28f, 26f), "−")) result -= step;
            result = Widgets.HorizontalSlider(new Rect(rect.x + 36f, rect.y + 28f, Mathf.Max(30f, rect.width - 178f), 24f), result, min, max);
            if (Widgets.ButtonText(new Rect(rect.xMax - 134f, rect.y + 26f, 28f, 26f), "+")) result += step;
            if (result != value)
            {
                result = Mathf.Clamp((float)Math.Round(result, 4), min, max);
                input.Text = result.ToString("0.####", CultureInfo.InvariantCulture);
            }
            Rect field = new Rect(rect.xMax - 98f, rect.y + 26f, 98f, 26f);
            input.Text = Widgets.TextField(field, input.Text);
            bool valid = TryReadNumericInput(input.Text, min, max, out float entered);
            if (valid) result = entered;
            else Widgets.DrawBoxSolid(new Rect(field.x, field.yMax - 2f, field.width, 2f), Color.red);
            TooltipHandler.TipRegion(field, "Mugirl.WeaponWheel.BackLayoutDebug.NumericHint".Translate(min, max));
            input.AppliedValue = result;
            if (value == result) return false;
            value = result;
            return true;
        }

        internal static bool TryReadNumericInput(string text, float min, float max, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }

        private void ActionButton(Rect rect, string key, Action action)
        {
            if (!Widgets.ButtonText(rect, key.Translate())) return;
            try { action(); }
            catch (Exception ex) { statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusFailed".Translate(ex.Message); }
        }

        private void Copy(string text)
        {
            GUIUtility.systemCopyBuffer = text;
            statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusCopied".Translate();
        }

        private static bool CanPreview(Pawn pawn) => pawn != null && pawn.Spawned && !pawn.Dead
            && !pawn.GetPosture().InBed() && pawn.TryGetComp<Comp_WeaponWheel>() != null;

        private static IEnumerable<Pawn> PreviewCandidates()
        {
            List<Map> maps = MugirlGameUtility.LoadedMaps;
            if (maps == null) yield break;
            foreach (Map map in maps)
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (CanPreview(pawn)) yield return pawn;
        }
    }
}
