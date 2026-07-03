using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mugirl
{
    internal static class MugirlMilkEffectUtility
    {
        // 这些数值锁定常量来自旧的内联 SpawnMilkEffect 实现。
        // 它们只控制可见反馈，未经确认不得修改。
        private const int MinFilthCount = 1;
        private const int MaxFilthCount = 3;
        private const float TextDuration = 3f;
        private const string MilkSprayTextKey = "Mugirl.Milk.SprayText";

        internal static void SpawnMilkSpray(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return;
            }

            SpawnSprayFilth(pawn);
            MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, MilkSprayTextKey.Translate(), Color.cyan, TextDuration);
            MugirlOptionalDefs.SoundDefs.MugirlMilkingSound?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
        }

        private static void SpawnSprayFilth(Pawn pawn)
        {
            ThingDef milkFilth = MugirlOptionalDefs.ThingDefs.MugirlMilkFilth;
            if (milkFilth == null)
            {
                return;
            }

            int count = Rand.RangeInclusive(MinFilthCount, MaxFilthCount);
            for (int i = 0; i < count; i++)
            {
                IntVec3 position = pawn.Position + pawn.Rotation.FacingCell
                    + new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));

                if (position.InBounds(pawn.Map))
                {
                    Thing filth = ThingMaker.MakeThing(milkFilth);
                    GenPlace.TryPlaceThing(filth, position, pawn.Map, ThingPlaceMode.Near);
                }
            }
        }
    }
}
