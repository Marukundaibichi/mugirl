using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Grazing;

/// <summary>吃草相关的植物判定；迁徙事件（Incidents）也用它挑选可啃食的地图植物。</summary>
internal static class GrazeUtility
{
    internal static bool IsMapPlantFood([NotNullWhen(true)] Thing? thing)
    {
        return thing is Plant plant
            && plant.Spawned
            && !plant.Destroyed
            && plant.IngestibleNow
            && IsPlantFoodDef(plant.def);
    }

    internal static bool IsPlantFoodDef([NotNullWhen(true)] ThingDef? def)
    {
        return def is not null && typeof(Plant).IsAssignableFrom(def.thingClass) && def.ingestible is not null;
    }
}
