using Mugirl.Platform;
using Mugirl.Platform.Ui;
using Verse.AI;
using Verse.Sound;

namespace Mugirl.Features.WeaponWheel;

// 武器轮盘检查面板（XML inspectorTabs 引用）：六个槽位，左键拖动交换、左键空槽选取地面武器装填、
// 右键丢弃。手写拖动需要逐事件的鼠标数据，经 Mugirl.Platform.Ui.UiEvent 读取。
public sealed class ITab_Pawn_WeaponWheel : ITab
{
    private const float SlotSize = 64f;
    private const float SlotGap = 8f;
    private const int LeftButton = 0;
    private const int RightButton = 1;

    private int dragSourceIndex = -1;
    private int dragPawnId = -1;
    private readonly List<Rect> slotRects = [];

    public ITab_Pawn_WeaponWheel()
    {
        size = new UVector2(500f, 132f);
        labelKey = "Mugirl.WeaponWheel.TabShort";
    }

    public override bool IsVisible
    {
        get
        {
            Pawn? pawn = SelPawn;
            return pawn != null && !pawn.Dead && MugirlIdentity.IsMugirlPawn(pawn) && pawn.TryGetComp<CompWeaponWheel>() != null;
        }
    }

    public override void Notify_ClickOutsideWindow()
    {
        base.Notify_ClickOutsideWindow();
        ResetDragState();
    }

    protected override void CloseTab()
    {
        ResetDragState();
        base.CloseTab();
    }

    protected override void FillTab()
    {
        Pawn? pawn = SelPawn;
        CompWeaponWheel? comp = pawn?.TryGetComp<CompWeaponWheel>();
        if (pawn == null || comp == null)
        {
            return;
        }

        Rect fullRect = new(0f, 0f, TabRect.width, TabRect.height);
        Widgets.DrawMenuSection(fullRect);
        Widgets.DrawBoxSolid(fullRect.ContractedBy(8f), WeaponWheelUiStyle.PanelTint);
        Widgets.DrawBoxSolid(new Rect(9f, 9f, TabRect.width - 18f, 1f), WeaponWheelUiStyle.PanelTopLine);

        int maxSlots = comp.MaxSlots;
        int unlockedCount = 0;
        for (int i = 0; i < maxSlots; i++)
        {
            if (comp.IsSlotUnlocked(i))
            {
                unlockedCount++;
            }
        }

        float totalWidth = maxSlots * SlotSize + (maxSlots - 1) * SlotGap;
        float startX = (TabRect.width - totalWidth) * 0.5f;
        DrawSummary(new Rect(startX, 12f, totalWidth, 26f), comp, comp.OccupiedUnlockedSlotCount, unlockedCount);
        Widgets.DrawBoxSolid(new Rect(startX, 42f, totalWidth, 1f), WeaponWheelUiStyle.SummaryDivider);

        const float slotY = 52f;
        DrawSlotTray(new Rect(startX - 4f, slotY - 4f, totalWidth + 8f, SlotSize + 8f));
        slotRects.Clear();
        for (int i = 0; i < maxSlots; i++)
        {
            slotRects.Add(new Rect(startX + i * (SlotSize + SlotGap), slotY, SlotSize, SlotSize));
        }

        for (int i = 0; i < slotRects.Count; i++)
        {
            DrawSlot(comp, pawn, i, slotRects[i]);
        }

        HandleDragRelease(comp, pawn);
        DrawDraggedWeapon(comp, pawn);
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private void DrawSlot(CompWeaponWheel comp, Pawn pawn, int index, Rect rect)
    {
        bool unlocked = comp.IsSlotUnlocked(index);
        ThingWithComps? weapon = comp.WeaponAt(index);
        bool mouseOver = Mouse.IsOver(rect);
        bool active = weapon != null && comp.ActiveSlotIndex == index;
        bool dragging = dragSourceIndex >= 0 && dragPawnId == pawn.thingIDNumber;
        bool dragSource = dragging && dragSourceIndex == index;
        bool dragTarget = dragging && mouseOver && dragSourceIndex != index;

        Color background = !unlocked
            ? WeaponWheelUiStyle.SlotBackgroundLocked
            : weapon != null ? WeaponWheelUiStyle.SlotBackgroundEquipped : WeaponWheelUiStyle.SlotBackgroundEmpty;
        Color border = SlotBorder(index, unlocked, weapon != null, active, dragTarget, mouseOver);

        Widgets.DrawBoxSolid(rect, background);
        Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1f), WeaponWheelUiStyle.SlotTopLine);
        if (mouseOver && unlocked && !dragSource)
        {
            Widgets.DrawBoxSolid(rect.ContractedBy(2f), WeaponWheelUiStyle.SlotHoverFill);
        }
        using (new TextBlock(border))
        {
            Widgets.DrawBox(rect, active || dragSource || dragTarget ? 2 : 1);
        }
        if (active)
        {
            Widgets.DrawBoxSolid(
                new Rect(rect.x + 7f, rect.yMax - 3f, rect.width - 14f, 2f),
                UnityColor.WithAlpha(WeaponWheelUiStyle.SlotBorderActive, 0.90f));
        }

