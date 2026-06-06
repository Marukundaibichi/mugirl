using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(ForbidUtility))]
    [HarmonyPatch("SetForbidden")]
    public static class FobidPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(this Thing t, bool value, bool warnOnFail = true)
        {
            if (t == null)
            {
                return false;
            }
            ThingWithComps thingWithComps = t as ThingWithComps;
            if (thingWithComps == null)
            {
                return false;
            }
            CompForbiddable comp = thingWithComps.GetComp<CompForbiddable>();
            if (comp == null)
            {
                return false;
            }
            comp.Forbidden = value;
            return false;
        }
    }
}
