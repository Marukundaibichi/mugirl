using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    public sealed class MugirlRaceRestrictedExtension : DefModExtension
    {
        public bool enabled = true;
    }

    [HarmonyPatch]
    internal static class Harmony_MugirlRaceRestrictedWeapons
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(EquipmentUtility),
                nameof(EquipmentUtility.CanEquip),
                new[] { typeof(Thing), typeof(Pawn), typeof(string).MakeByRefType(), typeof(bool) });
        }

        private static void Postfix(Thing thing, Pawn pawn, ref string cantReason, ref bool __result)
        {
            MugirlRaceRestrictedExtension restriction = thing?.def?.GetModExtension<MugirlRaceRestrictedExtension>();
            if (!__result || restriction?.enabled != true || MugirlIdentity.IsMugirlPawn(pawn))
            {
                return;
            }

            __result = false;
            cantReason = "Mugirl.Weapon.MugirlOnly".Translate();
        }
    }
}
