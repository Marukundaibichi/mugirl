using Mugirl.Compatibility.ExternalHediffs;

namespace Mugirl.Features.Milk;

// 外部乳房 Hediff（RJW 等可选 mod）对产量的倍率。每 60 tick 结算产奶时调用一次。
internal static class BreastProfileUtility
{
    internal static bool TryGetYieldMultiplier(Pawn? pawn, out float multiplier)
    {
        BreastProfile profile = Scan(pawn);
        return MilkProductionMath.TryGetBreastYieldMultiplier(profile, out multiplier);
    }

    // 只遍历一次 Hediff 列表；没有安装任何乳房 mod 时（10 个 Def 全部缺失）完全跳过遍历。
    private static BreastProfile Scan(Pawn? pawn)
    {
        HediffDef? huge = ExternalBreastHediffDefs.HugeBreasts;
        HediffDef? bionic = ExternalBreastHediffDefs.BionicBreasts;
        HediffDef? slime = ExternalBreastHediffDefs.SlimeBreasts;
        HediffDef? mammaries = ExternalBreastHediffDefs.GrMuffaloMammaries;
        HediffDef? breasts = ExternalBreastHediffDefs.Breasts;
        HediffDef? hydraulic = ExternalBreastHediffDefs.HydraulicBreasts;
        HediffDef? small = ExternalBreastHediffDefs.SmallBreasts;
        HediffDef? large = ExternalBreastHediffDefs.LargeBreasts;
        HediffDef? archotech = ExternalBreastHediffDefs.ArchotechBreasts;
        HediffDef? flat = ExternalBreastHediffDefs.FlatBreasts;

        var profile = new BreastProfile();
        List<Hediff>? hediffs = pawn?.health?.hediffSet?.hediffs;
        bool anyExternalDef = huge != null || bionic != null || slime != null || mammaries != null || breasts != null
            || hydraulic != null || small != null || large != null || archotech != null || flat != null;
        if (hediffs != null && anyExternalDef)
        {
            for (int i = 0; i < hediffs.Count; i++)
            {
                HediffDef def = hediffs[i].def;
                if (def == null)
                {
                    continue;
                }

                if (def == huge || def == bionic || def == slime || def == mammaries)
                {
                    profile.Special = true;
                }
                else if (def == breasts || def == hydraulic)
                {
                    profile.Normal = true;
                }
                else if (def == small)
                {
                    profile.Small = true;
                }
                else if (def == large || def == archotech)
                {
                    profile.Large = true;
                }
                else if (def == flat)
                {
                    profile.FlatOrMale = true;
                }
            }
        }

        // 未安装提供 Breasts 的 mod 时，女性按普通乳房计算；男性按平坦计算（与 1.6 相同）。
        Gender? gender = pawn?.gender;
        if (gender == Gender.Female && breasts == null)
        {
            profile.Normal = true;
        }

        if (gender == Gender.Male)
        {
            profile.FlatOrMale = true;
        }

        return profile;
    }
}
