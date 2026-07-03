using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class MugirlSettingsWindow
    {
        internal static void Draw(Rect inRect, MugirlSettings settings)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.Gap(12f);

            listingStandard.CheckboxLabeled("Mugirl.Settings.StructuralCrashEvent.Label".Translate(), ref settings.enableStructuralCrashEvent,
                "Mugirl.Settings.StructuralCrashEvent.Desc".Translate());

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("Mugirl.Settings.FastMilking.Label".Translate(), ref settings.enableFastMilking,
                "Mugirl.Settings.FastMilking.Desc".Translate());

            listingStandard.End();
        }
    }
}
