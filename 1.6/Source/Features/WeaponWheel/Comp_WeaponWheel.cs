using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl.Features.WeaponWheel
{
    public sealed partial class Comp_WeaponWheel : ThingComp, IThingHolder
    {
        private ThingOwner<ThingWithComps> reserveWeapons;
        private List<ThingWithComps> slots;
        private List<bool> unlockedSlots;

        private int activeSlotIndex;
        private bool normalizeAfterLoad;
        private bool wasWeaponOpenlyHeld;
        private bool wasMountedDisabled;

        private WeaponWheelCombatState combatState = WeaponWheelCombatState.Holstered;
        private int stateStartTick = -1;
        private int stateDurationTicks;
        private int cooldownUntilTick = -1;
        private int accumulatedCooldownTicks;
        private bool cycleActive;
        private LocalTargetInfo cycleTarget = LocalTargetInfo.Invalid;
        private Job combatSourceJob;
        private ThingWithComps animationOutgoingWeapon;
        private ThingWithComps animationIncomingWeapon;
        private float animationAimAngle;
        private bool animationSnapSoundPlayed;
        private ThingWithComps aimHandoffWeapon;
        private float aimHandoffAngle;
        private int aimHandoffUntilTick = -1;

        private LocalTargetInfo pendingCastTarget = LocalTargetInfo.Invalid;
        private LocalTargetInfo pendingCastDestination = LocalTargetInfo.Invalid;
        private bool pendingSurpriseAttack;
        private bool pendingCanHitNonTargetPawns = true;
        private bool pendingPreventFriendlyFire;
        private bool pendingNonInterruptingSelfCast;

        private readonly List<PawnRenderNode_BackWeapon> backWeaponNodes = new List<PawnRenderNode_BackWeapon>();

        public CompProperties_WeaponWheel Props => (CompProperties_WeaponWheel)props;
        public Pawn Pawn => parent as Pawn;
        public int MaxSlots => Mathf.Max(1, Props?.maxSlots ?? 6);
        public int ActiveSlotIndex => activeSlotIndex;
        public bool IsBusy => combatState == WeaponWheelCombatState.EquippingPrimary
            || combatState == WeaponWheelCombatState.Switching
            || combatState == WeaponWheelCombatState.CycleCooldown
            || combatState == WeaponWheelCombatState.Firing;
        internal WeaponWheelCombatState CombatState => combatState;
        internal ThingOwner<ThingWithComps> ReserveWeapons => reserveWeapons;

        IThingHolder IThingHolder.ParentHolder => Pawn;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            EnsureCollections();
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureCollections();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref reserveWeapons, "weaponWheelReserveWeapons", this);
            Scribe_Collections.Look(ref slots, "weaponWheelSlots", LookMode.Reference);
            Scribe_Collections.Look(ref unlockedSlots, "weaponWheelUnlockedSlots", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
                ValidateSlotReferences();
                normalizeAfterLoad = true;
                ResetRuntimeState();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            EnsureCollections();

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return;
            }

            if (normalizeAfterLoad)
            {
                normalizeAfterLoad = false;
                NormalizeToPrimarySlot();
            }

            TrackWeaponsRemovedFromReserve();
            TrackExternalPrimaryIfNeeded();
            TickReserveVerbs();
            TickMountedState();
            TickWeaponOpenState();
            TickCombatState();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            reserveWeapons?.ClearAndDestroyContents(mode);
            slots?.Clear();
            backWeaponNodes.Clear();
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureCollections();
            return reserveWeapons;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingWithComps WeaponAt(int index)
        {
            EnsureCollections();
            return index >= 0 && index < slots.Count ? slots[index] : null;
        }

        public bool IsSlotUnlocked(int index)
        {
            EnsureCollections();
            return index >= 0 && index < unlockedSlots.Count && unlockedSlots[index];
        }

        public bool UnlockSlotDeveloper(int index)
        {
            EnsureCollections();
            if (!Prefs.DevMode || index < 0 || index >= MaxSlots || unlockedSlots[index])
            {
                return false;
            }

            unlockedSlots[index] = true;
            MarkBackWeaponsDirty();
            return true;
        }

        public int OccupiedUnlockedSlotCount
        {
            get
            {
                EnsureCollections();
                int count = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (unlockedSlots[i] && slots[i] != null)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public float ReserveWeaponMass
        {
            get
            {
                EnsureCollections();
                float mass = 0f;
                for (int i = 0; i < reserveWeapons.Count; i++)
                {
                    ThingWithComps weapon = reserveWeapons[i];
                    mass += weapon.GetStatValue(StatDefOf.Mass, true, 1) * weapon.stackCount;
                }
                return mass;
            }
        }

        internal bool ContainsWeapon(ThingWithComps weapon)
        {
            return weapon != null && IndexOfWeapon(weapon) >= 0;
        }

        internal int IndexOfWeapon(ThingWithComps weapon)
        {
            EnsureCollections();
            return weapon == null ? -1 : slots.IndexOf(weapon);
        }

        private void EnsureCollections()
        {
            if (reserveWeapons == null)
            {
                reserveWeapons = new ThingOwner<ThingWithComps>(this);
            }

            int slotCount = MaxSlots;
            if (slots == null)
            {
                slots = new List<ThingWithComps>(slotCount);
            }
            while (slots.Count < slotCount)
            {
                slots.Add(null);
            }
            if (slots.Count > slotCount)
            {
                slots.RemoveRange(slotCount, slots.Count - slotCount);
            }

            if (unlockedSlots == null)
            {
                unlockedSlots = new List<bool>(slotCount);
            }
            int baseUnlocked = Mathf.Clamp(Props?.baseUnlockedSlots ?? 3, 0, slotCount);
            int unlockedCount = baseUnlocked;
            bool researchFinished = MugirlGameUtility.IsResearchFinished(Mugirl_DefOf.Mugirl_Autoloading);
            if (researchFinished)
            {
                unlockedCount = slotCount;
            }
            while (unlockedSlots.Count < slotCount)
            {
                unlockedSlots.Add(unlockedSlots.Count < unlockedCount);
            }
            if (unlockedSlots.Count > slotCount)
            {
                unlockedSlots.RemoveRange(slotCount, unlockedSlots.Count - slotCount);
            }
            for (int i = 0; i < unlockedCount; i++)
            {
                unlockedSlots[i] = true;
            }
        }

        private void ValidateSlotReferences()
        {
            Pawn pawn = Pawn;
            ThingWithComps primary = pawn?.equipment?.Primary;
            HashSet<ThingWithComps> seen = new HashSet<ThingWithComps>();

            for (int i = 0; i < slots.Count; i++)
            {
                ThingWithComps weapon = slots[i];
                if (weapon == null)
                {
                    continue;
                }
                if (weapon.Destroyed || (!reserveWeapons.Contains(weapon) && weapon != primary) || !seen.Add(weapon))
                {
                    slots[i] = null;
                }
            }

            activeSlotIndex = primary == null ? 0 : Mathf.Max(0, slots.IndexOf(primary));

            if (pawn != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    CompEquippable equippable = slots[i]?.GetComp<CompEquippable>();
                    List<Verb> verbs = equippable?.AllVerbs;
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
        }

        private void ResetRuntimeState()
        {
            combatState = WeaponWheelCombatState.Holstered;
            stateStartTick = -1;
            stateDurationTicks = 0;
            cooldownUntilTick = -1;
            accumulatedCooldownTicks = 0;
            cycleActive = false;
            cycleTarget = LocalTargetInfo.Invalid;
            combatSourceJob = null;
            animationOutgoingWeapon = null;
            animationIncomingWeapon = null;
            animationSnapSoundPlayed = false;
            aimHandoffWeapon = null;
            aimHandoffUntilTick = -1;
            ClearPendingCast();
            wasWeaponOpenlyHeld = false;
            wasMountedDisabled = false;
        }

        private void TickReserveVerbs()
        {
            if (reserveWeapons == null)
            {
                return;
            }

            for (int i = 0; i < reserveWeapons.Count; i++)
            {
                ThingWithComps weapon = reserveWeapons[i];
                if (weapon?.def?.tickerType == TickerType.Normal)
                {
                    continue;
                }
                CompEquippable equippable = weapon?.GetComp<CompEquippable>();
                equippable?.verbTracker?.VerbsTick();
            }
        }

        internal void RegisterBackWeaponNode(PawnRenderNode_BackWeapon node)
        {
            if (node != null && !backWeaponNodes.Contains(node))
            {
                backWeaponNodes.Add(node);
            }
        }

        internal void MarkBackWeaponsDirty()
        {
            for (int i = backWeaponNodes.Count - 1; i >= 0; i--)
            {
                PawnRenderNode_BackWeapon node = backWeaponNodes[i];
                if (node == null)
                {
                    backWeaponNodes.RemoveAt(i);
                }
                else
                {
                    node.requestRecache = true;
                }
            }
        }
    }
}
