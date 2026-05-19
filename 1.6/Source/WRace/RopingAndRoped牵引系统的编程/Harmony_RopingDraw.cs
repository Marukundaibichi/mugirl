using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using System.Reflection;

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
            Pawn pawn = (Pawn)fieldPawn.GetValue(__instance);
            if (pawn?.Map == null) return true;

            Material ropeLineMat = (Material)fieldRopeLineMat.GetValue(null);
            if (ropeLineMat == null) return true;

            // 绘制拴在墙上的绳索
            if (pawn.roping?.IsRopedToSpot == true)
            {
                IntVec3 ropeCell = pawn.roping.RopedTo.Cell;
                if (ropeCell.IsValid)
                {
                    Building hitch = ropeCell.GetFirstThing(pawn.Map, MooGirl_DefOf.WallRopeHitch) as Building;
                    if (hitch != null)
                    {
                        Vector3 targetWithOffset = hitch.Position.ToVector3Shifted();

                        // 根据朝向加偏移
                        switch (hitch.Rotation.AsInt)
                        {
                            case 0: // North
                                targetWithOffset += new Vector3(0f, 0f, -0.4f);
                                break;
                            case 1: // East
                                targetWithOffset += new Vector3(-0.5f, 0f, 0f);
                                break;
                            case 2: // South
                                targetWithOffset += new Vector3(0f, 0f, 0.5f);
                                break;
                            case 3: // West
                                targetWithOffset += new Vector3(0.4f, 0f, 0f);
                                break;
                        }

                        GenDraw.DrawLineBetween(
                            pawn.DrawPos.Yto0(),
                            targetWithOffset.Yto0(),
                            AltitudeLayer.PawnRope.AltitudeFor(),
                            ropeLineMat,
                            0.2f);

                        return false; // 拴墙时，不再执行原逻辑
                    }
                }
            }

            //// 绘制跟随者之间的绳索
            //List<Pawn> allPawns = pawn.Map.mapPawns.AllPawns;
            //for (int i = 0; i < allPawns.Count; i++)
            //{
            //    Pawn follower = allPawns[i];
            //    if (!follower.Spawned) continue;

            //    if (follower.RaceProps.body == MooGirl_DefOf.MooGirlBody &&
            //        follower.CurJob?.def == MooGirl_DefOf.Job_FollowRoper)
            //    {
            //        if (follower.CurJob.targetA.Thing is Pawn targetPawn && targetPawn.Spawned)
            //        {
            //            GenDraw.DrawLineBetween(
            //                follower.DrawPos.Yto0(),
            //                targetPawn.DrawPos.Yto0(),
            //                AltitudeLayer.PawnRope.AltitudeFor(),
            //                ropeLineMat,
            //                0.2f);
            //        }
            //    }
            //}


            return true;
        }
    }
}
