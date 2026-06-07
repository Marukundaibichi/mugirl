using HarmonyLib;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace MooGirl
{
    [HarmonyPatch]
    public static class Harmony_Patch_RopingDraw
    {
        // StaticCacheLifecycle: process-level reflection cache for Pawn_RopeTracker draw fields; no game objects are retained.
        private static readonly FieldInfo fieldPawn;
        private static readonly FieldInfo fieldRopeLineMat;

        static Harmony_Patch_RopingDraw()
        {
            fieldPawn = AccessTools.Field(typeof(Pawn_RopeTracker), "pawn");
            fieldRopeLineMat = AccessTools.Field(typeof(Pawn_RopeTracker), "RopeLineMat");
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Pawn_RopeTracker), "RopingDraw");
        }

        public static bool Prefix(Pawn_RopeTracker __instance)
        {
            if (fieldPawn == null || fieldRopeLineMat == null)
            {
                return true;
            }

            Pawn pawn = fieldPawn.GetValue(__instance) as Pawn;
            if (pawn?.Map == null)
            {
                return true;
            }

            Material ropeLineMat = fieldRopeLineMat.GetValue(null) as Material;
            if (ropeLineMat == null)
            {
                return true;
            }

            // 墙栓绘制只读取当前绳索状态，不扫描地图角色。
            if (RopingService.IsRopedToSpot(pawn) && pawn.roping?.RopedTo.IsValid == true)
            {
                IntVec3 ropeCell = pawn.roping.RopedTo.Cell;
                if (ropeCell.IsValid)
                {
                    Building hitch = ropeCell.GetFirstThing(pawn.Map, MooGirl_DefOf.WallRopeHitch) as Building;
                    if (hitch != null)
                    {
                        GenDraw.DrawLineBetween(
                            pawn.DrawPos.Yto0(),
                            WallHitchAnchor(hitch).Yto0(),
                            AltitudeLayer.PawnRope.AltitudeFor(),
                            ropeLineMat,
                            0.2f);

                        return false;
                    }
                }
            }

            return true;
        }

        private static Vector3 WallHitchAnchor(Building hitch)
        {
            Vector3 targetWithOffset = hitch.Position.ToVector3Shifted();

            // The wall hitch's usable rope point sits on the facing side of its occupied cell.
            switch (hitch.Rotation.AsInt)
            {
                case 0:
                    return targetWithOffset + new Vector3(0f, 0f, 0.5f);
                case 1:
                    return targetWithOffset + new Vector3(0.5f, 0f, 0f);
                case 2:
                    return targetWithOffset + new Vector3(0f, 0f, -0.5f);
                case 3:
                    return targetWithOffset + new Vector3(-0.5f, 0f, 0f);
                default:
                    return targetWithOffset;
            }
        }
    }
}
