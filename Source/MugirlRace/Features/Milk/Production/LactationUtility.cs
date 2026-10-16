namespace Mugirl.Features.Milk;

// 分娩入口（Genes 模块的出生补丁调用）：确保雪牛娘母亲带有哺乳期 Hediff 并记录分娩时间。
internal static class LactationUtility
{
    internal static void NotifyBirth(Pawn? mother)
    {
        if (!MugirlIdentity.IsMugirlPawn(mother) || mother.Destroyed || mother.health?.hediffSet == null)
        {
            return;
        }

        HediffDef lactationDef = MilkDefOf.Mugirl_Lactation;
        Hediff? lactationHediff = mother.health.hediffSet.GetFirstHediffOfDef(lactationDef)
            ?? mother.health.AddHediff(lactationDef);
        lactationHediff?.TryGetComp<HediffComp_Lactation>()?.NotifyBirth();
    }
}
