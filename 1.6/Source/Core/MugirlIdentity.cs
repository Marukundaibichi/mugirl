using Verse;

namespace Mugirl
{
    internal static class MugirlIdentity
    {
        internal static bool IsMugirlDef(Pawn pawn)
        {
            return pawn?.def == Mugirl_DefOf.Mugirl;
        }

        internal static bool IsMugirlDef(ThingDef thingDef)
        {
            return thingDef == Mugirl_DefOf.Mugirl;
        }

        internal static bool HasMugirlBody(Pawn pawn)
        {
            return pawn?.RaceProps?.body == Mugirl_DefOf.MugirlBody;
        }

        internal static bool HasMugirlBody(ThingDef thingDef)
        {
            return thingDef?.race?.body == Mugirl_DefOf.MugirlBody;
        }

        internal static bool IsMugirlPawn(Pawn pawn)
        {
            // 将历史种族判定集中在一处：部分生成 pawn 使用 Mugirl ThingDef，
            // 兼容路径则可能通过同一个 body 识别目标。
            return IsMugirlDef(pawn) || HasMugirlBody(pawn);
        }

        internal static bool IsMugirlPawnDef(ThingDef thingDef)
        {
            return thingDef?.category == ThingCategory.Pawn && (IsMugirlDef(thingDef) || HasMugirlBody(thingDef));
        }
    }
}
