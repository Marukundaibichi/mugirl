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
                        Vector3 targetWithOffset = hitch.Position.ToVector3Shifted();

                        // 墙面栓点的贴图中心不等于挂绳点，按朝向修正视觉终点。
                        switch (hitch.Rotation.AsInt)
                        {
                            case 0:
                                targetWithOffset += new Vector3(0f, 0f, -0.4f);
                                break;
                            case 1:
                                targetWithOffset += new Vector3(-0.5f, 0f, 0f);
                                break;
                            case 2:
                                targetWithOffset += new Vector3(0f, 0f, 0.5f);
                                break;
                            case 3:
                                targetWithOffset += new Vector3(0.4f, 0f, 0f);
                                break;
                        }

                        GenDraw.DrawLineBetween(
                            pawn.DrawPos.Yto0(),
                            targetWithOffset.Yto0(),
                            AltitudeLayer.PawnRope.AltitudeFor(),
                            ropeLineMat,
                            0.2f);

                        return false;
                    }
                }
            }

            return true;
        }
    }
}
