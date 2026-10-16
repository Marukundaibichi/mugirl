namespace Mugirl.Features.Incidents;

// 惩罚者死亡的爆裂特效；灌篮落地（Genes）以 1.35 倍规模复用。
internal static class PunisherDeathEffectUtility
{
    private const float MinScale = 0.1f;
    private const float BaseBurstCount = 8f;

    internal static void Play(UVector3 origin, Map? map, float scale = 1f)
    {
        if (map == null)
        {
            return;
        }

        scale = UnityMathf.Max(MinScale, scale);
        FleckMaker.Static(origin, map, IncidentsDefOf.ShockwaveFast, 0.025f * scale);
        FleckMaker.Static(origin, map, FleckDefOf.ExplosionFlash, 4.5f * scale);
        FleckMaker.ThrowLightningGlow(origin, map, 3f * scale);
        FleckMaker.ThrowSmoke(origin, map, 3.4f * scale);
        FleckMaker.ThrowDustPuff(origin, map, 4.2f * scale);
        int burstCount = UnityMathf.RoundToInt(BaseBurstCount * scale);
        Vector3 center = origin.ToNumerics();
        for (int i = 0; i < burstCount; i++)
        {
            UVector3 burstPosition = (center + Gen.RandomHorizontalVector(Rand.Range(0.3f, 1.8f) * scale).ToNumerics()).ToUnity();
            FleckMaker.ThrowMicroSparks(burstPosition, map);
            if (i % 2 == 0)
            {
                // 1.7 的 ThrowDustPuffThick 不使用颜色参数；保留白色与 1.6 一致。
                FleckMaker.ThrowDustPuffThick(burstPosition, map, Rand.Range(0.7f, 1.5f) * scale, UnityColor.White);
            }
        }
    }
}
