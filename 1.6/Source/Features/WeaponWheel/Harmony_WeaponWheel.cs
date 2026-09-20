using System;
using System.Collections.Generic;
using HarmonyLib;
using Mugirl.Features.Lances;
using Mugirl.Features.WeaponWheel;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class WeaponWheelHarmonyUtility
    {
        internal static Comp_WeaponWheel CompFor(Pawn pawn)
        {
            return MugirlIdentity.IsMugirlPawn(pawn)
                ? pawn.TryGetComp<Comp_WeaponWheel>()
                : null;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawPos), MethodType.Getter)]
    public static class Harmony_WeaponWheel_SwordDanceDashDrawPosition
    {
        public static void Postfix(Pawn __instance, ref Vector3 __result)
        {
            Comp_WeaponWheel comp = WeaponWheelHarmonyUtility.CompFor(__instance);
            if (comp != null && comp.TryGetSwordDanceDashDrawPosition(__result, out Vector3 dashPosition))
            {
                __result = dashPosition;
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Harmony_WeaponWheel_EnemyLoadout
    {
        public static void Postfix(PawnGenerationRequest request, Pawn __result)
        {
            WeaponWheelEnemyLoadoutGenerator.TryGenerateFor(__result, request);
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded))]
    public static class Harmony_WeaponWheel_EquipmentAdded
    {
        public static bool Prefix()
        {
            return !WeaponWheelTransferScope.IsInternalTransfer;
        }

        public static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
        {
            if (!WeaponWheelTransferScope.IsWheelOperation)
            {
                WeaponWheelHarmonyUtility.CompFor(__instance?.pawn)?.NotifyExternalEquipmentAdded(eq);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved))]
    public static class Harmony_WeaponWheel_EquipmentRemoved
    {
        public static bool Prefix()
        {
            return !WeaponWheelTransferScope.IsInternalTransfer;
        }

        public static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
        {
            if (!WeaponWheelTransferScope.IsWheelOperation)
            {
                WeaponWheelHarmonyUtility.CompFor(__instance?.pawn)?.NotifyExternalEquipmentRemoved(eq);
            }
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Harmony_WeaponWheel_BurstCompleted
    {
        public static bool Prefix(Verb __instance)
        {
            Comp_WeaponWheel comp = WeaponWheelHarmonyUtility.CompFor(__instance?.CasterPawn);
            return comp?.TryDelaySwordDanceStrikeUntilImpact(__instance) != true;
        }

        public static void Postfix(Verb __instance)
        {
            if (__instance?.verbProps?.IsMeleeAttack == true)
            {
                Comp_WeaponWheel comp = WeaponWheelHarmonyUtility.CompFor(__instance.CasterPawn);
                if (comp?.IsSwordDanceStrikeAwaitingImpact(__instance) != true)
                {
                    comp?.NotifyMeleeAttackCompleted(__instance);
                }
                return;
            }
            WeaponWheelDevLog.BurstShotPostfix(__instance);
            if (__instance == null || __instance.state != VerbState.Idle
                || !MugirlTickUtility.TryGetCurrentGameTick(out int currentTick)
                || __instance.LastShotTick != currentTick)
            {
                return;
            }
            WeaponWheelHarmonyUtility.CompFor(__instance.CasterPawn)?.NotifyBurstCompleted(__instance);
        }

        internal static bool TryInvokeDelayedMeleeBurst(Verb verb)
        {
            try
            {
                System.Reflection.MethodInfo method = AccessTools.Method(typeof(Verb), "TryCastNextBurstShot");
                if (method == null)
                {
                    MugirlLog.WarningOnce(
                        "WeaponWheel.DelayedSwordDanceBurstMissing",
                        "Unable to resolve Verb.TryCastNextBurstShot for delayed sword-dance impact.");
                    return false;
                }

                method.Invoke(verb, null);
                return true;
            }
            catch (Exception exception)
            {
                MugirlLog.WarningOnce(
                    "WeaponWheel.DelayedSwordDanceBurstFailed",
                    "Delayed sword-dance impact failed: " + exception.GetBaseException().Message);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class Harmony_WeaponWheel_MeleeAttack
    {
        public static void Postfix(Verb_MeleeAttack __instance)
        {
            WeaponWheelHarmonyUtility.CompFor(__instance?.CasterPawn)?.NotifySwordDanceStrikeResolved(__instance);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new Type[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class Harmony_WeaponWheel_TryStartCastOn
    {
        public static bool Prefix(
            Verb __instance,
            LocalTargetInfo castTarg,
            LocalTargetInfo destTarg,
            bool surpriseAttack,
            bool canHitNonTargetPawns,
            bool preventFriendlyFire,
            bool nonInterruptingSelfCast,
            ref bool __result)
        {
            Comp_WeaponWheel comp = WeaponWheelHarmonyUtility.CompFor(__instance?.CasterPawn);
            if (comp == null || !comp.HandleBeforeTryStartCast(
                __instance,
                castTarg,
                destTarg,
                surpriseAttack,
                canHitNonTargetPawns,
                preventFriendlyFire,
                nonInterruptingSelfCast,
                out bool handledResult))
            {
                return true;
            }

            __result = handledResult;
            return false;
        }

        public static void Postfix(Verb __instance, bool __result)
        {
            WeaponWheelHarmonyUtility.CompFor(__instance?.CasterPawn)?.NotifyCastStarted(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryGetAttackVerb), new Type[] { typeof(Thing), typeof(bool), typeof(bool) })]
    public static class Harmony_WeaponWheel_TryGetAttackVerb_Diagnostics
    {
        // Harmony Postfix 以低优先级先执行；int.MaxValue 确保本防线尽可能最后运行，
        // 在其他框架完成 Verb 替换之后再校验最终结果。
        [HarmonyPriority(int.MaxValue)]
        public static void Postfix(
            Pawn __instance,
            Thing target,
            bool allowManualCastWeapons,
            bool allowTurrets,
            ref Verb __result)
        {
            Verb selectedResult = __result;
            bool corrected = false;
            Comp_WeaponWheel comp = WeaponWheelHarmonyUtility.CompFor(__instance);
            if (comp != null && comp.TryCorrectAttackVerbSelection(
                selectedResult,
                allowManualCastWeapons,
                out Verb correctedResult))
            {
                __result = correctedResult;
                corrected = true;
            }

            WeaponWheelDevLog.AttackVerbSelected(
                __instance,
                target,
                allowManualCastWeapons,
                allowTurrets,
                selectedResult,
                __result,
                corrected);
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.WarmupTime), MethodType.Getter)]
    public static class Harmony_WeaponWheel_WarmupTime
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            if (WeaponWheelWarmupScope.IsSkipping(__instance))
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Harmony_WeaponWheel_DrawEquipmentAiming
    {
        public static bool Prefix(Thing eq)
        {
            ThingWithComps weapon = eq as ThingWithComps;
            Pawn_EquipmentTracker tracker = weapon?.ParentHolder as Pawn_EquipmentTracker;
            Pawn pawn = tracker?.pawn;
            if (pawn?.ParentHolder is PawnFlyer_LanceCharge)
            {
                return false;
            }

            return WeaponWheelHarmonyUtility.CompFor(pawn)?.ShouldSuppressVanillaWeaponDraw(weapon) != true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt))]
    public static class Harmony_WeaponWheel_AnimationDraw
    {
        public static void Postfix(Pawn __instance, DrawPhase phase)
        {
            if (MugirlIdentity.IsMugirlPawn(__instance))
            {
                WeaponWheelAnimationRenderer.Draw(__instance, phase);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_RotationTracker), nameof(Pawn_RotationTracker.UpdateRotation))]
    public static class Harmony_WeaponWheel_MaintainCombatFacing
    {
        public static void Postfix(Pawn ___pawn)
        {
            WeaponWheelHarmonyUtility.CompFor(___pawn)?.MaintainCombatFacing();
        }
    }

    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.GearMass))]
    public static class Harmony_WeaponWheel_GearMass
    {
        public static void Postfix(Pawn p, ref float __result)
        {
            Comp_WeaponWheel comp = WeaponWheelHarmonyUtility.CompFor(p);
            if (comp != null)
            {
                __result += comp.ReserveWeaponMass;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetChildHolders))]
    public static class Harmony_WeaponWheel_GetChildHolders
    {
        public static void Postfix(Pawn __instance, List<IThingHolder> outChildren)
        {
            Comp_WeaponWheel comp = WeaponWheelHarmonyUtility.CompFor(__instance);
            if (comp != null && outChildren != null && !outChildren.Contains(comp))
            {
                outChildren.Add(comp);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DropAndForbidEverything))]
    public static class Harmony_WeaponWheel_DropAndForbidEverything
    {
        public static void Prefix(Pawn __instance, bool keepInventoryAndEquipmentIfInBed)
        {
            WeaponWheelHarmonyUtility.CompFor(__instance)?.HandleDropAndForbidEverything(keepInventoryAndEquipmentIfInBed);
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_PawnSpawned))]
    public static class Harmony_WeaponWheel_EquipmentPawnSpawned
    {
        public static void Postfix(Pawn_EquipmentTracker __instance)
        {
            Pawn pawn = __instance?.pawn;
            if (pawn != null && pawn.Downed && !pawn.GetPosture().InBed())
            {
                WeaponWheelHarmonyUtility.CompFor(pawn)?.HandleDropAndForbidEverything(false);
            }
        }
    }
}
