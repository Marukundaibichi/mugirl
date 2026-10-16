using HarmonyLib;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(Verb), nameof(Verb.ExposeData))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Renamed verb owners replace loaded verbs and lose their in-flight burst state.")]
internal static class Harmony_Verb_ExposeData_LoadId
{
    internal static void Postfix(Verb __instance)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars
            && LoadIdRemapper.TryForDocument(SaveUpgradeRuntime.ActiveSession, Scribe.loader?.curXmlParent, out LoadIdRemapper? aliases))
        {
            string? renamed = aliases.VerbIdentity(__instance.loadID);
            SaveUpgradeRuntime.ActiveSession!.ReadTrace?.Scalar("VerbIdentity", Scribe.loader?.curXmlParent?["loadID"], __instance.loadID, renamed);
            __instance.loadID = renamed;
        }
    }
}
