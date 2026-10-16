using System.Globalization;
using System.IO;
using LudeonTK;
using Mugirl.Platform;
using Mugirl.Platform.Ui;

namespace Mugirl.Features.WeaponWheel;

// 开发者模式的背负武器布局调试窗口（参考 MRK WeaponBackEditor）：角色离屏预览、朝向切换、独立参数面板和显式导出。
// 直接预览当前 Pawn 的正式 Render Node，避免另外手绘一套不一致的遮挡关系。
internal sealed class Dialog_WeaponWheelBackLayoutDebug : Window
{
    // 预览尺寸与缩放参数独一无二，PortraitsCache 为它单独建缓存条目，不会与殖民者栏等普通肖像共用纹理。
    private static readonly UVector2 PreviewSize = new(512f, 512f);

    private readonly Dictionary<string, NumericInputState> numericInputs = [];
    private Pawn? previewPawn;
    private bool previewDirty = true;
    private int selectedFacing = Rot4.SouthInt;
    private int selectedWeapon;
    private float zoom = 0.82f;
    private UVector2 controlsScroll;
    private string? statusText;

    private sealed class NumericInputState
    {
        internal int Context = -1;
        internal float AppliedValue;
        internal string Text = string.Empty;
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

    public override UVector2 InitialSize => new(UnityMathf.Min(940f, UI.screenWidth - 40f), UnityMathf.Min(720f, UI.screenHeight - 40f));

