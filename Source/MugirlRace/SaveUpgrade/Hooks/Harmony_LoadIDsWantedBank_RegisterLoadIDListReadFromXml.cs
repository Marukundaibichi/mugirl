using HarmonyLib;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(LoadIDsWantedBank), nameof(LoadIDsWantedBank.RegisterLoadIDListReadFromXml),
    [typeof(List<string>), typeof(string), typeof(IExposable)])]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Legacy reference collections are not remapped to their upgraded identities.")]
internal static class Harmony_LoadIDsWantedBank_RegisterLoadIDListReadFromXml
{
    internal static void Prefix(LoadIDsWantedBank __instance, ref List<string>? targetLoadIDList)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars
            && ReferenceEquals(__instance, Scribe.loader?.crossRefs.loadIDs)
            && LoadIdRemapper.TryForDocument(SaveUpgradeRuntime.ActiveSession, Scribe.loader.curXmlParent, out LoadIdRemapper? aliases))
        {
            targetLoadIDList = aliases.RegisteredReferences(targetLoadIDList, Scribe.loader.curXmlParent, SaveUpgradeRuntime.ActiveSession!);
        }
    }
}
