using HarmonyLib;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(LoadIDsWantedBank), nameof(LoadIDsWantedBank.RegisterLoadIDReadFromXml),
    [typeof(string), typeof(Type), typeof(string), typeof(IExposable)])]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Legacy object references are not remapped to their upgraded identities.")]
internal static class Harmony_LoadIDsWantedBank_RegisterLoadIDReadFromXml
{
    internal static void Prefix(LoadIDsWantedBank __instance, ref string? targetLoadID, Type targetType, string pathRelToParent, IExposable parent)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars
            && ReferenceEquals(__instance, Scribe.loader?.crossRefs.loadIDs)
            && LoadIdRemapper.TryForDocument(SaveUpgradeRuntime.ActiveSession, Scribe.loader.curXmlParent, out LoadIdRemapper? aliases))
        {
            string? renamed = aliases.Reference(targetLoadID);
            SaveUpgradeRuntime.ActiveSession!.ReadTrace?.Reference(Scribe.loader.curXmlParent,
                Scribe.loader.curPathRelToParent, pathRelToParent, Scribe.loader.curParent, parent, targetType, targetLoadID, renamed);
            targetLoadID = renamed;
        }
    }
}