        if (!unlocked)
        {
            DrawSlotBadge(rect, index, false);
            DrawLockedSlotContent(rect, mouseOver);
            if (mouseOver)
            {
                TooltipHandler.TipRegion(rect, "Mugirl.WeaponWheel.LockedDesc".Translate());
            }

            if (Prefs.DevMode)
            {
                Rect buttonRect = new(rect.xMax - 28f, rect.yMax - 17f, 23f, 12f);
                if (DrawDeveloperUnlockButton(buttonRect))
                {
                    comp.UnlockSlotDeveloper(index);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                TooltipHandler.TipRegion(buttonRect, "Mugirl.WeaponWheel.DevUnlock".Translate());
            }
            return;
        }

        if (weapon != null)
        {
            DrawWeaponContent(rect, weapon, mouseOver);
        }
        else
        {
            Color addColor = WeaponWheelUiStyle.AddMark(mouseOver);
            Vector2 center = UnityRect.Center(rect);
            Widgets.DrawBoxSolid(new Rect(center.X - 6f, center.Y - 1f, 12f, 2f), addColor);
            Widgets.DrawBoxSolid(new Rect(center.X - 1f, center.Y - 6f, 2f, 12f), addColor);
            if (mouseOver)
            {
                TooltipHandler.TipRegion(rect, "Mugirl.WeaponWheel.EmptySlotDesc".Translate());
            }
        }

        DrawSlotBadge(rect, index, true);

        if (dragSource)
        {
            Widgets.DrawBoxSolid(rect.ContractedBy(4f), WeaponWheelUiStyle.SlotDragSourceFill);
        }

        HandleSlotClick(comp, pawn, index, rect, weapon);
    }

    private static Color SlotBorder(int index, bool unlocked, bool occupied, bool active, bool dragTarget, bool mouseOver)
    {
        if (active)
        {
            return WeaponWheelUiStyle.SlotBorderActive;
        }
        if (dragTarget)
        {
            return unlocked ? WeaponWheelUiStyle.SlotBorderDropTarget : WeaponWheelUiStyle.SlotBorderLocked;
        }
        if (mouseOver)
        {
            return unlocked ? WeaponWheelUiStyle.SlotBorderHover : WeaponWheelUiStyle.SlotBorderLockedHover;
        }
        if (index == 0 && unlocked)
        {
            return WeaponWheelUiStyle.PrimaryAccent;
        }
        return !unlocked
            ? WeaponWheelUiStyle.SlotBorderLocked
            : occupied ? WeaponWheelUiStyle.SlotBorderEquipped : WeaponWheelUiStyle.SlotBorderEmpty;
    }

    private static void DrawWeaponContent(Rect rect, ThingWithComps weapon, bool mouseOver)
    {
        Rect contracted = rect.ContractedBy(7f);
        Rect iconRect = new(contracted.x, contracted.y + 1f, contracted.width, contracted.height - 2f);
        Widgets.ThingIcon(iconRect, weapon);
        // GetTooltip 每次都拼接字符串；TipRegion 本身只在鼠标悬停时显示，先判断悬停省掉其余事件的构造。
        if (mouseOver)
        {
            TooltipHandler.TipRegion(rect, weapon.GetTooltip());
        }

        if (weapon.def.useHitPoints && weapon.MaxHitPoints > 0)
        {
            float hitPointPercent = UnityMathf.Clamp01((float)weapon.HitPoints / weapon.MaxHitPoints);
            Rect barRect = new(rect.x + 7f, rect.yMax - 7f, rect.width - 14f, 3f);
            Widgets.DrawBoxSolid(barRect, WeaponWheelUiStyle.HealthBarBackground);
            Color healthColor = UnityColor.Lerp(WeaponWheelUiStyle.HealthLow, WeaponWheelUiStyle.HealthHigh, hitPointPercent);
            Widgets.DrawBoxSolid(new Rect(barRect.x + 1f, barRect.y + 1f, (barRect.width - 2f) * hitPointPercent, barRect.height - 2f), healthColor);
        }
    }

