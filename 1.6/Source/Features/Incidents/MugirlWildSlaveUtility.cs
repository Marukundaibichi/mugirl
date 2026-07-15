using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class MugirlWildSlaveUtility
    {
        public static Faction PlayerFaction => Faction.OfPlayerSilentFail;

        public static bool IsEscapeWildSlave(Pawn pawn)
        {
            return pawn != null && pawn.kindDef == Mugirl_DefOf.Mugirl_EscapeWildSlave;
        }

        public static bool IsWildMugirl(Pawn pawn)
        {
            return IsEscapeWildSlave(pawn) || pawn?.kindDef == Mugirl_DefOf.Mugirl_WildMugirl;
        }

        public static bool HasLegacyPlayerMugirlKind(Pawn pawn)
        {
            return pawn != null
                && (pawn.kindDef == Mugirl_DefOf.Mugirl_EscapeWildSlave
                    || pawn.kindDef == Mugirl_DefOf.Mugirl_WildMugirl
                    || pawn.kindDef == Mugirl_DefOf.Mugirl_PreEscapeWildSlave);
        }

        public static bool IsNonPlayerWildMugirl(Pawn pawn)
        {
            return IsWildMugirl(pawn) && !IsPlayerFaction(pawn.Faction);
        }

        public static bool IsMugirlPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            return MugirlIdentity.IsMugirlPawn(pawn)
                || pawn.kindDef == Mugirl_DefOf.Mugirl_EscapeWildSlave
                || pawn.kindDef == Mugirl_DefOf.Mugirl_WildMugirl
                || pawn.kindDef == Mugirl_DefOf.Mugirl_PreEscapeWildSlave;
        }

        public static bool IsPlayerFaction(Faction faction)
        {
            Faction playerFaction = PlayerFaction;
            return playerFaction != null && faction == playerFaction;
        }

        public static bool IsHostileToPlayer(Faction faction)
        {
            Faction playerFaction = PlayerFaction;
            return faction != null && playerFaction != null && faction.HostileTo(playerFaction);
        }

        public static bool TrySetPlayerFaction(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || PlayerFaction == null)
            {
                return false;
            }

            pawn.SetFaction(PlayerFaction);
            return IsPlayerFaction(pawn.Faction);
        }

        public static bool IsHostileToPlayer(Pawn pawn)
        {
            return pawn != null && IsHostileToPlayer(pawn.Faction);
        }

        public static bool CleanupAfterJoiningPlayer(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || !IsPlayerFaction(pawn.Faction))
            {
                return false;
            }

            bool changed = false;
            if (NormalizePlayerKindToFactionDefault(pawn))
            {
                changed = true;
            }

            if (MugirlEventUtility.ClearMigrationPawn(pawn))
            {
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

            if (IsMugirlPawn(pawn) && pawn.RaceProps?.Humanlike == true && pawn.training != null)
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

        public static int NormalizeLoadedPlayerPawnKinds()
        {
            int changed = 0;
            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                if (NormalizePlayerKindToFactionDefault(pawn))
                {
                    RefreshPawn(pawn);
                    changed++;
                }
            }

            return changed;
        }

        private static bool NormalizePlayerKindToFactionDefault(Pawn pawn)
        {
            if (!HasLegacyPlayerMugirlKind(pawn)
                || !IsPlayerFaction(pawn.Faction)
                || pawn.RaceProps?.Humanlike != true
                || pawn.IsQuestLodger())
            {
                return false;
            }

            PawnKindDef targetKind = pawn.Faction?.def?.basicMemberKind;
            if (targetKind == null || pawn.kindDef == targetKind)
            {
                return false;
            }

            pawn.ChangeKind(targetKind);
            return true;
        }

        public static void RefreshPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            PortraitsCache.SetDirty(pawn);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();

            MugirlGameUtility.TryMarkColonistsDirty();
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    internal static class Pawn_SetFaction_MugirlWildSlaveCleanup_Patch
    {
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (MugirlWildSlaveUtility.IsPlayerFaction(newFaction) && MugirlWildSlaveUtility.IsMugirlPawn(__instance))
            {
                MugirlWildSlaveUtility.CleanupAfterJoiningPlayer(__instance);
            }
        }
    }

}
