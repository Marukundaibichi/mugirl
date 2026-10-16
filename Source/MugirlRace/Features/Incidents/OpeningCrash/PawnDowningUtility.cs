namespace Mugirl.Features.Incidents;

// 救援事件需要 Pawn 以倒地状态落地。原版 HealthUtility.DamageUntilDowned 会持续加伤直到
// 疼痛休克或失能；对雪牛娘这类高抗倒地种族（PainShockThreshold 0.9、IncomingDamageFactor 0.65、
// 全身束具只压制容量不压制部位效率），伤势会持续累积到 LethalDamageThreshold（150 × healthScale）
// 直接死亡。这里改为：先按 WouldDieAfterAddingHediff 守卫加少量可控轻伤制造迫降观感，
// 再走 forceDowned + CheckForStateChange 无伤倒地；任何一步都不会杀死 Pawn。
internal static class PawnDowningUtility
{
    private const float MinPartHealthForCosmeticInjury = 20f;
    private const float CosmeticInjurySeverityMin = 4f;
    private const float CosmeticInjurySeverityMax = 10f;

    // 返回 true 表示 Pawn 存活且已倒地。调用方需保证倒地前置 Hediff（如 Mugirl_Abasia）已加上。
    internal static bool DownRescuePawnSafely(Pawn? pawn, int cosmeticInjuryCount = 4)
    {
        if (pawn == null || pawn.Dead || pawn.health == null)
        {
            return false;
        }

        ApplyCosmeticInjuries(pawn, cosmeticInjuryCount);
        if (pawn.Dead)
        {
            return false;
        }

        if (!pawn.health.Downed)
        {
            ForceDownWithoutInjuries(pawn);
        }

        return pawn.health.Downed && !pawn.Dead;
    }

    private static void ApplyCosmeticInjuries(Pawn pawn, int count)
    {
        // 轻伤只挑血量足够的部位；每处都过死亡守卫，总量远低于 LethalDamageThreshold。
        List<BodyPartRecord> candidates = new List<BodyPartRecord>();
        foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
        {
            if (pawn.health.hediffSet.GetPartHealth(part) >= MinPartHealthForCosmeticInjury)
            {
                candidates.Add(part);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        // 顺序与 1.6 的静态数组相同，随机结果分布不变；改用 DefOf 后不再在类型初始化时查 DefDatabase。
        HediffDef[] injuryDefs = [HediffDefOf.Cut, IncidentsDefOf.Scratch, IncidentsDefOf.Bruise, IncidentsDefOf.Crack];
        int applied = 0;
        for (int i = 0; i < candidates.Count && applied < count; i++)
        {
            BodyPartRecord part = candidates.RandomElement();
            HediffDef def = injuryDefs.RandomElement();
            float severity = Rand.Range(CosmeticInjurySeverityMin, CosmeticInjurySeverityMax);
            if (pawn.Dead || pawn.health.WouldDieAfterAddingHediff(def, part, severity))
            {
                continue;
            }

            // 骨裂只出现在骨骼等坚硬部位（原版钝伤同样如此）；软组织上跳过这一次。
            if (def == IncidentsDefOf.Crack && !part.def.IsSolid(part, pawn.health.hediffSet.hediffs))
            {
                continue;
            }

            // 先设严重度再加入，与死亡守卫检查的是同一个状态；1.6 先以默认严重度加入、再改严重度，多触发一次状态检查。
            Hediff injury = HediffMaker.MakeHediff(def, pawn, part);
            injury.Severity = severity;
            pawn.health.AddHediff(injury, part);
            applied++;
        }
    }

    private static void ForceDownWithoutInjuries(Pawn pawn)
    {
        // Abasia 类前置 Hediff 把 Moving 压到失能线下时 ShouldBeDowned 必然为真；
        // 先清容量缓存避免读到生成阶段的旧值，forceDowned 则跳过“倒地即死”随机判定。
        pawn.health.capacities.Clear();
        pawn.health.forceDowned = true;
        try
        {
            pawn.health.CheckForStateChange(null, null);
        }
        finally
        {
            pawn.health.forceDowned = false;
        }
    }
}
