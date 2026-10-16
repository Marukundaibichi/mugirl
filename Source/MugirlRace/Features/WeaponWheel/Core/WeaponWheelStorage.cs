using Mugirl.Features.Mounting;
using Verse.AI;
using Verse.Sound;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.WeaponWheel;
internal sealed class WeaponWheelStorage
{
    private readonly CompWeaponWheel owner;
    internal WeaponWheelStorage(CompWeaponWheel owner)
    {
        this.owner = owner;
    }

    internal ThingOwner<ThingWithComps> reserveWeapons = null!;
    internal List<WeaponWheelSlot> slots = [];
    internal bool collectionsReady;
    private bool researchUnlockApplied;
    internal bool reserveMassDirty = true;
    private float reserveMassCache;
    internal int activeSlotIndex;
    internal readonly List<CompEquippable> reserveVerbTickers = [];
    internal bool reserveVerbTickersDirty = true;
    internal ThingWithComps? WeaponAt(int index)
    {
        EnsureCollections();
        return index >= 0 && index < slots.Count ? slots[index].Weapon : null;
    }

    internal bool IsSlotUnlocked(int index)
    {
        EnsureCollections();
        return index >= 0 && index < slots.Count && slots[index].Unlocked;
    }

    internal bool UnlockSlotDeveloper(int index)
    {
        EnsureCollections();
        if (!Prefs.DevMode || index < 0 || index >= owner.MaxSlots || slots[index].Unlocked)
        {
            return false;
        }

        slots[index].Unlocked = true;
        owner.MarkBackWeaponsDirty();
        return true;
    }

    internal int OccupiedUnlockedSlotCount
    {
        get
        {
            EnsureCollections();
            return WeaponWheelSlotMath.CountOccupiedUnlocked(slots);
        }
    }

    internal float ReserveWeaponMass
    {
        get
        {
            if (!reserveMassDirty)
            {
                return reserveMassCache;
            }

            EnsureCollections();
            float mass = 0f;
            for (int i = 0; i < reserveWeapons.Count; i++)
            {
                ThingWithComps weapon = reserveWeapons[i];
                mass += weapon.GetStatValue(StatDefOf.Mass, true, 1) * weapon.stackCount;
            }

            reserveMassDirty = false;
            reserveMassCache = mass;
            return mass;
        }
    }

    internal bool ContainsWeapon(ThingWithComps? weapon)
    {
        return weapon != null && IndexOfWeapon(weapon) >= 0;
    }

    internal int IndexOfWeapon(ThingWithComps? weapon)
    {
        EnsureCollections();
        return weapon == null ? -1 : IndexOfSlotWeapon(weapon);
    }

