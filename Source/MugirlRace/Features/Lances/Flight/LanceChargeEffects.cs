namespace Mugirl.Features.Lances;

internal static class LanceChargeEffects
{
    /// <summary>落地冲击波；地图已卸载时不生成。</summary>
    internal static void ThrowShockwave(UVector3 position, Map? map)
    {
        if (map is not null && LancesDefOf.ShockwaveFast is not null)
        {
            FleckMaker.Static(position, map, LancesDefOf.ShockwaveFast, 0.025f);
        }
    }
}
