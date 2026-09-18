using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    // 救援事件需要 pawn 以倒地状态落地。原版 HealthUtility.DamageUntilDowned 会持续加伤直到
    // 疼痛休克或失能；对雪牛娘这类高抗倒地种族（PainShockThreshold 0.9、IncomingDamageFactor 0.65、
    // 全身束具只压制容量不压制部位效率），伤势会持续累积到 LethalDamageThreshold（150 × healthScale）
    // 直接死亡。这里改为：先按 WouldDieAfterAddingHediff 守卫加少量可控轻伤制造迫降观感，
    // 再走 forceDowned + CheckForStateChange 无伤倒地；任何一步都不会杀死 pawn。
    internal static class MugirlPawnDowningUtility
    {
        private const float MinPartHealthForCosmeticInjury = 20f;
        private const float CosmeticInjurySeverityMin = 4f;
        private const float CosmeticInjurySeverityMax = 10f;

        private static readonly HediffDef[] CosmeticInjuryDefs = BuildCosmeticInjuryDefs();

        // Scratch/Bruise/Crack 没有 DefOf 字段，按 defName 静默解析；解析不到就跳过该种伤。
        private static HediffDef[] BuildCosmeticInjuryDefs()
        {
            string[] defNames = { "Cut", "Scratch", "Bruise", "Crack" };
            List<HediffDef> defs = new List<HediffDef>();
            for (int i = 0; i < defNames.Length; i++)
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defNames[i]);
                if (def != null)
                {
                    defs.Add(def);
                }
            }

            if (defs.Count == 0)
            {
                defs.Add(HediffDefOf.Cut);
            }

            return defs.ToArray();
        }

        // 返回 true 表示 pawn 存活且已倒地。调用方需保证倒地前置 hediff（如 Mugirl_Abasia）已加上。
        internal static bool DownRescuePawnSafely(Pawn pawn, int cosmeticInjuryCount = 4)
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

            int applied = 0;
            for (int i = 0; i < candidates.Count && applied < count; i++)
            {
                BodyPartRecord part = candidates.RandomElement();
                HediffDef def = CosmeticInjuryDefs.RandomElement();
                float severity = Rand.Range(CosmeticInjurySeverityMin, CosmeticInjurySeverityMax);
                if (pawn.Dead || pawn.health.WouldDieAfterAddingHediff(def, part, severity))
                {
                    continue;
                }

                Hediff injury = pawn.health.AddHediff(def, part);
                if (injury == null)
                {
                    continue;
                }

                injury.Severity = severity;
                applied++;
            }
        }

        private static void ForceDownWithoutInjuries(Pawn pawn)
        {
            // Abasia 类前置 hediff 把 Moving 压到失能线下时 ShouldBeDowned 必然为真；
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
}