    private int IndexOfSlotWeapon(ThingWithComps? weapon)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (EqualityComparer<ThingWithComps?>.Default.Equals(slots[i].Weapon, weapon))
            {
                return i;
            }
        }
        return -1;
    }

    internal void EnsureCollections()
    {
        int slotCount = owner.MaxSlots;
        if (collectionsReady && reserveWeapons != null && slots != null && slots.Count == slotCount)
        {
            return;
        }

        reserveWeapons ??= new ThingOwner<ThingWithComps>(owner);
        slots ??= new List<WeaponWheelSlot>(slotCount);
        while (slots.Count < slotCount)
        {
            slots.Add(new WeaponWheelSlot());
        }
        if (slots.Count > slotCount)
        {
            slots.RemoveRange(slotCount, slots.Count - slotCount);
        }
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i] ??= new WeaponWheelSlot();
        }
        int baseUnlocked = UnityMathf.Clamp(owner.Props?.baseUnlockedSlots ?? 3, 0, slotCount);
        researchUnlockApplied = GameUtility.IsResearchFinished(WeaponWheelDefOf.Mugirl_Autoloading);
        int unlockedCount = researchUnlockApplied ? slotCount : baseUnlocked;
        for (int i = 0; i < unlockedCount; i++)
        {
            slots[i].Unlocked = true;
        }

        collectionsReady = true;
        reserveVerbTickersDirty = true;
        owner.renderCache.backWeaponVisibilityMask = -1;
        reserveMassDirty = true;
    }

    internal void RefreshResearchUnlocks()
    {
        if (researchUnlockApplied || !GameUtility.IsResearchFinished(WeaponWheelDefOf.Mugirl_Autoloading))
        {
            return;
        }

        researchUnlockApplied = true;
        bool changed = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].Unlocked)
            {
                slots[i].Unlocked = true;
                changed = true;
            }
        }

        if (changed)
        {
            owner.MarkBackWeaponsDirty();
        }
    }

    internal void ValidateSlotReferences()
    {
        Pawn? pawn = owner.Pawn;
        ThingWithComps? primary = pawn?.equipment?.Primary;
        HashSet<ThingWithComps> seen = [];
        for (int i = 0; i < slots.Count; i++)
        {
            ThingWithComps? weapon = slots[i].Weapon;
            if (weapon == null)
            {
                continue;
            }

            if (weapon.Destroyed || (!reserveWeapons.Contains(weapon) && weapon != primary) || !seen.Add(weapon))
            {
                slots[i].Weapon = null;
            }
        }

        activeSlotIndex = primary == null ? 0 : Math.Max(0, IndexOfSlotWeapon(primary));
        if (pawn == null)
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            List<Verb>? verbs = slots[i].Weapon?.GetComp<CompEquippable>()?.AllVerbs;
            if (verbs == null)
            {
                continue;
            }

            for (int j = 0; j < verbs.Count; j++)
            {
                verbs[j].caster = pawn;
            }
        }
    }

    internal void TickReserveVerbs()
    {
        if (reserveVerbTickersDirty)
        {
            reserveVerbTickersDirty = false;
            reserveVerbTickers.Clear();
            for (int i = 0; i < reserveWeapons.Count; i++)
            {
                ThingWithComps weapon = reserveWeapons[i];
                // 自带 Normal ticker 的武器由 ThingOwner 自己驱动 Verb，不重复 tick。
                if (weapon?.def?.tickerType == TickerType.Normal)
                {
                    continue;
                }

                CompEquippable? equippable = weapon?.GetComp<CompEquippable>();
                if (equippable != null)
                {
                    reserveVerbTickers.Add(equippable);
                }
            }
        }

        for (int i = 0; i < reserveVerbTickers.Count; i++)
        {
            reserveVerbTickers[i]?.verbTracker?.VerbsTick();
        }
    }

    internal bool CanAcceptWeapon(ThingWithComps? weapon, int slotIndex, out string? reason)
    {
        reason = null;
        EnsureCollections();
        Pawn? pawn = owner.Pawn;
        if (pawn == null || pawn.equipment == null || weapon == null || weapon.def?.equipmentType != EquipmentType.Primary)
        {
            reason = "Mugirl.WeaponWheel.InvalidWeapon".Translate();
            return false;
        }

        if (slotIndex < 0 || slotIndex >= slots.Count || !slots[slotIndex].Unlocked)
        {
            reason = "Mugirl.WeaponWheel.SlotLocked".Translate();
            return false;
        }

        if (slots[slotIndex].Weapon != null)
        {
            reason = "Mugirl.WeaponWheel.SlotOccupied".Translate();
            return false;
        }

        if (owner.IsBusy)
        {
            reason = "Mugirl.WeaponWheel.Busy".Translate();
            return false;
        }

        if (ContainsWeapon(weapon))
        {
            reason = "Mugirl.WeaponWheel.AlreadyStored".Translate();
            return false;
        }

        return EquipmentUtility.CanEquip(weapon, pawn, out reason);
    }

    internal bool TryAttachGroundWeapon(ThingWithComps weapon, int slotIndex, out string? reason)
    {
        if (!CanAcceptWeapon(weapon, slotIndex, out reason))
        {
            return false;
        }

        // CanAcceptWeapon 已确认 Pawn 与 equipment 存在。
        Pawn pawn = owner.Pawn!;
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
        slots[slotIndex].Weapon = attachedWeapon;
        try
        {
            using (EquipmentTransferScope.Registration())
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
            owner.MarkBackWeaponsDirty();
            return true;
        }
        finally
        {
            if (!attached)
            {
                slots[slotIndex].Weapon = null;
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
        WeaponWheelHooks.Current.InitializeGeneratedWeapon(owner.Pawn, weapon);
        EnsureCollections();
        Pawn? pawn = owner.Pawn;
        if (pawn?.equipment == null || weapon == null || !weapon.def.IsRangedWeapon || weapon.def.equipmentType != EquipmentType.Primary || ContainsWeapon(weapon))
        {
            return false;
        }

        int slotIndex = WeaponWheelSlotMath.FirstUnlockedEmpty(slots, 1);
        if (slotIndex < 0 || !reserveWeapons.TryAdd(weapon))
        {
            return false;
        }

        slots[slotIndex].Weapon = weapon;
        using (EquipmentTransferScope.Registration())
        {
            pawn.equipment.Notify_EquipmentAdded(weapon);
        }

        owner.MarkBackWeaponsDirty();
        return true;
    }

    internal bool TrySwapSlots(int firstIndex, int secondIndex, out string? reason)
    {
        reason = null;
        EnsureCollections();
        if (firstIndex == secondIndex)
        {
            return true;
        }

        if (firstIndex < 0 || firstIndex >= slots.Count || secondIndex < 0 || secondIndex >= slots.Count || !slots[firstIndex].Unlocked || !slots[secondIndex].Unlocked)
        {
            reason = "Mugirl.WeaponWheel.SlotLocked".Translate();
            return false;
        }

        if (owner.IsBusy)
        {
            reason = "Mugirl.WeaponWheel.Busy".Translate();
            return false;
        }

        (slots[firstIndex].Weapon, slots[secondIndex].Weapon) = (slots[secondIndex].Weapon, slots[firstIndex].Weapon);
        if ((firstIndex == 0 || secondIndex == 0) && !NormalizeToPrimarySlot())
        {
            // 主手无法切到新的 0 号武器时撤销交换并恢复原主手。
            (slots[firstIndex].Weapon, slots[secondIndex].Weapon) = (slots[secondIndex].Weapon, slots[firstIndex].Weapon);
            NormalizeToPrimarySlot();
            reason = "Mugirl.WeaponWheel.CannotSwap".Translate();
            return false;
        }

        owner.MarkBackWeaponsDirty();
        return true;
    }

    internal bool TryDropSlot(int slotIndex, out string? reason)
    {
        reason = null;
        EnsureCollections();
        Pawn? pawn = owner.Pawn;
        if (pawn == null || !pawn.Spawned || slotIndex < 0 || slotIndex >= slots.Count)
        {
            reason = "Mugirl.WeaponWheel.CannotDrop".Translate();
            return false;
        }

        if (owner.IsBusy)
        {
            reason = "Mugirl.WeaponWheel.Busy".Translate();
            return false;
        }

        ThingWithComps? weapon = slots[slotIndex].Weapon;
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

            dropped?.SetForbidden(false, false);
        }
        else
        {
            using (EquipmentTransferScope.Registration())
            {
                pawn.equipment.Notify_EquipmentRemoved(weapon);
            }

            slots[slotIndex].Weapon = null;
            if (!reserveWeapons.TryDrop(weapon, pawn.Position, pawn.Map, ThingPlaceMode.Near, out ThingWithComps dropped))
            {
                slots[slotIndex].Weapon = weapon;
                using (EquipmentTransferScope.Registration())
                {
                    pawn.equipment.Notify_EquipmentAdded(weapon);
                }

                reason = "Mugirl.WeaponWheel.CannotDrop".Translate();
                return false;
            }

            dropped?.SetForbidden(false, false);
        }

        slots[slotIndex].Weapon = null;
        activeSlotIndex = 0;
        NormalizeToPrimarySlot();
        SynchronizeHuntingVerb(weapon);
        owner.MarkBackWeaponsDirty();
        return true;
    }

    private static void ResetBurstingVerbs(ThingWithComps? weapon)
    {
        List<Verb>? verbs = weapon?.GetComp<CompEquippable>()?.AllVerbs;
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
        WeaponWheelHookSet.PrimarySwapScope scope = default;
        try
        {
            if (!WeaponWheelHooks.Current.TryBeginPrimarySwap(owner, slotIndex, out scope))
            {
                return false;
            }

            bool result = MoveActiveWeaponCore(slotIndex);
            scope.Restore();
            scope.NotifySucceeded(result);
            return result;
        }
        finally
        {
            scope.Restore();
        }
    }

    private bool MoveActiveWeaponCore(int slotIndex)
    {
        EnsureCollections();
        Pawn? pawn = owner.Pawn;
        if (pawn?.equipment == null || slotIndex < 0 || slotIndex >= slots.Count)
        {
            return false;
        }

        ThingWithComps? desired = slots[slotIndex].Weapon;
        if (desired == null || !slots[slotIndex].Unlocked)
        {
            return false;
        }

        ThingWithComps? current = pawn.equipment.Primary;
        if (current == desired)
        {
            activeSlotIndex = slotIndex;
            SynchronizeHuntingVerb();
            return true;
        }

        if (!reserveWeapons.Contains(desired))
        {
            return false;
        }

        using (EquipmentTransferScope.InternalTransfer())
        {
            WeaponWheelStorage.ResetBurstingVerbs(current);
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
        SynchronizeHuntingVerb(current);
        owner.MarkBackWeaponsDirty();
        return true;
    }

    internal bool NormalizeToPrimarySlot()
    {
        WeaponWheelHookSet.PrimarySwapScope scope = default;
        try
        {
            if (!WeaponWheelHooks.Current.TryBeginPrimarySwap(owner, 0, out scope))
            {
                return false;
            }

            bool result = NormalizeToPrimarySlotCore();
            scope.Restore();
            scope.NotifySucceeded(result);
            return result;
        }
        finally
        {
            scope.Restore();
        }
    }

    private bool NormalizeToPrimarySlotCore()
    {
        EnsureCollections();
        Pawn? pawn = owner.Pawn;
        if (pawn?.equipment == null)
        {
            return false;
        }

        ThingWithComps? desired = slots[0].Weapon;
        ThingWithComps? current = pawn.equipment.Primary;
        if (current == desired)
        {
            activeSlotIndex = 0;
            SynchronizeHuntingVerb();
            return true;
        }

        if (current != null && !ContainsWeapon(current))
        {
            return desired == null;
        }

        using (EquipmentTransferScope.InternalTransfer())
        {
            if (current != null)
            {
                WeaponWheelStorage.ResetBurstingVerbs(current);
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
        SynchronizeHuntingVerb(current);
        owner.MarkBackWeaponsDirty();
        return true;
    }

    private void SynchronizeHuntingVerb(ThingWithComps? previousWeapon = null)
    {
        Pawn? pawn = owner.Pawn;
        Job? job = pawn?.CurJob;
        if (pawn == null || job?.def != JobDefOf.Hunt || job.targetA.Thing is not Pawn target || target.Dead)
        {
            return;
        }

        ThingWithComps? cachedWeapon = job.verbToUse?.EquipmentSource;
        ThingWithComps? primary = pawn.equipment?.Primary;
        if (cachedWeapon == null || cachedWeapon == primary || (cachedWeapon != previousWeapon && !ContainsWeapon(cachedWeapon)))
        {
            return;
        }

        // Hunt 只在开始时缓存 Verb。旧枪进入储备后不再满足 IsStillUsableBy，
        // 原版会在命中检查与已到达的射击位置之间同步递归。所有权转移结束后再同步。
        Verb? primaryVerb = WeaponWheelCombat.PrimaryVerbFor(primary);
        if (primaryVerb != null && WorkGiver_HunterHunt.HasHuntingWeapon(pawn) && !primaryVerb.verbProps.onlyManualCast && primaryVerb.IsStillUsableBy(pawn))
        {
            job.verbToUse = primaryVerb;
            return;
        }

        // 空槽、近战或不可用武器不能继续当前 Hunt；下一 tick 再由原版安排工作，避免同步重入。
        pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: false);
    }

    internal void NotifyExternalEquipmentAdded(ThingWithComps? equipment)
    {
        if (WeaponWheelHooks.Current.IsForeignEquipment(equipment))
        {
            return;
        }

        if (equipment == null || equipment.def?.equipmentType != EquipmentType.Primary)
        {
            return;
        }

        EnsureCollections();
        owner.combat.AbortCombatAndRestorePrimary(false);
        int existingIndex = IndexOfSlotWeapon(equipment);
        if (existingIndex >= 0)
        {
            slots[existingIndex].Weapon = null;
        }

        ThingWithComps? displaced = slots[0].Weapon;
        if (displaced != null && displaced != equipment)
        {
            int emptyIndex = WeaponWheelSlotMath.FirstUnlockedEmpty(slots, 1);
            if (emptyIndex >= 0)
            {
                slots[emptyIndex].Weapon = displaced;
            }
        }

        slots[0].Weapon = equipment;
        activeSlotIndex = 0;
        owner.MarkBackWeaponsDirty();
    }

    internal void NotifyExternalEquipmentRemoved(ThingWithComps? equipment)
    {
        if (WeaponWheelHooks.Current.IsForeignEquipment(equipment))
        {
            return;
        }

        int index = IndexOfWeapon(equipment);
        if (index < 0)
        {
            return;
        }

        owner.combat.AbortCombatAndRestorePrimary(false);
        slots[index].Weapon = null;
        activeSlotIndex = 0;
        owner.MarkBackWeaponsDirty();
    }

    internal void TrackExternalPrimaryIfNeeded()
    {
        ThingWithComps? primary = owner.Pawn?.equipment?.Primary;
        if (primary == null)
        {
            return;
        }

        int index = IndexOfSlotWeapon(primary);
        if (index >= 0)
        {
            activeSlotIndex = index;
            return;
        }

        NotifyExternalEquipmentAdded(primary);
    }

    internal void TrackWeaponsRemovedFromReserve()
    {
        Pawn? pawn = owner.Pawn;
        ThingWithComps? primary = pawn?.equipment?.Primary;
        for (int i = 0; i < slots.Count; i++)
        {
            ThingWithComps? weapon = slots[i].Weapon;
            if (weapon == null || weapon == primary || reserveWeapons.Contains(weapon))
            {
                continue;
            }

            slots[i].Weapon = null;
            using (EquipmentTransferScope.Registration())
            {
                pawn?.equipment?.Notify_EquipmentRemoved(weapon);
            }

            owner.MarkBackWeaponsDirty();
        }
    }

    internal void HandleDropAndForbidEverything(bool keepInventoryAndEquipmentIfInBed)
    {
        Pawn? pawn = owner.Pawn;
        if (pawn == null || (keepInventoryAndEquipmentIfInBed && pawn.InBed() && !pawn.kindDef.destroyGearOnDrop))
        {
            return;
        }

        owner.combat.AbortCombatAndRestorePrimary(false);
        ThingOwner? destination = null;
        if (pawn.InContainerEnclosed && pawn.ParentHolder != null)
        {
            destination = pawn.ParentHolder.GetDirectlyHeldThings();
        }

        for (int i = reserveWeapons.Count - 1; i >= 0; i--)
        {
            ThingWithComps weapon = reserveWeapons[i];
            int slotIndex = IndexOfSlotWeapon(weapon);
            bool canHandle = pawn.kindDef.destroyGearOnDrop || destination != null || (pawn.SpawnedOrAnyParentSpawned && pawn.MapHeld != null);
            if (!canHandle)
            {
                continue;
            }

            using (EquipmentTransferScope.Registration())
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
                    slots[slotIndex].Weapon = null;
                }
            }
            else
            {
                using (EquipmentTransferScope.Registration())
                {
                    pawn.equipment?.Notify_EquipmentAdded(weapon);
                }
            }
        }

        owner.MarkBackWeaponsDirty();
    }
}
