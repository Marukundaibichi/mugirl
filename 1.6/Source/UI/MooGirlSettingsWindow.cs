using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    internal static class MooGirlSettingsWindow
    {
        internal static void Draw(Rect inRect, MechanoidWorkControlSettings settings)
        {
            settings.DoWindowContents(inRect);
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.Gap(12f);

            listingStandard.CheckboxLabeled("MooGirl.Settings.StructuralCrashEvent.Label".Translate(), ref settings.enableStructuralCrashEvent,
                "MooGirl.Settings.StructuralCrashEvent.Desc".Translate());

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("MooGirl.Settings.FastMilking.Label".Translate(), ref settings.enableFastMilking,
                "MooGirl.Settings.FastMilking.Desc".Translate());

            listingStandard.End();
        }
    }
}
