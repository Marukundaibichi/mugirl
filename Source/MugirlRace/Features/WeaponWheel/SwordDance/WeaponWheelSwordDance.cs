using Mugirl.Features.Mounting;
using Verse.AI;
using Verse.Sound;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.WeaponWheel;
internal sealed class WeaponWheelSwordDance
{
    private readonly CompWeaponWheel owner;
    internal WeaponWheelSwordDance(CompWeaponWheel owner)
    {
        this.owner = owner;
    }

    // StaticCacheLifecycle: 进程级计数，只在主线程的冲刺开始/结束时增减。组件在冲刺中途被 GC 而未清理时只会偏大，
    // 结果是 DrawPos 补丁多做一次组件查找，不影响正确性；不随存档重置。
    private static int activeSwordDanceDashCount;
    internal static bool AnySwordDanceDashActive => Volatile.Read(ref WeaponWheelSwordDance.activeSwordDanceDashCount) > 0;

    private static void AddActiveSwordDanceDash(int delta)
    {
        Interlocked.Add(ref WeaponWheelSwordDance.activeSwordDanceDashCount, delta);
    }

    internal const int SwordDanceGuaranteeFailures = SwordDanceRules.GuaranteeFailures;
    private Verb? pendingSwordDanceVerb;
    private Pawn? pendingSwordDanceTarget;
    private int pendingSwordDanceNextSlot = -1;
    private bool pendingSwordDanceStrikeResolved;
    private bool pendingSwordDanceStrikeDelayed;
    private bool resolvingDelayedSwordDanceStrike;
    internal int swordDanceFailureStreak;
    // 冲刺动画（运行期，不存档）。swordDanceDashOrigin 是绘制起点（取补间位置以免画面跳动），
    // 方向与命中进度只由逻辑位置决定，保证结算 tick 与帧率、画面插值无关。
    private Vector3 swordDanceDashOrigin;
    private Vector3 swordDanceDashDestination;
    private Vector3 swordDanceDashDirection;
    private Rot4 swordDanceDashForwardRotation = Rot4.Invalid;
    private Rot4 swordDanceDashReturnRotation = Rot4.Invalid;
    private int swordDanceDashStartTick = -1;
    private int swordDanceDashDurationTicks;
    private float swordDanceDashImpactProgress = 0.5f;
    // 剑舞复用的轮转缓冲，只在主线程的换手中使用。
    private readonly List<int> swordDanceOccupiedIndices = [];
    private readonly List<ThingWithComps?> swordDanceRotatedWeapons = [];
    internal bool IsDashHoldingWeapon => pendingSwordDanceStrikeDelayed && !pendingSwordDanceStrikeResolved && swordDanceDashStartTick >= 0;

    // 没有冲刺动画，也没有待结算的剑舞：TickSwordDanceDashAnimation 与 TickPendingSwordDanceCompletion 都无事可做。
    internal bool IsIdle => swordDanceDashStartTick < 0 && pendingSwordDanceVerb == null;

    internal bool IsSwordDanceEligible()
    {
        owner.storage.EnsureCollections();
        Pawn? pawn = owner.Pawn;
        if (pawn?.equipment == null || owner.combat.IsCombatDisabledByMount() || pawn.WorkTagIsDisabled(WorkTags.Violent))
        {
            return false;
        }

        if (owner.storage.slots[0].Weapon == null)
        {
            return false;
        }

        int weaponCount = 0;
        for (int i = 0; i < owner.storage.slots.Count; i++)
        {
            ThingWithComps? weapon = owner.storage.slots[i].Weapon;
            if (!owner.storage.slots[i].Unlocked || weapon == null)
            {
                continue;
            }

            weaponCount++;
            if (!weapon.def.IsMeleeWeapon || WeaponWheelCombat.PrimaryVerbFor(weapon)?.verbProps?.IsMeleeAttack != true)
            {
                return false;
            }
        }

        return weaponCount >= 2;
    }