    // 右键有武器的槽位打开丢弃菜单；左键空槽开始选取地面武器，左键有武器的槽位开始拖动（忙碌时不允许）。
    private void HandleSlotClick(CompWeaponWheel comp, Pawn pawn, int index, Rect rect, ThingWithComps? weapon)
    {
        if (!UnityRect.Contains(rect, UiEvent.MousePosition))
        {
            return;
        }

        if (UiEvent.IsMouseDown(RightButton) && weapon != null)
        {
            OpenWeaponMenu(comp, pawn, index, weapon);
            UiEvent.Use();
        }
        else if (UiEvent.IsMouseDown(LeftButton))
        {
            if (weapon == null)
            {
                BeginWeaponTargeting(comp, pawn, index);
            }
            else if (!comp.IsBusy)
            {
                dragSourceIndex = index;
                dragPawnId = pawn.thingIDNumber;
            }
            UiEvent.Use();
        }
    }

    private static void DrawSummary(Rect rect, CompWeaponWheel comp, int occupiedCount, int unlockedCount)
    {
        WeaponWheelStatus status = comp.EvaluateStatus();
        string label = status.Label();
        Color accent = comp.IsCombatDisabledByMount()
            ? WeaponWheelUiStyle.AccentMounted
            : comp.IsBusy
                ? WeaponWheelUiStyle.AccentBusy
                : status.AnyLinkedMode ? WeaponWheelUiStyle.AccentEligible : WeaponWheelUiStyle.AccentIdle;

        Rect statusRect = new(rect.x, rect.y, rect.width - 72f, rect.height);
        Widgets.DrawBoxSolid(new Rect(statusRect.x, statusRect.y + 6f, 3f, statusRect.height - 12f), accent);
        UiDraw.Label(
            new Rect(statusRect.x + 11f, statusRect.y, statusRect.width - 11f, statusRect.height),
            label,
            WeaponWheelUiStyle.StatusText,
            GameFont.Tiny,
            TextAnchor.MiddleLeft);
        UiDraw.Label(
            new Rect(rect.xMax - 66f, rect.y, 66f, rect.height),
            "Mugirl.WeaponWheel.HeaderSlots".Translate(occupiedCount, unlockedCount),
            WeaponWheelUiStyle.SlotCountText,
            GameFont.Tiny,
            TextAnchor.MiddleRight);
        if (Mouse.IsOver(statusRect))
        {
            TooltipHandler.TipRegion(statusRect, status.Tooltip());
        }
    }

