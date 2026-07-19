using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Mugirl.Features.WeaponWheel
{
    public sealed class ITab_Pawn_WeaponWheel : ITab
    {
        private const float SlotSize = 64f;
        private const float SlotGap = 8f;

        private static readonly Vector2 WindowSize = new Vector2(500f, 132f);
        private static readonly Color PanelTint = new Color(0.065f, 0.072f, 0.082f, 0.72f);
        private static readonly Color SlotBackgroundEquipped = new Color(0.145f, 0.16f, 0.18f, 0.96f);
        private static readonly Color SlotBackgroundEmpty = new Color(0.09f, 0.10f, 0.115f, 0.92f);
        private static readonly Color SlotBackgroundLocked = new Color(0.065f, 0.073f, 0.084f, 0.96f);
        private static readonly Color SlotBorderEquipped = new Color(0.31f, 0.35f, 0.38f, 0.74f);
        private static readonly Color SlotBorderEmpty = new Color(0.23f, 0.25f, 0.28f, 0.58f);
        private static readonly Color SlotBorderLocked = new Color(0.25f, 0.28f, 0.31f, 0.76f);
        private static readonly Color SlotBorderHover = new Color(0.76f, 0.70f, 0.38f, 0.94f);
        private static readonly Color SlotBorderActive = new Color(0.32f, 0.76f, 0.94f, 1f);
        private static readonly Color PrimaryAccent = new Color(0.94f, 0.68f, 0.20f, 0.96f);

        private int dragSourceIndex = -1;
        private int dragPawnId = -1;
        private readonly List<Rect> slotRects = new List<Rect>();

        public override bool IsVisible
        {
            get
            {
                Pawn pawn = SelPawn;
                return pawn != null && !pawn.Dead && MugirlIdentity.IsMugirlPawn(pawn) && pawn.TryGetComp<Comp_WeaponWheel>() != null;
            }
        }

        public ITab_Pawn_WeaponWheel()
        {
            size = WindowSize;
            labelKey = "Mugirl.WeaponWheel.TabShort";
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
            Pawn pawn = SelPawn;
            Comp_WeaponWheel comp = pawn?.TryGetComp<Comp_WeaponWheel>();
            if (pawn == null || comp == null)
            {
                return;
            }

            Rect fullRect = new Rect(0f, 0f, TabRect.width, TabRect.height);
            Widgets.DrawMenuSection(fullRect);
            Widgets.DrawBoxSolid(fullRect.ContractedBy(8f), PanelTint);
            Widgets.DrawBoxSolid(
                new Rect(9f, 9f, TabRect.width - 18f, 1f),
                new Color(0.72f, 0.78f, 0.82f, 0.08f));

            int unlockedCount = 0;
            for (int i = 0; i < comp.MaxSlots; i++)
            {
                if (comp.IsSlotUnlocked(i))
                {
                    unlockedCount++;
                }
            }

            float totalWidth = comp.MaxSlots * SlotSize + (comp.MaxSlots - 1) * SlotGap;
            float startX = (TabRect.width - totalWidth) * 0.5f;
            Rect summaryRect = new Rect(startX, 12f, totalWidth, 26f);
            DrawSummary(summaryRect, comp, comp.OccupiedUnlockedSlotCount, unlockedCount);
            Widgets.DrawBoxSolid(
                new Rect(startX, 42f, totalWidth, 1f),
                new Color(0.48f, 0.53f, 0.57f, 0.18f));

            float slotY = 52f;
            DrawSlotTray(new Rect(startX - 4f, slotY - 4f, totalWidth + 8f, SlotSize + 8f));
            slotRects.Clear();

            for (int i = 0; i < comp.MaxSlots; i++)
            {
                Rect slotRect = new Rect(
                    startX + i * (SlotSize + SlotGap),
                    slotY,
                    SlotSize,
                    SlotSize);
                slotRects.Add(slotRect);
            }

            for (int i = 0; i < slotRects.Count; i++)
            {
                DrawSlot(comp, pawn, i, slotRects[i]);
            }

            HandleDragRelease(comp, pawn);
            DrawDraggedWeapon(comp, pawn);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSlot(Comp_WeaponWheel comp, Pawn pawn, int index, Rect rect)
        {
            bool unlocked = comp.IsSlotUnlocked(index);
            ThingWithComps weapon = comp.WeaponAt(index);
            bool mouseOver = Mouse.IsOver(rect);
            bool active = weapon != null && comp.ActiveSlotIndex == index;
            bool dragging = dragSourceIndex >= 0 && dragPawnId == pawn.thingIDNumber;
            bool dragSource = dragging && dragSourceIndex == index;
            bool dragTarget = dragging && mouseOver && dragSourceIndex != index;

            Color background = !unlocked
                ? SlotBackgroundLocked
                : weapon != null ? SlotBackgroundEquipped : SlotBackgroundEmpty;
            Color border = !unlocked
                ? SlotBorderLocked
                : weapon != null ? SlotBorderEquipped : SlotBorderEmpty;
            if (index == 0 && unlocked)
            {
                border = PrimaryAccent;
            }
            if (active)
            {
                border = SlotBorderActive;
            }
            else if (dragTarget)
            {
                border = unlocked ? new Color(0.48f, 0.88f, 0.52f, 1f) : SlotBorderLocked;
            }
            else if (mouseOver && !unlocked)
            {
                border = new Color(0.48f, 0.44f, 0.27f, 0.90f);
            }
            else if (mouseOver && unlocked)
            {
                border = SlotBorderHover;
            }

            Widgets.DrawBoxSolid(rect, background);
            Widgets.DrawBoxSolid(
                new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1f),
                new Color(1f, 1f, 1f, 0.07f));
            if (mouseOver && unlocked && !dragSource)
            {
                Widgets.DrawBoxSolid(rect.ContractedBy(2f), new Color(0.34f, 0.38f, 0.42f, 0.18f));
            }
            GUI.color = border;
            Widgets.DrawBox(rect, active || dragSource || dragTarget ? 2 : 1);
            GUI.color = Color.white;
            if (active)
            {
                Widgets.DrawBoxSolid(
                    new Rect(rect.x + 7f, rect.yMax - 3f, rect.width - 14f, 2f),
                    new Color(SlotBorderActive.r, SlotBorderActive.g, SlotBorderActive.b, 0.90f));
            }

            if (!unlocked)
            {
                DrawSlotBadge(rect, index, false);
                DrawLockedSlotContent(rect, mouseOver);
                TooltipHandler.TipRegion(rect, "Mugirl.WeaponWheel.LockedDesc".Translate());

                if (Prefs.DevMode)
                {
                    Rect buttonRect = new Rect(rect.xMax - 28f, rect.yMax - 17f, 23f, 12f);
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
                Rect iconRect = rect.ContractedBy(7f);
                iconRect.y += 1f;
                iconRect.height -= 2f;
                Widgets.ThingIcon(iconRect, weapon);
                TooltipHandler.TipRegion(rect, weapon.GetTooltip());

                if (weapon.def.useHitPoints && weapon.MaxHitPoints > 0)
                {
                    float hitPointPercent = Mathf.Clamp01((float)weapon.HitPoints / weapon.MaxHitPoints);
                    Rect barRect = new Rect(rect.x + 7f, rect.yMax - 7f, rect.width - 14f, 3f);
                    Widgets.DrawBoxSolid(barRect, new Color(0.035f, 0.04f, 0.05f, 0.92f));
                    Color healthColor = Color.Lerp(new Color(0.78f, 0.22f, 0.18f), new Color(0.32f, 0.82f, 0.38f), hitPointPercent);
                    Widgets.DrawBoxSolid(new Rect(barRect.x + 1f, barRect.y + 1f, (barRect.width - 2f) * hitPointPercent, barRect.height - 2f), healthColor);
                }
            }
            else
            {
                Color addColor = new Color(0.58f, 0.62f, 0.66f, mouseOver ? 0.86f : 0.42f);
                Widgets.DrawBoxSolid(new Rect(rect.center.x - 6f, rect.center.y - 1f, 12f, 2f), addColor);
                Widgets.DrawBoxSolid(new Rect(rect.center.x - 1f, rect.center.y - 6f, 2f, 12f), addColor);
                TooltipHandler.TipRegion(rect, "Mugirl.WeaponWheel.EmptySlotDesc".Translate());
            }

            DrawSlotBadge(rect, index, true);

            if (dragSource)
            {
                Widgets.DrawBoxSolid(rect.ContractedBy(4f), new Color(0.08f, 0.12f, 0.15f, 0.42f));
            }

            Event current = Event.current;
            if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
            {
                if (current.button == 1 && weapon != null)
                {
                    OpenWeaponMenu(comp, pawn, index, weapon);
                    current.Use();
                }
                else if (current.button == 0)
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
                    current.Use();
                }
            }
        }

        private static void DrawSummary(Rect rect, Comp_WeaponWheel comp, int occupiedCount, int unlockedCount)
        {
            bool mounted = comp.IsCombatDisabledByMount();
            bool eligible = comp.IsFullFirepowerEligible() || comp.IsSwordDanceEligible();
            Color accent = mounted
                ? new Color(0.40f, 0.60f, 0.72f, 0.92f)
                : comp.IsBusy
                    ? new Color(0.90f, 0.62f, 0.24f, 0.95f)
                    : eligible
                        ? new Color(0.38f, 0.82f, 0.46f, 0.95f)
                        : new Color(0.42f, 0.48f, 0.52f, 0.78f);

            Rect statusRect = new Rect(rect.x, rect.y, rect.width - 72f, rect.height);
            Widgets.DrawBoxSolid(new Rect(statusRect.x, statusRect.y + 6f, 3f, statusRect.height - 12f), accent);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.84f, 0.87f, 0.85f, 0.94f);
            Widgets.Label(new Rect(statusRect.x + 11f, statusRect.y, statusRect.width - 11f, statusRect.height), comp.StatusLabel);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.70f, 0.73f, 0.72f, 0.88f);
            Widgets.Label(
                new Rect(rect.xMax - 66f, rect.y, 66f, rect.height),
                "Mugirl.WeaponWheel.HeaderSlots".Translate(occupiedCount, unlockedCount));
            GUI.color = Color.white;
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            TooltipHandler.TipRegion(statusRect, comp.StatusTooltip);
        }

        private static void DrawSlotTray(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.025f, 0.03f, 0.036f, 0.54f));
            Widgets.DrawBoxSolid(
                new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1f),
                new Color(0.48f, 0.56f, 0.60f, 0.10f));
        }

        private static void DrawSlotBadge(Rect slotRect, int index, bool unlocked)
        {
            Rect badge = new Rect(slotRect.x + 5f, slotRect.y + 5f, 16f, 15f);
            Color badgeBackground = !unlocked
                ? new Color(0.075f, 0.086f, 0.10f, 0.94f)
                : index == 0
                    ? new Color(0.34f, 0.24f, 0.07f, 0.92f)
                    : new Color(0.065f, 0.075f, 0.09f, 0.86f);
            Widgets.DrawBoxSolid(badge, badgeBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = !unlocked
                ? new Color(0.52f, 0.56f, 0.60f, 0.82f)
                : index == 0 ? PrimaryAccent : new Color(0.82f, 0.84f, 0.80f, 0.88f);
            Widgets.Label(badge, (index + 1).ToString());
            GUI.color = Color.white;
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
        }

        private static void DrawLockedSlotContent(Rect rect, bool mouseOver)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = mouseOver
                ? new Color(0.82f, 0.79f, 0.62f, 0.94f)
                : new Color(0.57f, 0.60f, 0.62f, 0.82f);
            Widgets.Label(rect.ContractedBy(4f), "Mugirl.WeaponWheel.Locked".Translate());
            GUI.color = Color.white;
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
        }

        private static bool DrawDeveloperUnlockButton(Rect rect)
        {
            bool mouseOver = Mouse.IsOver(rect);
            Widgets.DrawBoxSolid(
                rect,
                mouseOver
                    ? new Color(0.42f, 0.37f, 0.18f, 0.94f)
                    : new Color(0.12f, 0.14f, 0.16f, 0.92f));
            GUI.color = mouseOver
                ? new Color(0.88f, 0.79f, 0.42f, 0.98f)
                : new Color(0.37f, 0.40f, 0.42f, 0.82f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = mouseOver
                ? new Color(1f, 0.91f, 0.58f)
                : new Color(0.62f, 0.65f, 0.66f, 0.88f);
            Widgets.Label(rect, "DEV");
            GUI.color = Color.white;
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            return Widgets.ButtonInvisible(rect);
        }

        private void HandleDragRelease(Comp_WeaponWheel comp, Pawn pawn)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseUp || current.button != 0 || dragSourceIndex < 0)
            {
                return;
            }

            if (dragPawnId == pawn.thingIDNumber)
            {
                for (int i = 0; i < slotRects.Count; i++)
                {
                    if (!slotRects[i].Contains(current.mousePosition))
                    {
                        continue;
                    }
                    if (!comp.TrySwapSlots(dragSourceIndex, i, out string reason) && !reason.NullOrEmpty())
                    {
                        Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                    }
                    else
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                    break;
                }
            }

            dragSourceIndex = -1;
            dragPawnId = -1;
            current.Use();
        }

        private void DrawDraggedWeapon(Comp_WeaponWheel comp, Pawn pawn)
        {
            if (dragSourceIndex < 0 || dragPawnId != pawn.thingIDNumber || Event.current.type != EventType.Repaint)
            {
                return;
            }
            ThingWithComps weapon = comp.WeaponAt(dragSourceIndex);
            if (weapon == null)
            {
                return;
            }

            Rect ghostRect = new Rect(Event.current.mousePosition.x - 28f, Event.current.mousePosition.y - 28f, 56f, 56f);
            Rect shadow = ghostRect;
            shadow.x += 3f;
            shadow.y += 3f;
            Widgets.DrawBoxSolid(shadow, new Color(0f, 0f, 0f, 0.30f));
            Widgets.DrawBoxSolid(ghostRect, new Color(0.08f, 0.10f, 0.12f, 0.88f));
            GUI.color = new Color(SlotBorderActive.r, SlotBorderActive.g, SlotBorderActive.b, 0.88f);
            Widgets.DrawBox(ghostRect, 2);
            GUI.color = new Color(1f, 1f, 1f, 0.84f);
            Widgets.ThingIcon(ghostRect.ContractedBy(5f), weapon);
            GUI.color = Color.white;
        }

        private void ResetDragState()
        {
            dragSourceIndex = -1;
            dragPawnId = -1;
        }

        private static void BeginWeaponTargeting(Comp_WeaponWheel comp, Pawn pawn, int slotIndex)
        {
            if (comp.IsBusy)
            {
                Messages.Message("Mugirl.WeaponWheel.Busy".Translate(), pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }

            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = false,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetAnimals = false,
                canTargetHumans = false,
                canTargetMechs = false,
                canTargetItems = true,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = delegate(TargetInfo target)
                {
                    ThingWithComps weapon = target.Thing as ThingWithComps;
                    return weapon != null && weapon.Spawned && comp.CanAcceptWeapon(weapon, slotIndex, out _);
                }
            };

            MugirlGameUtility.TryBeginTargeting(parameters, delegate(LocalTargetInfo target)
            {
                ThingWithComps weapon = target.Thing as ThingWithComps;
                string reason = null;
                if (weapon == null || pawn.Destroyed || pawn.Dead || !comp.CanAcceptWeapon(weapon, slotIndex, out reason))
                {
                    if (!reason.NullOrEmpty())
                    {
                        Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                    }
                    return;
                }

                Job job = JobMaker.MakeJob(Mugirl_DefOf.Job_LoadWeaponWheel, weapon);
                job.count = slotIndex + 1;
                job.playerForced = true;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, pawn);
        }

        private static void OpenWeaponMenu(Comp_WeaponWheel comp, Pawn pawn, int slotIndex, ThingWithComps weapon)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Mugirl.WeaponWheel.DropWeapon".Translate(weapon.LabelCap), delegate
                {
                    if (!comp.TryDropSlot(slotIndex, out string reason) && !reason.NullOrEmpty())
                    {
                        Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                    }
                    else
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                })
            };
            MugirlGameUtility.TryAddWindow(new FloatMenu(options));
        }
    }
}
