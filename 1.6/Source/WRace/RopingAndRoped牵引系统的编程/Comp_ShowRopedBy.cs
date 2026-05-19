using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace MooGirl
{
    public class CompProperties_ShowRopedBy : CompProperties
    {
        public CompProperties_ShowRopedBy()
        {
            this.compClass = typeof(CompShowRopedBy);
        }
    }

    public class CompShowRopedBy : ThingComp
    {
        public Pawn Pawn => this.parent as Pawn;

        public override string CompInspectStringExtra()
        {
            if (Pawn == null || Pawn.roping == null)
                return null;

            Pawn_RopeTracker roping = Pawn.roping;


            // 1. 被其他 Pawn 牵引
            if (roping.IsRopedByPawn)
            {
                return "MooGirl.RopedByPawn".Translate();
            }
            // 2. 被建筑物牵引（比如马棚）
            else if (roping.IsRopedToHitchingPost)
            {
                return "MooGirl.RopedToHitchingPost".Translate();
            }
            // 3. 被地面某点牵引
            else if (roping.IsRopedToSpot)
            {
                return "MooGirl.RopedToSpot".Translate();
            }

            return null;
        }

    }
}
