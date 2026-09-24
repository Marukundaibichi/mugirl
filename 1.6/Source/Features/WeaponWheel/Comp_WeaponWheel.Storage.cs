using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mugirl.Features.WeaponWheel
{
    public sealed partial class Comp_WeaponWheel
    {
        public bool CanAcceptWeapon(ThingWithComps weapon, int slotIndex, out string reason)
        {
            reason = null;
            EnsureCollections();

            Pawn pawn = Pawn;
            if (pawn == null || pawn.equipment == null || weapon == null || weapon.def?.equipmentType != EquipmentType.Primary)
            {
                reason = "Mugirl.WeaponWheel.InvalidWeapon".Translate();
                return false;
            }
            if (slotIndex < 0 || slotIndex >= slots.Count || !unlockedSlots[slotIndex])
            {
                reason = "Mugirl.WeaponWheel.SlotLocked".Translate();
                return false;
            }
            if (slots[slotIndex] != null)
            {
                reason = "Mugirl.WeaponWheel.SlotOccupied".Translate();
                return false;
            }
            if (IsBusy)
            {
                reason = "Mugirl.WeaponWheel.Busy".Translate();
                return false;
            }
            if (ContainsWeapon(weapon))
            {
                reason = "Mugirl.WeaponWheel.AlreadyStored".Translate();
                return false;
            }
            if (!EquipmentUtility.CanEquip(weapon, pawn, out reason))
            {
                return false;
            }
            return true;
        }

        public bool TryAttachGroundWeapon(ThingWithComps weapon, int slotIndex, out string reason)
        {
            if (!CanAcceptWeapon(weapon, slotIndex, out reason))
            {
                return false;
            }

            Pawn pawn = Pawn;
            Map map = weapon.Map;
            IntVec3 originalPosition = weapon.Position;
            ThingWithComps attachedWeapon = weapon;
            if (weapon.def.stackLimit > 1 && weapon.stackCount > 1)
            {
                attachedWeapon = (ThingWithComps)weapon.SplitOff(1);
            }
            else
            {
                weapon.DeSpawn();
            }

            bool attached = false;
            slots[slotIndex] = attachedWeapon;
            try
            {
                using (WeaponWheelTransferScope.Registration())
                {
                    if (slotIndex == 0)
                    {
                        if (pawn.equipment.Primary != null)
                        {
                            reason = "Mugirl.WeaponWheel.SlotOccupied".Translate();
                            return false;
                        }
                        pawn.equipment.AddEquipment(attachedWeapon);
                    }
                    else if (reserveWeapons.TryAdd(attachedWeapon))
                    {
                        pawn.equipment.Notify_EquipmentAdded(attachedWeapon);
                    }
                    else
                    {
                        reason = "Mugirl.WeaponWheel.CannotStore".Translate();
                        return false;
                    }
                }
                attached = true;
                activeSlotIndex = 0;
                attachedWeapon.def.soundInteract?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                MarkBackWeaponsDirty();
                return true;
            }
            finally
            {
                if (!attached)
                {
                    slots[slotIndex] = null;
                    if (reserveWeapons.Contains(attachedWeapon))
                    {
                        reserveWeapons.Remove(attachedWeapon);
                    }
                    if (!attachedWeapon.Spawned && attachedWeapon.ParentHolder == null && !attachedWeapon.Destroyed && map != null)
                    {
                        GenPlace.TryPlaceThing(attachedWeapon, originalPosition, map, ThingPlaceMode.Near);
                    }
                }
            }
        }

        internal bool TryAddGeneratedReserveWeapon(ThingWithComps weapon)
        {
            EnsureCollections();
            Pawn pawn = Pawn;
            if (pawn?.equipment == null || weapon == null || !weapon.def.IsRangedWeapon
                || weapon.def.equipmentType != EquipmentType.Primary || ContainsWeapon(weapon))
            {
                return false;
            }

            int slotIndex = FirstUnlockedEmptySlot(1);
            if (slotIndex < 0 || !reserveWeapons.TryAdd(weapon))
            {
                return false;
            }

            slots[slotIndex] = weapon;
            using (WeaponWheelTransferScope.Registration())
            {
                pawn.equipment.Notify_EquipmentAdded(weapon);
            }
            MarkBackWeaponsDirty();
            return true;
        }

        public bool TrySwapSlots(int firstIndex, int secondIndex, out string reason)
        {
            reason = null;
            EnsureCollections();
            if (firstIndex == secondIndex)
            {
                return true;
            }
            if (firstIndex < 0 || firstIndex >= slots.Count || secondIndex < 0 || secondIndex >= slots.Count
                || !unlockedSlots[firstIndex] || !unlockedSlots[secondIndex])
            {
                reason = "Mugirl.WeaponWheel.SlotLocked".Translate();
                return false;
            }
            if (IsBusy)
            {
                reason = "Mugirl.WeaponWheel.Busy".Translate();
                return false;
            }

            ThingWithComps first = slots[firstIndex];
            slots[firstIndex] = slots[secondIndex];
            slots[secondIndex] = first;

            if (firstIndex == 0 || secondIndex == 0)
            {
                if (!NormalizeToPrimarySlot())
                {
                    first = slots[firstIndex];
                    slots[firstIndex] = slots[secondIndex];
                    slots[secondIndex] = first;
                    NormalizeToPrimarySlot();
                    reason = "Mugirl.WeaponWheel.CannotSwap".Translate();
                    return false;
                }
            }

            MarkBackWeaponsDirty();
            return true;
        }

        public bool TryDropSlot(int slotIndex, out string reason)
        {
            reason = null;
            EnsureCollections();
            Pawn pawn = Pawn;
            if (pawn == null || !pawn.Spawned || slotIndex < 0 || slotIndex >= slots.Count)
            {
                reason = "Mugirl.WeaponWheel.CannotDrop".Translate();
                return false;
            }
            if (IsBusy)
            {
                reason = "Mugirl.WeaponWheel.Busy".Translate();
                return false;
            }

            ThingWithComps weapon = slots[slotIndex];
            if (weapon == null)
            {
                return true;
            }

            if (weapon == pawn.equipment.Primary)
            {
                if (!pawn.equipment.TryDropEquipment(weapon, out ThingWithComps dropped, pawn.Position, false))
                {
                    reason = "Mugirl.WeaponWheel.CannotDrop".Translate();
                    return false;
                }
                if (dropped != null)
                {
                    dropped.SetForbidden(false, false);
                }
            }
            else
            {
                using (WeaponWheelTransferScope.Registration())
                {
                    pawn.equipment.Notify_EquipmentRemoved(weapon);
                }
                slots[slotIndex] = null;
                if (!reserveWeapons.TryDrop(weapon, pawn.Position, pawn.Map, ThingPlaceMode.Near, out ThingWithComps dropped))
                {
                    slots[slotIndex] = weapon;
                    using (WeaponWheelTransferScope.Registration())
                    {
                        pawn.equipment.Notify_EquipmentAdded(weapon);
                    }
                    reason = "Mugirl.WeaponWheel.CannotDrop".Translate();
                    return false;
                }
                dropped?.SetForbidden(false, false);
            }

            slots[slotIndex] = null;
            activeSlotIndex = 0;
            NormalizeToPrimarySlot();
            MarkBackWeaponsDirty();
            return true;
        }

        private static void ResetBurstingVerbs(ThingWithComps weapon)
        {
            List<Verb> verbs = weapon?.GetComp<CompEquippable>()?.AllVerbs;
            if (verbs == null)
            {
                return;
            }
            for (int i = 0; i < verbs.Count; i++)
            {
                if (verbs[i].state == VerbState.Bursting)
                {
                    verbs[i].Reset();
                }
            }
        }

        internal bool MoveActiveWeaponTo(int slotIndex)
        {
            EnsureCollections();
            Pawn pawn = Pawn;
            if (pawn?.equipment == null || slotIndex < 0 || slotIndex >= slots.Count)
            {
                return false;
            }

            ThingWithComps desired = slots[slotIndex];
            if (desired == null || !unlockedSlots[slotIndex])
            {
                return false;
            }

            ThingWithComps current = pawn.equipment.Primary;
            if (current == desired)
            {
                activeSlotIndex = slotIndex;
                return true;
            }
            if (!reserveWeapons.Contains(desired))
            {
                return false;
            }

            using (WeaponWheelTransferScope.InternalTransfer())
            {
                ResetBurstingVerbs(current);
                if (current != null && !pawn.equipment.TryTransferEquipmentToContainer(current, reserveWeapons))
                {
                    return false;
                }
                if (!reserveWeapons.Remove(desired))
                {
                    if (current != null && reserveWeapons.Remove(current))
                    {
                        pawn.equipment.AddEquipment(current);
                    }
                    return false;
                }
                pawn.equipment.AddEquipment(desired);
            }

            activeSlotIndex = slotIndex;
            MarkBackWeaponsDirty();
            return true;
        }

        internal bool NormalizeToPrimarySlot()
        {
            EnsureCollections();
            Pawn pawn = Pawn;
            if (pawn?.equipment == null)
            {
                return false;
            }

            ThingWithComps desired = slots[0];
            ThingWithComps current = pawn.equipment.Primary;
            if (current == desired)
            {
                activeSlotIndex = 0;
                return true;
            }
            if (current != null && !ContainsWeapon(current))
            {
                return desired == null;
            }

            using (WeaponWheelTransferScope.InternalTransfer())
            {
                if (current != null && ContainsWeapon(current))
                {
                    ResetBurstingVerbs(current);
                    if (!pawn.equipment.TryTransferEquipmentToContainer(current, reserveWeapons))
                    {
                        return false;
                    }
                }
                if (desired != null)
                {
                    if (!reserveWeapons.Remove(desired))
                    {
                        return false;
                    }
                    pawn.equipment.AddEquipment(desired);
                }
            }

            activeSlotIndex = 0;
            MarkBackWeaponsDirty();
            return true;
        }

        internal void NotifyExternalEquipmentAdded(ThingWithComps equipment)
        {
            if (equipment == null || equipment.def?.equipmentType != EquipmentType.Primary)
            {
                return;
            }

            EnsureCollections();
            AbortCombatAndRestorePrimary(false);
            int existingIndex = slots.IndexOf(equipment);
            if (existingIndex >= 0)
            {
                slots[existingIndex] = null;
            }

            ThingWithComps displaced = slots[0];
            if (displaced != null && displaced != equipment)
            {
                int emptyIndex = FirstUnlockedEmptySlot(1);
                if (emptyIndex >= 0)
                {
                    slots[emptyIndex] = displaced;
                }
            }

            slots[0] = equipment;
            activeSlotIndex = 0;
            MarkBackWeaponsDirty();
        }

        internal void NotifyExternalEquipmentRemoved(ThingWithComps equipment)
        {
            int index = IndexOfWeapon(equipment);
            if (index < 0)
            {
                return;
            }

            AbortCombatAndRestorePrimary(false);
            slots[index] = null;
            activeSlotIndex = 0;
            MarkBackWeaponsDirty();
        }

        private void TrackExternalPrimaryIfNeeded()
        {
            Pawn pawn = Pawn;
            ThingWithComps primary = pawn?.equipment?.Primary;
            if (primary == null)
            {
                return;
            }

            int index = slots.IndexOf(primary);
            if (index >= 0)
            {
                activeSlotIndex = index;
                return;
            }
            NotifyExternalEquipmentAdded(primary);
        }

        private void TrackWeaponsRemovedFromReserve()
        {
            Pawn pawn = Pawn;
            ThingWithComps primary = pawn?.equipment?.Primary;
            for (int i = 0; i < slots.Count; i++)
            {
                ThingWithComps weapon = slots[i];
                if (weapon == null || weapon == primary || reserveWeapons.Contains(weapon))
                {
                    continue;
                }

                slots[i] = null;
                using (WeaponWheelTransferScope.Registration())
                {
                    pawn?.equipment?.Notify_EquipmentRemoved(weapon);
                }
                MarkBackWeaponsDirty();
            }
        }

        private int FirstUnlockedEmptySlot(int startIndex)
        {
            for (int i = Mathf.Max(0, startIndex); i < slots.Count; i++)
            {
                if (unlockedSlots[i] && slots[i] == null)
                {
                    return i;
                }
            }
            return -1;
        }

        internal ThingWithComps GetBackWeapon(int displayIndex)
        {
            EnsureCollections();
            bool useAutoloadingLayout = HasAutoloadingSystem;
            if (backWeaponCacheDirty || backWeaponCacheAutoloadingLayout != useAutoloadingLayout)
            {
                RebuildBackWeaponCache(useAutoloadingLayout);
            }
            return displayIndex == 0
                ? firstBackWeapon
                : displayIndex == 1 ? secondBackWeapon : null;
        }

        internal ThingWithComps GetAutoloadingWeapon(int displayIndex)
        {
            EnsureCollections();
            if (displayIndex < 0 || displayIndex >= 4 || !HasAutoloadingSystem)
            {
                return null;
            }
            return WeaponVisibleInStorageSlot(displayIndex + 2);
        }

        private void RebuildBackWeaponCache(bool useAutoloadingLayout)
        {
            backWeaponCacheDirty = false;
            backWeaponCacheAutoloadingLayout = useAutoloadingLayout;
            firstBackWeapon = null;
            secondBackWeapon = null;
            if (useAutoloadingLayout)
            {
                // 装备自驱装弹系统时，第 2 格仍按普通背负武器显示；第 3–6 格固定进入四个凹槽。
                // 固定槽位映射可避免轮射切枪时其余武器在凹槽之间跳位。
                firstBackWeapon = WeaponVisibleInStorageSlot(1);
                return;
            }

            ThingWithComps primary = Pawn?.equipment?.Primary;
            for (int i = 0; i < slots.Count; i++)
            {
                ThingWithComps weapon = slots[i];
                if (!unlockedSlots[i] || weapon == null || weapon == primary
                    || weapon == animationOutgoingWeapon || weapon == animationIncomingWeapon)
                {
                    continue;
                }
                if (firstBackWeapon == null)
                {
                    firstBackWeapon = weapon;
                }
                else
                {
                    secondBackWeapon = weapon;
                    break;
                }
            }
        }

        private ThingWithComps WeaponVisibleInStorageSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count || !unlockedSlots[slotIndex])
            {
                return null;
            }
            ThingWithComps weapon = slots[slotIndex];
            if (weapon == null || weapon == Pawn?.equipment?.Primary
                || weapon == animationOutgoingWeapon || weapon == animationIncomingWeapon)
            {
                return null;
            }
            return weapon;
        }

        internal void HandleDropAndForbidEverything(bool keepInventoryAndEquipmentIfInBed)
        {
            Pawn pawn = Pawn;
            if (pawn == null || (keepInventoryAndEquipmentIfInBed && pawn.InBed() && !pawn.kindDef.destroyGearOnDrop))
            {
                return;
            }

            AbortCombatAndRestorePrimary(false);
            ThingOwner destination = null;
            if (pawn.InContainerEnclosed && pawn.ParentHolder != null)
            {
                destination = pawn.ParentHolder.GetDirectlyHeldThings();
            }

            for (int i = reserveWeapons.Count - 1; i >= 0; i--)
            {
                ThingWithComps weapon = reserveWeapons[i];
                int slotIndex = slots.IndexOf(weapon);
                bool canHandle = pawn.kindDef.destroyGearOnDrop
                    || destination != null
                    || (pawn.SpawnedOrAnyParentSpawned && pawn.MapHeld != null);
                if (!canHandle)
                {
                    continue;
                }

                using (WeaponWheelTransferScope.Registration())
                {
                    pawn.equipment?.Notify_EquipmentRemoved(weapon);
                }

                bool handled = false;
                if (pawn.kindDef.destroyGearOnDrop)
                {
                    reserveWeapons.Remove(weapon);
                    weapon.Destroy();
                    handled = true;
                }
                else if (destination != null)
                {
                    handled = reserveWeapons.TryTransferToContainer(weapon, destination);
                }
                else if (pawn.SpawnedOrAnyParentSpawned && pawn.MapHeld != null)
                {
                    handled = reserveWeapons.TryDrop(weapon, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near, out ThingWithComps dropped);
                    if (handled)
                    {
                        dropped?.SetForbidden(true, false);
                    }
                }

                if (handled)
                {
                    if (slotIndex >= 0)
                    {
                        slots[slotIndex] = null;
                    }
                }
                else
                {
                    using (WeaponWheelTransferScope.Registration())
                    {
                        pawn.equipment?.Notify_EquipmentAdded(weapon);
                    }
                }
            }
            MarkBackWeaponsDirty();
        }
    }
}