    [DebugAction("Mugirl", "Back weapon layout / 背负武器微调", actionType = DebugActionType.Action,
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void OpenDebugEditor()
    {
        if (Prefs.DevMode)
        {
            GameUtility.TryAddWindow(new Dialog_WeaponWheelBackLayoutDebug());
        }
    }

    public override void PreOpen()
    {
        base.PreOpen();
        foreach (Pawn pawn in PreviewCandidates())
        {
            previewPawn ??= pawn;
            if (SelectionUtility.IsSelectedInPlaying(pawn))
            {
                previewPawn = pawn;
                break;
            }
        }
    }

    public override void PostClose()
    {
        previewPawn = null;
        base.PostClose();
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (!Prefs.DevMode)
        {
            Close();
            return;
        }

        using (new TextBlock(GameFont.Medium, TextAnchor.UpperLeft, UnityColor.White))
        {
            Widgets.Label(new Rect(0f, 0f, inRect.width - 30f, 34f), "Mugirl.WeaponWheel.BackLayoutDebug.Title".Translate());
            Text.Font = GameFont.Small;
            float leftWidth = inRect.width * 0.43f;
            Rect left = new(0f, 44f, leftWidth, inRect.height - 44f);
            Rect right = new(leftWidth + 16f, 44f, inRect.width - leftWidth - 16f, inRect.height - 44f);
            DrawPreviewPanel(left);
            Rect view = new(0f, 0f, right.width - 18f, 720f);
            Widgets.BeginScrollView(right, ref controlsScroll, view);
            try
            {
                DrawControls(view);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }
    }

    private void DrawPreviewPanel(Rect rect)
    {
        if (previewPawn != null && !CanPreview(previewPawn))
        {
            previewPawn = null;
            previewDirty = true;
        }

        string pawnLabel = previewPawn?.LabelShortCap.ToString() ?? "Mugirl.WeaponWheel.BackLayoutDebug.ChoosePawn".Translate().ToString();
        if (Widgets.ButtonText(new Rect(rect.x, rect.y, rect.width, 32f), pawnLabel))
        {
            List<FloatMenuOption> options = [];
            foreach (Pawn pawn in PreviewCandidates())
            {
                Pawn candidate = pawn;
                options.Add(new FloatMenuOption(candidate.LabelShortCap, () =>
                {
                    previewPawn = candidate;
                    previewDirty = true;
                }));
            }
            if (options.Count > 0)
            {
                GameUtility.TryAddWindow(new FloatMenu(options));
            }
            else
            {
                statusText = "Mugirl.WeaponWheel.BackLayoutDebug.NoPawn".Translate();
            }
        }

        float size = UnityMathf.Min(rect.width, UnityMathf.Max(80f, rect.height - 185f));
        Rect preview = new(rect.x + (rect.width - size) / 2f, rect.y + 42f, size, size);
        Widgets.DrawMenuSection(preview);
        if (previewPawn == null)
        {
            Widgets.Label(preview.ContractedBy(12f), "Mugirl.WeaponWheel.BackLayoutDebug.NoPawn".Translate());
        }
        else if (GuiPhase.IsRepaint)
        {
            Texture? texture = RenderPreview(previewPawn);
            if (texture is not null)
            {
                Widgets.DrawTextureFitted(preview.ContractedBy(2f), texture, 1f);
            }
        }

        float y = preview.yMax + 10f;
        for (int facing = 0; facing < 4; facing++)
        {
            Rect button = new(rect.x + facing * rect.width / 4f, y, rect.width / 4f - 4f, 30f);
            if (facing == selectedFacing)
            {
                Widgets.DrawHighlightSelected(button);
            }
            if (Widgets.ButtonText(button, new Rot4(facing).ToStringHuman()))
            {
                selectedFacing = facing;
                previewDirty = true;
            }
        }

        float newZoom = Widgets.HorizontalSlider(new Rect(rect.x, y + 42f, rect.width, 32f), zoom, 0.5f, 1.4f,
            false, "Mugirl.WeaponWheel.BackLayoutDebug.Zoom".Translate(zoom.ToString("0.00", CultureInfo.CurrentCulture)));
        if (newZoom != zoom)
        {
            zoom = newZoom;
            previewDirty = true;
        }
        using (new TextBlock(GameFont.Tiny))
        {
            Widgets.Label(new Rect(rect.x, y + 80f, rect.width, 65f), "Mugirl.WeaponWheel.BackLayoutDebug.PreviewHint".Translate());
        }
    }

    // 每次 Repaint 都向 PortraitsCache 取一次纹理：取用会刷新条目的存活时间，纹理不跨帧持有（过期后会回到原版的纹理池）。
    // 只有缓存被标脏时 Get 才同步重绘；重绘期间打开预览开关，并在前后让渲染树重新判定节点可见性
    // （预览与普通肖像可能同向且标志相同，不重建就会沿用上一次的可见节点）。
    private Texture? RenderPreview(Pawn pawn)
    {
        CompWeaponWheel? comp = pawn.TryGetComp<CompWeaponWheel>();
        if (comp == null)
        {
            return null;
        }

        bool wasActive = comp.BackLayoutPreviewActive;
        try
        {
            comp.BackLayoutPreviewActive = true;
            if (previewDirty)
            {
                previewDirty = false;
                PortraitsCache.SetDirty(pawn);
            }
            RequestPreviewRecache(pawn);
            return PortraitsCache.Get(pawn, PreviewSize, new Rot4(selectedFacing), default, zoom, supersample: false, compensateForUIScale: false);
        }
        catch (Exception ex)
        {
            statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusFailed".Translate(ex.Message);
            MugirlLog.WarningOnce("BackLayoutPreview.Failed", "Back weapon preview failed: " + ex.Message);
            return null;
        }
        finally
        {
            comp.BackLayoutPreviewActive = wasActive;
            RequestPreviewRecache(pawn);
        }
    }

    private static void RequestPreviewRecache(Pawn pawn)
    {
        PawnRenderNode? root = pawn.Drawer?.renderer?.renderTree?.rootNode;
        if (root != null)
        {
            root.requestRecache = true;
        }
    }

    private void DrawControls(Rect rect)
    {
        for (int weapon = 0; weapon < WeaponWheelBackLayoutData.WeaponCount; weapon++)
        {
            Rect button = new(weapon * rect.width / 2f, 0f, rect.width / 2f - 4f, 32f);
            if (weapon == selectedWeapon)
            {
                Widgets.DrawHighlightSelected(button);
            }
            string label = weapon == 0
                ? "Mugirl.WeaponWheel.BackLayoutDebug.Weapon0".Translate()
                : "Mugirl.WeaponWheel.BackLayoutDebug.Weapon1".Translate();
            if (Widgets.ButtonText(button, label))
            {
                selectedWeapon = weapon;
            }
        }

        ThingWithComps? shown = previewPawn?.TryGetComp<CompWeaponWheel>()?.GetBackWeapon(selectedWeapon);
        Widgets.Label(new Rect(0f, 40f, rect.width, 48f), shown?.LabelCap.ToString() ?? "Mugirl.WeaponWheel.BackLayoutDebug.EmptyWeapon".Translate().ToString());

        int index = WeaponWheelBackLayoutData.IndexOf(selectedWeapon, selectedFacing);
        WeaponWheelBackLayoutEntry entry = WeaponWheelBackLayout.GetEntry(index);
        bool changed = false;
        changed |= SliderRow(new Rect(0f, 96f, rect.width, 55f), "OffsetX",
            "Mugirl.WeaponWheel.BackLayoutDebug.OffsetX".Translate(WeaponWheelBackLayoutData.FormatNumber(entry.OffsetX)),
            ref entry.OffsetX, -WeaponWheelBackLayoutData.OffsetRange, WeaponWheelBackLayoutData.OffsetRange, 0.005f);
        changed |= SliderRow(new Rect(0f, 154f, rect.width, 55f), "OffsetZ",
            "Mugirl.WeaponWheel.BackLayoutDebug.OffsetZ".Translate(WeaponWheelBackLayoutData.FormatNumber(entry.OffsetZ)),
            ref entry.OffsetZ, -WeaponWheelBackLayoutData.OffsetRange, WeaponWheelBackLayoutData.OffsetRange, 0.005f);
        changed |= SliderRow(new Rect(0f, 212f, rect.width, 55f), "Angle",
            "Mugirl.WeaponWheel.BackLayoutDebug.Angle".Translate(WeaponWheelBackLayoutData.FormatNumber(entry.Angle)),
            ref entry.Angle, -WeaponWheelBackLayoutData.AngleRange, WeaponWheelBackLayoutData.AngleRange, 1f);
        changed |= SliderRow(new Rect(0f, 270f, rect.width, 55f), "Layer",
            "Mugirl.WeaponWheel.BackLayoutDebug.Layer".Translate(WeaponWheelBackLayoutData.FormatNumber(entry.Layer)),
            ref entry.Layer, WeaponWheelBackLayoutData.LayerMin, WeaponWheelBackLayoutData.LayerMax, 0.5f);

        bool horizontal = entry.FlipHorizontal;
        bool vertical = entry.FlipVertical;
        Widgets.CheckboxLabeled(new Rect(0f, 328f, rect.width / 2f - 8f, 28f),
            "Mugirl.WeaponWheel.BackLayoutDebug.FlipHorizontal".Translate(), ref entry.FlipHorizontal);
        Widgets.CheckboxLabeled(new Rect(rect.width / 2f, 328f, rect.width / 2f - 8f, 28f),
            "Mugirl.WeaponWheel.BackLayoutDebug.FlipVertical".Translate(), ref entry.FlipVertical);
        TooltipHandler.TipRegion(new Rect(0f, 328f, rect.width, 28f), "Mugirl.WeaponWheel.BackLayoutDebug.MirrorHint".Translate());
        bool mirrorChanged = horizontal != entry.FlipHorizontal || vertical != entry.FlipVertical;
        if (changed || mirrorChanged)
        {
            WeaponWheelBackLayout.SetEntry(index, entry);
            previewDirty = true;
            if (mirrorChanged)
            {
                // 镜像体现在节点网格上，绘制请求缓存到重建为止；位置、角度与图层每帧现读，不需要重建。
                RequestLayoutRecacheOnMaps();
            }
        }

        if (selectedFacing == Rot4.SouthInt)
        {
            float layer = WeaponWheelBackLayout.SouthLayerWithAutoloading;
            if (SliderRow(new Rect(0f, 368f, rect.width, 55f), "SouthAutoloadingLayer",
                "Mugirl.WeaponWheel.BackLayoutDebug.SouthAutoloadingLayer".Translate(WeaponWheelBackLayoutData.FormatNumber(layer)),
                ref layer, WeaponWheelBackLayoutData.LayerMin, WeaponWheelBackLayoutData.LayerMax, 0.5f))
            {
                WeaponWheelBackLayout.SouthLayerWithAutoloading = layer;
                previewDirty = true;
            }
        }

        float half = rect.width / 2f;
        ActionButton(new Rect(0f, 438f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.ResetPose".Translate(), () =>
        {
            WeaponWheelBackLayout.SetEntry(index, WeaponWheelBackLayoutData.DefaultAt(index));
            previewDirty = true;
            RequestLayoutRecacheOnMaps();
        });
        ActionButton(new Rect(half, 438f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.Reset".Translate(), () =>
        {
            WeaponWheelBackLayout.ResetToDefaults();
            previewDirty = true;
            RequestLayoutRecacheOnMaps();
            statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusReset".Translate();
        });
        ActionButton(new Rect(0f, 478f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.CopyXml".Translate(),
            () => Copy(WeaponWheelBackLayout.ExportXml()));
        ActionButton(new Rect(half, 478f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.CopyCSharp".Translate(),
            () => Copy(WeaponWheelBackLayout.ExportCSharp()));
        ActionButton(new Rect(0f, 518f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.WriteFile".Translate(), () =>
        {
            statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusWritten".Translate(WeaponWheelBackLayout.WriteOverrideFile());
        });
        ActionButton(new Rect(half, 518f, half - 4f, 32f), "Mugirl.WeaponWheel.BackLayoutDebug.OpenFolder".Translate(), () =>
        {
            Directory.CreateDirectory(WeaponWheelBackLayout.OverrideDirectory);
            EngineShell.OpenFolder(WeaponWheelBackLayout.OverrideDirectory);
        });

        using (new TextBlock(GameFont.Tiny))
        {
            Widgets.Label(new Rect(0f, 562f, rect.width, 150f),
                "Mugirl.WeaponWheel.BackLayoutDebug.Hint".Translate(WeaponWheelBackLayout.OverrideFilePath) + "\n" + statusText);
        }
    }

    // inputId 只用于区分各行的输入框状态；label 由调用方用字面量键翻译（含当前数值）。
    private bool SliderRow(Rect rect, string inputId, string label, ref float value, float min, float max, float step)
    {
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), label);
        int context = WeaponWheelBackLayoutData.IndexOf(selectedWeapon, selectedFacing);
        if (!numericInputs.TryGetValue(inputId, out NumericInputState? input))
        {
            input = new NumericInputState();
            numericInputs.Add(inputId, input);
        }
        // 切换武器/朝向或恢复默认后同步文本；单独输入 '-' 或小数点时保留未完成输入。
        if (input.Context != context || input.AppliedValue != value)
        {
            input.Context = context;
            input.AppliedValue = value;
            input.Text = WeaponWheelBackLayoutData.FormatNumber(value);
        }

        float result = value;
        if (Widgets.ButtonText(new Rect(rect.x, rect.y + 26f, 28f, 26f), "−"))
        {
            result -= step;
        }
        result = Widgets.HorizontalSlider(new Rect(rect.x + 36f, rect.y + 28f, UnityMathf.Max(30f, rect.width - 178f), 24f), result, min, max);
        if (Widgets.ButtonText(new Rect(rect.xMax - 134f, rect.y + 26f, 28f, 26f), "+"))
        {
            result += step;
        }
        if (result != value)
        {
            result = UnityMathf.Clamp((float)Math.Round(result, 4), min, max);
            input.Text = WeaponWheelBackLayoutData.FormatNumber(result);
        }

        Rect field = new(rect.xMax - 98f, rect.y + 26f, 98f, 26f);
        input.Text = Widgets.TextField(field, input.Text);
        if (WeaponWheelBackLayoutData.TryReadNumber(input.Text, min, max, out float entered))
        {
            result = entered;
        }
        else
        {
            Widgets.DrawBoxSolid(new Rect(field.x, field.yMax - 2f, field.width, 2f), UnityColor.Red);
        }

        TooltipHandler.TipRegion(field, "Mugirl.WeaponWheel.BackLayoutDebug.NumericHint".Translate(min, max));
        input.AppliedValue = result;
        if (value == result)
        {
            return false;
        }

        value = result;
        return true;
    }

    private void ActionButton(Rect rect, string label, Action action)
    {
        if (!Widgets.ButtonText(rect, label))
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusFailed".Translate(ex.Message);
        }
    }

    private void Copy(string text)
    {
        EngineClipboard.Text = text;
        statusText = "Mugirl.WeaponWheel.BackLayoutDebug.StatusCopied".Translate();
    }

    // 地图上所有轮盘 Pawn 重建背负节点的绘制请求，使镜像改动在游戏画面中立即可见。
    private static void RequestLayoutRecacheOnMaps()
    {
        foreach (Pawn pawn in PreviewCandidates())
        {
            pawn.TryGetComp<CompWeaponWheel>()?.RequestBackWeaponRecache();
        }
    }

    private static bool CanPreview(Pawn pawn) => pawn.Spawned && !pawn.Dead
        && !pawn.GetPosture().InBed() && pawn.TryGetComp<CompWeaponWheel>() != null;

    private static IEnumerable<Pawn> PreviewCandidates()
    {
        List<Map>? maps = GameUtility.LoadedMaps;
        if (maps == null)
        {
            yield break;
        }

        foreach (Map map in maps)
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (CanPreview(pawn))
                {
                    yield return pawn;
                }
            }
        }
    }
}
