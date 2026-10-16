using Verse.Sound;

namespace Mugirl.Features.Milk;

// 手动榨乳完成后的喷乳反馈：奶渍、浮动文字与音效。
internal static class MilkEffectUtility
{
    // 这些数值锁定常量来自旧的内联 SpawnMilkEffect 实现；它们只控制可见反馈，未经确认不得修改。
    private const int MinFilthCount = 1;
    private const int MaxFilthCount = 3;
    private const float TextDuration = 3f;
    private const string MilkSprayTextKey = "Mugirl.Milk.SprayText";

    internal static void SpawnMilkSpray(Pawn? pawn)
    {
        Map? map = pawn?.Map;
        if (pawn == null || map == null)
        {
            return;
        }

        SpawnSprayFilth(pawn, map);
        MoteMaker.ThrowText(pawn.DrawPos, map, MilkSprayTextKey.Translate(), UnityColor.Cyan, TextDuration);
        MilkDefOf.Mugirl_Milking_Sound.PlayOneShot(new TargetInfo(pawn.Position, map));
    }

    private static void SpawnSprayFilth(Pawn pawn, Map map)
    {
        ThingDef milkFilth = MilkDefOf.Mugirl_Filth_MilkStain;
        int count = Rand.RangeInclusive(MinFilthCount, MaxFilthCount);
        for (int i = 0; i < count; i++)
        {
            IntVec3 position = pawn.Position + pawn.Rotation.FacingCell
                + new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));

            if (position.InBounds(map))
            {
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(milkFilth), position, map, ThingPlaceMode.Near);
            }
        }
    }
}
