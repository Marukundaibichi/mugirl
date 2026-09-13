using Mugirl.Features.WeaponWheel;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class MugirlSettingsWindow
    {
        // StaticCacheLifecycle: UI-only pending invalidation; consumed on slider release.
        private static bool pendingOutlineRefresh;
        internal static void Draw(Rect inRect, MugirlSettings settings)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.Gap(12f);

            listingStandard.CheckboxLabeled("Mugirl.Settings.StructuralCrashEvent.Label".Translate(), ref settings.enableStructuralCrashEvent,
                "Mugirl.Settings.StructuralCrashEvent.Desc".Translate());

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("Mugirl.Settings.MigrationEvent.Label".Translate(), ref settings.enableMugirlMigrationEvent,
                "Mugirl.Settings.MigrationEvent.Desc".Translate());

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("Mugirl.Settings.FusionInvestmentEvent.Label".Translate(), ref settings.enableMugirlFusionInvestmentEvent,
                "Mugirl.Settings.FusionInvestmentEvent.Desc".Translate());

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("Mugirl.Settings.RunawayFarmQuest.Label".Translate(), ref settings.enableRunawayMugirlFarmQuest,
                "Mugirl.Settings.RunawayFarmQuest.Desc".Translate());

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("Mugirl.Settings.FastMilking.Label".Translate(), ref settings.enableFastMilking,
                "Mugirl.Settings.FastMilking.Desc".Translate());

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("Mugirl.Settings.VanillaMilkGauge.Label".Translate(), ref settings.enableVanillaMilkGauge,
                "Mugirl.Settings.VanillaMilkGauge.Desc".Translate());

            listingStandard.Gap(12f);
            bool outlineEnabled = settings.enableExtraOutline;
            float outlineWidth = settings.extraOutlineWidth;
            listingStandard.CheckboxLabeled("Mugirl.Settings.ExtraOutline.Label".Translate(), ref settings.enableExtraOutline,
                "Mugirl.Settings.ExtraOutline.Desc".Translate());
            if (settings.enableExtraOutline)
            {
                listingStandard.Label("Mugirl.Settings.ExtraOutline.Width".Translate(settings.extraOutlineWidth.ToString("0.00")));
                settings.extraOutlineWidth = Mathf.Round(listingStandard.Slider(settings.extraOutlineWidth,
                    MugirlExtraOutline.MinWidth, MugirlExtraOutline.MaxWidth) * 20f) / 20f;
            }
            if (outlineEnabled != settings.enableExtraOutline || outlineWidth != settings.extraOutlineWidth)
                pendingOutlineRefresh = true;
            if (pendingOutlineRefresh && GUIUtility.hotControl == 0)
            {
                pendingOutlineRefresh = false;
                MugirlExtraOutline.SettingsChanged();
            }

            listingStandard.Gap(12f);
            listingStandard.CheckboxLabeled("Mugirl.Settings.WeaponWheelDevLog.Label".Translate(), ref settings.enableWeaponWheelDevLog,
                "Mugirl.Settings.WeaponWheelDevLog.Desc".Translate());
            if (settings.enableWeaponWheelDevLog
                && listingStandard.ButtonText("Mugirl.Settings.WeaponWheelDevLog.DumpPatches".Translate()))
            {
                WeaponWheelDevLog.PrintPatchReport();
            }

            listingStandard.End();
        }
    }
}
