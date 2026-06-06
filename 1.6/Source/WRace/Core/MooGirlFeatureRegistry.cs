using System.Collections.Generic;

namespace MooGirl
{
    internal static class MooGirlFeatureRegistry
    {
        private static readonly IReadOnlyList<MooGirlFeatureDescriptor> features = new List<MooGirlFeatureDescriptor>
        {
            new MooGirlFeatureDescriptor("Core", "Bootstrap, logging, shared Def caches, identity helpers, and patch diagnostics."),
            new MooGirlFeatureDescriptor("Milk", "Body-resource production, milking, breastfeeding, drinking milk, and milk visuals."),
            new MooGirlFeatureDescriptor("Apparel", "Adult-content filtering, apparel-tag restoration, restraint apparel, and keys."),
            new MooGirlFeatureDescriptor("Incidents", "Opening pod crash, refugee pod crash, wanderer joins, wild slaves, and courier raids."),
            new MooGirlFeatureDescriptor("Roping", "Rope links, restraint information, prisoner escape prevention, and roped AI."),
            new MooGirlFeatureDescriptor("Mounting", "Mount containers, dismount safety, mounted rendering, and mounted combat."),
            new MooGirlFeatureDescriptor("Genes", "Biotech xenotype correction, birth hooks, juvenile body graphics, and DLC gates."),
            new MooGirlFeatureDescriptor("Compatibility", "HAR, Ideology, Anomaly, and cross-mod reflection boundaries.")
        };

        internal static IReadOnlyList<MooGirlFeatureDescriptor> Features => features;
    }

    internal readonly struct MooGirlFeatureDescriptor
    {
        internal MooGirlFeatureDescriptor(string name, string responsibility)
        {
            Name = name;
            Responsibility = responsibility;
        }

        internal string Name { get; }

        internal string Responsibility { get; }
    }
}
