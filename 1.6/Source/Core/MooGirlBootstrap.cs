using HarmonyLib;

namespace MooGirl
{
    internal static class MooGirlBootstrap
    {
        internal const string HarmonyId = "MooGirlMod.Mod";

        internal static Harmony Harmony { get; private set; }

        internal static void Initialize()
        {
            if (Harmony != null)
            {
                return;
            }

            Harmony = new Harmony(HarmonyId);
            Harmony.PatchAll();
            MooGirlPatchRegistry.RegisterManualPatches(Harmony);
        }
    }
}
