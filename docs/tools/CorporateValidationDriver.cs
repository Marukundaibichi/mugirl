// Optional validation-only update hook. Drivers are deliberately not GameComponents:
// RimWorld automatically instantiates and serializes every GameComponent subclass,
// even when the driver's own command-line safety gate disables its behavior.
using HarmonyLib;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class CorporateValidationDriver
    {
        // StaticCacheLifecycle: isolated test process only; each new Game replaces
        // its drivers. The runtime driver's three roundtrip assertions survive only
        // its own pending save/load and are never added to the game's save data.
        private static Game activeGame;
        private static CorporateRuntimeValidation runtime;
        private static CorporateVisualValidation visual;

        private static void Postfix(Game __instance)
        {
            bool runRuntime = GenCommandLine.CommandLineArgPassed("mugirlCorporateChecks");
            bool runVisual = GenCommandLine.CommandLineArgPassed("mugirlCorporateVisual");
            if (!runRuntime && !runVisual) return;

            if (!ReferenceEquals(activeGame, __instance))
            {
                activeGame = __instance;
                if (runRuntime)
                {
                    if (runtime?.AwaitingReload == true) runtime.ResumeAfterReload();
                    else runtime = new CorporateRuntimeValidation(__instance);
                }
                if (runVisual)
                {
                    if (visual?.AwaitingReload == true) visual.ResumeAfterReload();
                    else visual = new CorporateVisualValidation(__instance);
                }
            }
            runtime?.Update();
            visual?.Update();
        }
    }
}
