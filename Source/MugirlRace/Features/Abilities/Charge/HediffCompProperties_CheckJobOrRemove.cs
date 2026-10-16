namespace Mugirl.Features.Abilities;

// 冲锋加速 Hediff 的 XML 配置（1.7/Mods/Biotech/Defs/HediffDefs/Hediffs_Gene.xml）：在 Disappears 倒计时之外，
// 要求 Pawn 正在执行指定 Job。
public class HediffCompProperties_CheckJobOrRemove : HediffCompProperties_Disappears
{
    // 维持该 Hediff 时要求 Pawn 正在执行的 Job；未配置时只保留 Disappears 的倒计时。
    public JobDef? requiredJob;

    public HediffCompProperties_CheckJobOrRemove()
    {
        compClass = typeof(HediffComp_CheckJobOrRemove);
    }
}
