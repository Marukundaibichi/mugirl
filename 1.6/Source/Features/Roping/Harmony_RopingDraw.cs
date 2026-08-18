using HarmonyLib;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [HarmonyPatch]
    public static class Harmony_Patch_RopingDraw
    {
        // StaticCacheLifecycle: 进程级 Pawn_RopeTracker 绘制字段反射缓存；不持有游戏对象。
        private static readonly FieldInfo fieldRopeLineMat;
        private static readonly Material ropeLineMat;

        static Harmony_Patch_RopingDraw()
        {
            fieldRopeLineMat = AccessTools.Field(typeof(Pawn_RopeTracker), "RopeLineMat");
            ropeLineMat = fieldRopeLineMat?.GetValue(null) as Material;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Pawn_RopeTracker), "RopingDraw");
        }

        public static bool Prefix(Pawn_RopeTracker __instance, Pawn ___pawn)
        {
            // RopingDraw 会为每个正在绘制的 Pawn 调用。绝大多数 Pawn 没有拴到地点，
            // 必须在反射、地图索引和格子查询之前走完这个快路径。
            if (__instance?.IsRopedToSpot != true)
            {
                return true;
            }

            Pawn pawn = ___pawn;
            if (pawn?.Map == null)
            {
                return true;
            }

            if (fieldRopeLineMat == null || ropeLineMat == null)
            {
                return true;
            }

            LocalTargetInfo ropedTo = __instance.RopedTo;
            if (ropedTo.IsValid)
            {
                IntVec3 ropeCell = ropedTo.Cell;
                if (ropeCell.IsValid)
                {
                    Building hitch = ropeCell.GetFirstThing(pawn.Map, Mugirl_DefOf.WallRopeHitch) as Building;
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

            // 墙绳桩的可用绳点位于占用格朝向侧的边缘。
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