    internal bool TryDelaySwordDanceStrikeUntilImpact(Verb verb)
    {
        if (resolvingDelayedSwordDanceStrike)
        {
            return false;
        }

        if (IsSwordDanceStrikeAwaitingImpact(verb))
        {
            return true;
        }

        ClearPendingSwordDance();
        if (verb.verbProps?.IsMeleeAttack != true || !IsSwordDanceAttack(verb) || !IsSwordDanceEligible())
        {
            return false;
        }

        Pawn? pawn = owner.Pawn;
        Pawn? target = verb.CurrentTarget.Pawn;
        int nextSlot = FindNextMeleeSlot(owner.storage.activeSlotIndex);
        if (pawn == null || target == null || target == pawn || target.Dead || target.Destroyed || pawn.stances?.FullBodyBusy == true || !target.Spawned || target.Map != pawn.Map || !pawn.HostileTo(target) || nextSlot < 0 || !WeaponWheelSwordDance.TryFindSwordDanceDestination(pawn, target, out IntVec3 destination) || !RollSwordDanceChance())
        {
            return false;
        }

        pendingSwordDanceVerb = verb;
        pendingSwordDanceTarget = target;
        pendingSwordDanceNextSlot = nextSlot;
        pendingSwordDanceStrikeResolved = false;
        pendingSwordDanceStrikeDelayed = true;
        Map map = pawn.Map;
        IntVec3 origin = pawn.Position;
        bool dashStarted = false;
        if (origin != destination)
        {
            FleckMaker.ThrowDustPuff(origin, map, 1.15f);
            BeginSwordDanceDashAnimation(pawn, destination, target.Position.ToVector3Shifted().ToNumerics());
            pawn.Position = destination;
            // 逻辑位置立即完成换位，绘制位置与朝向由剑舞自己的分段冲刺动画接管。
            pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: false);
            dashStarted = true;
        }

        if (!dashStarted)
        {
            pawn.rotationTracker?.Face(target.DrawPos);
        }

