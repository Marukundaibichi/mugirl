using Verse;

namespace MooGirl
{
    internal static class MooGirlIdentity
    {
        internal static bool IsMooGirlDef(Pawn pawn)
        {
            return pawn?.def == MooGirl_DefOf.MooGirl;
        }

        internal static bool IsMooGirlDef(ThingDef thingDef)
        {
            return thingDef == MooGirl_DefOf.MooGirl;
        }

        internal static bool HasMooGirlBody(Pawn pawn)
        {
            return pawn?.RaceProps?.body == MooGirl_DefOf.MooGirlBody;
        }

        internal static bool HasMooGirlBody(ThingDef thingDef)
        {
            return thingDef?.race?.body == MooGirl_DefOf.MooGirlBody;
        }

        internal static bool IsMooGirlPawn(Pawn pawn)
        {
            // 将历史种族判定集中在一处：部分生成 pawn 使用 MooGirl ThingDef，
            // 兼容路径则可能通过同一个 body 识别目标。
            return IsMooGirlDef(pawn) || HasMooGirlBody(pawn);
        }

        internal static bool IsMooGirlPawnDef(ThingDef thingDef)
        {
            return thingDef?.category == ThingCategory.Pawn && (IsMooGirlDef(thingDef) || HasMooGirlBody(thingDef));
        }
    }
}
