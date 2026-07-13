using Verse;

namespace Mugirl
{
    public sealed class CompProperties_AutoloadingSystem : CompProperties
    {
        public bool skipCycleReload = true;

        public CompProperties_AutoloadingSystem()
        {
            compClass = typeof(CompAutoloadingSystem);
        }
    }

    public sealed class CompAutoloadingSystem : ThingComp
    {
        public bool SkipCycleReload => ((CompProperties_AutoloadingSystem)props).skipCycleReload;
    }
}
