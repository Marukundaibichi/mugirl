using Verse;

namespace MooGirl
{
    [StaticConstructorOnStartup]
    public static class AiGenerated_Init
    {
        static AiGenerated_Init()
        {
            // GameComponent will be auto-registered when added to Current.Game
            // by RimWorld's game loading mechanism via the reflection-based setup
            Log.Message("[MooGirl AiGenerated] Initialized.");
        }
    }
}