    private static void DrawSlotTray(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, WeaponWheelUiStyle.TrayBackground);
        Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1f), WeaponWheelUiStyle.TrayTopLine);
    }

    private static void DrawSlotBadge(Rect slotRect, int index, bool unlocked)
    {
        Rect badge = new(slotRect.x + 5f, slotRect.y + 5f, 16f, 15f);
        Color badgeBackground = !unlocked
            ? WeaponWheelUiStyle.BadgeLockedBackground
            : index == 0 ? WeaponWheelUiStyle.BadgePrimaryBackground : WeaponWheelUiStyle.BadgeBackground;
        Widgets.DrawBoxSolid(badge, badgeBackground);

        Color textColor = !unlocked
            ? WeaponWheelUiStyle.BadgeLockedText
            : index == 0 ? WeaponWheelUiStyle.PrimaryAccent : WeaponWheelUiStyle.BadgeText;
        UiDraw.Label(badge, (index + 1).ToStringCached(), textColor, GameFont.Tiny, TextAnchor.MiddleCenter);
    }

    private static void DrawLockedSlotContent(Rect rect, bool mouseOver)
    {
        UiDraw.Label(
            rect.ContractedBy(4f),
            "Mugirl.WeaponWheel.Locked".Translate(),
            mouseOver ? WeaponWheelUiStyle.LockedTextHover : WeaponWheelUiStyle.LockedText,
            GameFont.Tiny,
            TextAnchor.MiddleCenter);
    }

    private static bool DrawDeveloperUnlockButton(Rect rect)
    {
        bool mouseOver = Mouse.IsOver(rect);
        Widgets.DrawBoxSolid(rect, mouseOver ? WeaponWheelUiStyle.DevButtonBackgroundHover : WeaponWheelUiStyle.DevButtonBackground);
        using (new TextBlock(mouseOver ? WeaponWheelUiStyle.DevButtonBorderHover : WeaponWheelUiStyle.DevButtonBorder))
        {
            Widgets.DrawBox(rect, 1);
        }
        UiDraw.Label(rect, "DEV", mouseOver ? WeaponWheelUiStyle.DevButtonTextHover : WeaponWheelUiStyle.DevButtonText, GameFont.Tiny, TextAnchor.MiddleCenter);
        return Widgets.ButtonInvisible(rect);
    }

    private void HandleDragRelease(CompWeaponWheel comp, Pawn pawn)
    {
        if (dragSourceIndex < 0)
        {
            return;
        }

        // 用原始事件类型：在面板内任何位置松开（包括槽位之外、裁剪区之外）都结束拖动。
        if (UiEvent.IsRawMouseUp(LeftButton))
        {
            if (dragPawnId == pawn.thingIDNumber)
            {
                TryDropOnSlot(comp, pawn, UiEvent.MousePosition);
            }
            ResetDragState();
            UiEvent.Use();
            return;
        }

        // 在面板窗口之外松开时本面板收不到 MouseUp（1.6 中拖影会一直跟随鼠标直到下次点击）：
        // 每帧 Repaint 时若左键已经不在按住状态就取消拖动。
        if (GuiPhase.IsRepaint && !EngineInput.LeftMouseDown)
        {
            ResetDragState();
        }
    }

    private void TryDropOnSlot(CompWeaponWheel comp, Pawn pawn, Vector2 mousePosition)
    {
        for (int i = 0; i < slotRects.Count; i++)
        {
            if (!UnityRect.Contains(slotRects[i], mousePosition))
            {
                continue;
            }
            if (!comp.TrySwapSlots(dragSourceIndex, i, out string? reason) && !reason.NullOrEmpty())
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
            }
            else
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            return;
        }
    }

    private void DrawDraggedWeapon(CompWeaponWheel comp, Pawn pawn)
    {
        if (dragSourceIndex < 0 || dragPawnId != pawn.thingIDNumber || !GuiPhase.IsRepaint)
        {
            return;
        }
        ThingWithComps? weapon = comp.WeaponAt(dragSourceIndex);
        if (weapon == null)
        {
            return;
        }

        Vector2 mouse = UiEvent.MousePosition;
        Rect ghostRect = new(mouse.X - 28f, mouse.Y - 28f, 56f, 56f);
        Rect shadow = new(ghostRect.x + 3f, ghostRect.y + 3f, ghostRect.width, ghostRect.height);
        Widgets.DrawBoxSolid(shadow, WeaponWheelUiStyle.GhostShadow);
        Widgets.DrawBoxSolid(ghostRect, WeaponWheelUiStyle.GhostBackground);
        using (new TextBlock(UnityColor.WithAlpha(WeaponWheelUiStyle.SlotBorderActive, 0.88f)))
        {
            Widgets.DrawBox(ghostRect, 2);
        }
        // ThingIcon 会把 GUI 颜色改成物品颜色，1.6 在此之前设置的 0.84 透明度从未生效；保持不透明的外观。
        Widgets.ThingIcon(ghostRect.ContractedBy(5f), weapon);
    }

    private void ResetDragState()
    {
        dragSourceIndex = -1;
        dragPawnId = -1;
    }

    private static void BeginWeaponTargeting(CompWeaponWheel comp, Pawn pawn, int slotIndex)
    {
        if (comp.IsBusy)
        {
            Messages.Message("Mugirl.WeaponWheel.Busy".Translate(), pawn, MessageTypeDefOf.RejectInput, false);
            return;
        }

        TargetingParameters parameters = new()
        {
            canTargetLocations = false,
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetAnimals = false,
            canTargetHumans = false,
            canTargetMechs = false,
            canTargetItems = true,
            mapObjectTargetsMustBeAutoAttackable = false,
            validator = target => target.Thing is ThingWithComps weapon
                && weapon.Spawned
                && comp.CanAcceptWeapon(weapon, slotIndex, out _)
        };

        GameUtility.TryBeginTargeting(parameters, target =>
        {
            ThingWithComps? weapon = target.Thing as ThingWithComps;
            string? reason = null;
            if (weapon == null || pawn.Destroyed || pawn.Dead || !comp.CanAcceptWeapon(weapon, slotIndex, out reason))
            {
                if (!reason.NullOrEmpty())
                {
                    Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                }
                return;
            }

            Job job = WeaponWheelJobs.MakeLoadJob(weapon, slotIndex);
            job.playerForced = true;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }, pawn);
    }

    private static void OpenWeaponMenu(CompWeaponWheel comp, Pawn pawn, int slotIndex, ThingWithComps weapon)
    {
        List<FloatMenuOption> options =
        [
            new FloatMenuOption("Mugirl.WeaponWheel.DropWeapon".Translate(weapon.LabelCap), () =>
            {
                if (!comp.TryDropSlot(slotIndex, out string? reason) && !reason.NullOrEmpty())
                {
                    Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
            })
        ];
        GameUtility.TryAddWindow(new FloatMenu(options));
    }
}
