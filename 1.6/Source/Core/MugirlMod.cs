using UnityEngine;
using Verse;

namespace Mugirl
{
    [StaticConstructorOnStartup]
    public class MugirlMod : Mod
    {
        private static MugirlSettings settings;

        public MugirlMod(ModContentPack modContentPack) : base(modContentPack)
        {
            MugirlBootstrap.Initialize();
            settings = GetSettings<MugirlSettings>();
        }

        internal static MugirlSettings Settings => settings;

        public override string SettingsCategory()
        {
            return "Mugirl.Settings.Category".Translate().ToString();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            MugirlSettingsWindow.Draw(inRect, Settings);
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class MugirlSettings : ModSettings
    {
        public bool enableStructuralCrashEvent = true;

        public bool enableMugirlMigrationEvent = true;

        public bool enableMugirlFusionInvestmentEvent = true;

        public bool enableRunawayMugirlFarmQuest = true;

        public bool enableFastMilking = false;

        public bool enableVanillaMilkGauge = false;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref enableStructuralCrashEvent, "enableStructuralCrashEvent", true);
            Scribe_Values.Look(ref enableMugirlMigrationEvent, "enableMugirlMigrationEvent", true);
            Scribe_Values.Look(ref enableMugirlFusionInvestmentEvent, "enableMugirlFusionInvestmentEvent", true);
            Scribe_Values.Look(ref enableRunawayMugirlFarmQuest, "enableRunawayMugirlFarmQuest", true);
            Scribe_Values.Look(ref enableFastMilking, "enableFastMilking", false);
            Scribe_Values.Look(ref enableVanillaMilkGauge, "enableVanillaMilkGauge", false);
        }
    }
}