        target.stances?.stunner?.StunFor(Math.Max(1, owner.Props.swordDanceStunTicks), pawn, addBattleLog: false);
        ApplySwordDanceHaste(pawn);
        return dashStarted;
    }

    internal bool IsSwordDanceStrikeAwaitingImpact(Verb? verb)
    {
        return pendingSwordDanceStrikeDelayed && !pendingSwordDanceStrikeResolved && pendingSwordDanceVerb == verb;
    }

    private void BeginSwordDanceDashAnimation(Pawn pawn, IntVec3 destination, Vector3 targetPosition)
    {
        // 逻辑起点：已有冲刺时取其按 tick 计算的当前根位置，否则取所在格中心；两者都与画面无关。
        Vector3 logicalOrigin = swordDanceDashStartTick >= 0 ? CurrentSwordDanceDashRootPosition() : pawn.Position.ToVector3Shifted().ToNumerics();
        logicalOrigin.Y = 0f;
        Vector3 visualOrigin = CurrentSwordDanceDashRootPosition();
        visualOrigin.Y = 0f;
        Vector3 end = destination.ToVector3Shifted().ToNumerics();
        end.Y = 0f;
        Vector3 direction = SwordDanceDashCurve.Direction(logicalOrigin, end);
        if (swordDanceDashStartTick < 0)
        {
            WeaponWheelSwordDance.AddActiveSwordDanceDash(1);
        }

        swordDanceDashOrigin = visualOrigin;
        swordDanceDashDestination = end;
        swordDanceDashDirection = direction;
        swordDanceDashForwardRotation = Rot4.FromAngleFlat(UnityVector.AngleFlat(direction));
        swordDanceDashReturnRotation = swordDanceDashForwardRotation.Opposite;
        swordDanceDashStartTick = WeaponWheelCombat.CurrentTick;
        swordDanceDashDurationTicks = Math.Max(1, owner.Props.swordDanceDashAnimationTicks);
        swordDanceDashImpactProgress = SwordDanceDashCurve.ImpactProgress(logicalOrigin, end, direction, targetPosition);
        pawn.Rotation = swordDanceDashForwardRotation;
    }

    internal bool TryGetSwordDanceDashDrawPosition(UVector3 vanillaDrawPosition, out UVector3 drawPosition)
    {
        drawPosition = vanillaDrawPosition;
        PawnTweener? tweener = owner.Pawn?.Drawer?.tweener;
        if (swordDanceDashStartTick < 0 || tweener == null)
        {
            return false;
        }

        Vector3 dashRoot = CurrentSwordDanceDashRootPosition();
        Vector3 vanillaRoot = tweener.TweenedPos.ToNumerics();
        drawPosition = (dashRoot + (vanillaDrawPosition.ToNumerics() - vanillaRoot)).ToUnity();
        return true;
    }

    private Vector3 CurrentSwordDanceDashRootPosition()
    {
        if (swordDanceDashStartTick < 0)
        {
            Pawn? pawn = owner.Pawn;
            if (pawn?.Drawer?.tweener is { } tweener)
            {
                return tweener.TweenedPos.ToNumerics();
            }

            return pawn?.Position.ToVector3Shifted().ToNumerics() ?? Vector3.Zero;
        }

        float progress = SwordDanceDashCurve.Progress(WeaponWheelCombat.CurrentTick - swordDanceDashStartTick, swordDanceDashDurationTicks);
        return SwordDanceDashCurve.RootPosition(swordDanceDashOrigin, swordDanceDashDestination, swordDanceDashDirection, progress);
    }

    internal bool MaintainSwordDanceDashFacing()
    {
        Pawn? pawn = owner.Pawn;
        if (swordDanceDashStartTick < 0 || pawn == null)
        {
            return false;
        }

        float progress = SwordDanceDashCurve.Progress(WeaponWheelCombat.CurrentTick - swordDanceDashStartTick, swordDanceDashDurationTicks);
        pawn.Rotation = SwordDanceDashCurve.FacesForward(progress) ? swordDanceDashForwardRotation : swordDanceDashReturnRotation;
        return true;
    }

    internal float SwordDanceDashAimAngle(float fallbackAngle)
    {
        if (swordDanceDashStartTick < 0 || UnityVector.SqrMagnitude(swordDanceDashDirection) <= SwordDanceDashCurve.MinDirectionSqrMagnitude)
        {
            return fallbackAngle;
        }

        float progress = SwordDanceDashCurve.Progress(WeaponWheelCombat.CurrentTick - swordDanceDashStartTick, swordDanceDashDurationTicks);
        Vector3 facingDirection = SwordDanceDashCurve.FacesForward(progress) ? swordDanceDashDirection : -swordDanceDashDirection;
        return UnityVector.AngleFlat(facingDirection);
    }

    internal bool TryGetSwordDanceDashHeldWeapon([NotNullWhen(true)] out ThingWithComps? weapon, out float aimAngle)
    {
        weapon = owner.Pawn?.equipment?.Primary;
        aimAngle = SwordDanceDashAimAngle(0f);
        return weapon != null && IsDashHoldingWeapon;
    }

    internal void TickSwordDanceDashAnimation(int currentTick)
    {
        if (swordDanceDashStartTick < 0)
        {
            return;
        }

        int duration = Math.Max(1, swordDanceDashDurationTicks);
        int elapsed = currentTick - swordDanceDashStartTick;
        float progress = SwordDanceDashCurve.Progress(elapsed, duration);
        if (pendingSwordDanceStrikeDelayed && !pendingSwordDanceStrikeResolved && progress >= swordDanceDashImpactProgress)
        {
            ResolveDelayedSwordDanceStrike();
        }

        if (elapsed < duration)
        {
            return;
        }

        if (pendingSwordDanceStrikeDelayed && !pendingSwordDanceStrikeResolved)
        {
            ResolveDelayedSwordDanceStrike();
        }

        Pawn? pawn = owner.Pawn;
        if (pawn?.Spawned == true)
        {
            FleckMaker.ThrowDustPuff(swordDanceDashDestination.ToUnity(), pawn.Map, 1.35f);
            pawn.Drawer?.tweener?.ResetTweenedPosToRoot();
            pawn.Rotation = swordDanceDashReturnRotation;
        }

        ClearSwordDanceDashAnimation();
    }

    private void ResolveDelayedSwordDanceStrike()
    {
        Verb? verb = pendingSwordDanceVerb;
        if (verb == null || resolvingDelayedSwordDanceStrike)
        {
            return;
        }

        resolvingDelayedSwordDanceStrike = true;
        bool invoked;
        try
        {
            invoked = SwordDanceDelayedStrike.TryInvoke(verb);
        }
        finally
        {
            resolvingDelayedSwordDanceStrike = false;
            MaintainSwordDanceDashFacing();
        }

        if (!invoked)
        {
            verb.Reset();
            ClearPendingSwordDance();
        }
    }

    internal void ClearSwordDanceDashAnimation()
    {
        if (swordDanceDashStartTick >= 0)
        {
            WeaponWheelSwordDance.AddActiveSwordDanceDash(-1);
        }

        swordDanceDashOrigin = Vector3.Zero;
        swordDanceDashDestination = Vector3.Zero;
        swordDanceDashDirection = Vector3.Zero;
        swordDanceDashForwardRotation = Rot4.Invalid;
        swordDanceDashReturnRotation = Rot4.Invalid;
        swordDanceDashStartTick = -1;
        swordDanceDashDurationTicks = 0;
        swordDanceDashImpactProgress = 0.5f;
    }

    private bool RollSwordDanceChance()
    {
        return SwordDanceRules.ResolveChance(Rand.Chance(UnityMathf.Clamp01(owner.Props.swordDanceChance)), ref swordDanceFailureStreak);
    }

    internal void NotifySwordDanceStrikeResolved(Verb? verb)
    {
        if (pendingSwordDanceVerb == verb && pendingSwordDanceNextSlot >= 0)
        {
            pendingSwordDanceStrikeResolved = true;
        }
    }

    internal bool NotifyMeleeAttackCompleted(Verb verb)
    {
        if (!IsSwordDanceAttack(verb))
        {
            return false;
        }

        bool triggered = pendingSwordDanceVerb == verb && pendingSwordDanceNextSlot >= 0 && pendingSwordDanceStrikeResolved;
        Pawn? target = pendingSwordDanceTarget;
        int nextSlot = pendingSwordDanceNextSlot;
        ClearPendingSwordDance();
        owner.combat.combatSourceJob = null;
        if (!triggered)
        {
            return false;
        }

        ApplySwordDanceRamDamage(target, verb);
        BeginSwordDanceSwitchTo(nextSlot, target, verb);
        return true;
    }

    internal void TickPendingSwordDanceCompletion()
    {
        if (pendingSwordDanceStrikeResolved && pendingSwordDanceVerb != null)
        {
            NotifyMeleeAttackCompleted(pendingSwordDanceVerb);
        }
    }

    private bool IsSwordDanceAttack(Verb? verb)
    {
        Pawn? pawn = owner.Pawn;
        ThingWithComps? primary = pawn?.equipment?.Primary;
        ThingWithComps? attackEquipment = verb?.EquipmentSource;
        return verb?.verbProps?.IsMeleeAttack == true && verb.CasterPawn == pawn && primary?.def?.IsMeleeWeapon == true && owner.storage.ContainsWeapon(primary) // 角、拳头等身体部位攻击没有装备来源；装着轮盘近战武器时同样算作剑舞攻击。
 && (attackEquipment == null || attackEquipment == primary);
    }

    internal bool ShouldSuppressExternalMeleeAttackAnimation()
    {
        return pendingSwordDanceVerb != null && pendingSwordDanceNextSlot >= 0 && !pendingSwordDanceStrikeResolved;
    }

    private int FindNextMeleeSlot(int currentSlot)
    {
        return WeaponWheelSlotMath.FindNextCyclic(owner.storage.slots, currentSlot, WeaponWheelSwordDance.IsMeleeWheelWeapon);
    }

    private static bool IsMeleeWheelWeapon(ThingWithComps weapon)
    {
        return weapon.def?.IsMeleeWeapon == true && WeaponWheelCombat.PrimaryVerbFor(weapon)?.verbProps?.IsMeleeAttack == true;
    }

    private static bool TryFindSwordDanceDestination(Pawn pawn, Pawn target, out IntVec3 destination)
    {
        destination = IntVec3.Invalid;
        Map? map = pawn.Map;
        if (map == null)
        {
            return false;
        }

        IntVec3 behindDirection = SwordDanceRules.BehindDirection(pawn.Position, target.Position);
        if (behindDirection == IntVec3.Zero)
        {
            return false;
        }

        IntVec3 exactBehind = target.Position + behindDirection;
        if (WeaponWheelSwordDance.IsValidSwordDanceDestination(exactBehind, pawn, map))
        {
            destination = exactBehind;
            return true;
        }

        int bestScore = int.MinValue;
        foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(target))
        {
            int? score = SwordDanceRules.ScoreBehindCell(target.Position, behindDirection, cell);
            if (score is not int value || !WeaponWheelSwordDance.IsValidSwordDanceDestination(cell, pawn, map))
            {
                continue;
            }

            if (value > bestScore)
            {
                bestScore = value;
                destination = cell;
            }
        }

        return destination.IsValid;
    }

    private static bool IsValidSwordDanceDestination(IntVec3 cell, Pawn pawn, Map map)
    {
        if (!cell.InBounds(map) || !cell.Standable(map))
        {
            return false;
        }

        Pawn? occupyingPawn = cell.GetFirstPawn(map);
        if (occupyingPawn != null && occupyingPawn != pawn)
        {
            return false;
        }

        return cell == pawn.Position || GenPlace.HaulPlaceBlockerIn(pawn, cell, map, checkBlueprintsAndFrames: false) == null;
    }

    private void ApplySwordDanceRamDamage(Pawn? target, Verb meleeVerb)
    {
        Pawn? pawn = owner.Pawn;
        if (pawn == null || target == null || target.Destroyed || !target.Spawned || target.Map != pawn.Map)
        {
            return;
        }

        ThingWithComps? weapon = meleeVerb.EquipmentSource ?? pawn.equipment?.Primary;
        DamageInfo damage = new(DamageDefOf.Blunt, UnityMathf.Max(0f, owner.Props.swordDanceRamDamage), UnityMathf.Max(0f, owner.Props.swordDanceRamArmorPenetration), (target.Position - pawn.Position).AngleFlat, pawn, null, weapon?.def, DamageInfo.SourceCategory.ThingOrUnknown, target);
        target.TakeDamage(damage);
    }

    private void ApplySwordDanceHaste(Pawn pawn)
    {
        HediffDef hasteDef = WeaponWheelDefOf.Mugirl_LinkedSwordDance;
        if (pawn.health == null || hasteDef == null)
        {
            return;
        }

        Hediff? haste = pawn.health.hediffSet.GetFirstHediffOfDef(hasteDef) ?? pawn.health.AddHediff(hasteDef);
        haste?.TryGetComp<HediffComp_Disappears>()?.SetDuration(Math.Max(1, owner.Props.swordDanceHasteTicks));
    }

    private void BeginSwordDanceSwitchTo(int slotIndex, LocalTargetInfo target, Verb completedVerb)
    {
        owner.combat.ClearAimHandoff();
        ThingWithComps? outgoing = owner.Pawn?.equipment?.Primary;
        ThingWithComps? incoming = owner.storage.WeaponAt(slotIndex);
        if (incoming == null || !PromoteSlotToPrimary(slotIndex))
        {
            owner.combat.combatState = owner.combat.RestingState;
            return;
        }

        owner.combat.EnterSwitchAnimation(WeaponWheelCombatState.SwordDanceSwitching, outgoing, incoming, target, completedVerb);
    }

    // 把 slotIndex 的武器换到主手，并按循环顺序轮转其余已占用槽位，使下一把近战武器总在下一个位置。
    private bool PromoteSlotToPrimary(int slotIndex)
    {
        owner.storage.EnsureCollections();
        if (slotIndex <= 0 || slotIndex >= owner.storage.slots.Count || owner.storage.slots[slotIndex].Weapon == null)
        {
            return false;
        }

        if (!WeaponWheelSlotMath.TryRotateToFront(owner.storage.slots, slotIndex, swordDanceOccupiedIndices, swordDanceRotatedWeapons))
        {
            return false;
        }

        if (!owner.storage.MoveActiveWeaponTo(slotIndex))
        {
            return false;
        }

        for (int i = 0; i < swordDanceOccupiedIndices.Count; i++)
        {
            owner.storage.slots[swordDanceOccupiedIndices[i]].Weapon = swordDanceRotatedWeapons[i];
        }

        swordDanceOccupiedIndices.Clear();
        swordDanceRotatedWeapons.Clear();
        owner.storage.activeSlotIndex = 0;
        owner.MarkBackWeaponsDirty();
        return owner.Pawn?.equipment?.Primary == owner.storage.slots[0].Weapon;
    }

    internal void FinishSwordDanceSwitchAnimation()
    {
        owner.combat.EndSwitchAnimation();
        owner.combat.combatState = owner.combat.RestingState;
        owner.MarkBackWeaponsDirty();
    }

    internal void ClearPendingSwordDance()
    {
        pendingSwordDanceVerb = null;
        pendingSwordDanceTarget = null;
        pendingSwordDanceNextSlot = -1;
        pendingSwordDanceStrikeResolved = false;
        pendingSwordDanceStrikeDelayed = false;
    }
}
