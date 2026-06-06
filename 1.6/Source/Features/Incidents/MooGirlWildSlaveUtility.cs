using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    internal static class MooGirlWildSlaveUtility
    {
        public static bool IsEscapeWildSlave(Pawn pawn)
        {
            return pawn != null && pawn.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave;
        }

        public static bool IsMooGirlPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            return MooGirlIdentity.IsMooGirlPawn(pawn)
                || pawn.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave
                || pawn.kindDef == MooGirl_DefOf.MooGirl_PreEscapeWildSlave;
        }

        public static bool IsPlayerFaction(Faction faction)
        {
            Faction playerFaction = Faction.OfPlayerSilentFail;
            return playerFaction != null && faction == playerFaction;
        }

        public static bool NormalizeAfterJoiningPlayer(Pawn pawn, bool wasEscapeWildSlave = false)
        {
            if (pawn == null || pawn.Destroyed || !IsPlayerFaction(pawn.Faction))
            {
                return false;
            }

            bool changed = false;
            bool shouldUseNonWildKind = wasEscapeWildSlave || pawn.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave;
            if (shouldUseNonWildKind && MooGirl_DefOf.MooGirl_PreEscapeWildSlave != null && pawn.kindDef != MooGirl_DefOf.MooGirl_PreEscapeWildSlave)
            {
                pawn.ChangeKind(MooGirl_DefOf.MooGirl_PreEscapeWildSlave);
                changed = true;
            }

            if (pawn.mindState != null)
            {
                if (pawn.mindState.WillJoinColonyIfRescued)
                {
                    pawn.mindState.WillJoinColonyIfRescued = false;
                    changed = true;
                }

                if (pawn.mindState.WildManEverReachedOutside)
                {
                    pawn.mindState.WildManEverReachedOutside = false;
                    changed = true;
                }
            }

            Map map = pawn.MapHeld;
            if (map != null)
            {
                Designation tameDesignation = map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame);
                if (tameDesignation != null)
                {
                    map.designationManager.RemoveDesignation(tameDesignation);
                    changed = true;
                }
            }

            if (IsMooGirlPawn(pawn) && pawn.RaceProps?.Humanlike == true && pawn.training != null)
            {
                if (pawn.playerSettings != null)
                {
                    pawn.playerSettings.Master = null;
                    pawn.playerSettings.followDrafted = false;
                    pawn.playerSettings.followFieldwork = false;
                    pawn.playerSettings.animalsReleased = false;
                    pawn.playerSettings.animalForage = false;
                    pawn.playerSettings.animalDig = false;
                }

                pawn.training = null;
                changed = true;
            }

            if (changed)
            {
                RefreshPawn(pawn);
            }

            return changed;
        }

        public static bool NormalizePlayerPawnIfNeeded(Pawn pawn)
        {
            if (pawn == null || !IsPlayerFaction(pawn.Faction))
            {
                return false;
            }

            if (!IsMooGirlPawn(pawn) && pawn.kindDef != MooGirl_DefOf.MooGirl_EscapeWildSlave)
            {
                return false;
            }

            return NormalizeAfterJoiningPlayer(pawn);
        }

        public static void RefreshPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            PortraitsCache.SetDirty(pawn);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();

            if (Current.ProgramState == ProgramState.Playing)
            {
                Find.ColonistBar.MarkColonistsDirty();
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    internal static class Pawn_SetFaction_MooGirlWildSlaveCleanup_Patch
    {
        public static void Prefix(Pawn __instance, out bool __state)
        {
            __state = MooGirlWildSlaveUtility.IsEscapeWildSlave(__instance);
        }

        public static void Postfix(Pawn __instance, Faction newFaction, bool __state)
        {
            if (MooGirlWildSlaveUtility.IsPlayerFaction(newFaction) && MooGirlWildSlaveUtility.IsMooGirlPawn(__instance))
            {
                MooGirlWildSlaveUtility.NormalizeAfterJoiningPlayer(__instance, __state);
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    internal static class PawnGenerator_GeneratePawn_MooGirlWildSlaveBirth_Patch
    {
        public static void Prefix(ref PawnGenerationRequest request)
        {
            if (MooGirlWildSlaveUtility.IsPlayerFaction(request.Faction)
                && request.KindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave
                && request.AllowedDevelopmentalStages.Newborn()
                && MooGirl_DefOf.MooGirl_PreEscapeWildSlave != null)
            {
                request.KindDef = MooGirl_DefOf.MooGirl_PreEscapeWildSlave;
            }
        }

        public static void Postfix(Pawn __result, PawnGenerationRequest request)
        {
            if (__result != null
                && MooGirlWildSlaveUtility.IsPlayerFaction(request.Faction)
                && request.AllowedDevelopmentalStages.Newborn()
                && __result.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave)
            {
                MooGirlWildSlaveUtility.NormalizeAfterJoiningPlayer(__result, true);
            }
        }
    }
}
