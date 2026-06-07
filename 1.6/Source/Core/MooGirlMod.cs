using UnityEngine;
using Verse;

namespace MooGirl
{
    [StaticConstructorOnStartup]
    public class MooGirlMod : Mod
    {
        private static MooGirlSettings settings;

        public MooGirlMod(ModContentPack modContentPack) : base(modContentPack)
        {
            MooGirlBootstrap.Initialize();
            settings = GetSettings<MooGirlSettings>();
        }

        internal static MooGirlSettings Settings => settings;

        public override string SettingsCategory()
        {
            return "MooGirl.Settings.Category".Translate().ToString();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            MooGirlSettingsWindow.Draw(inRect, Settings);
            base.DoSettingsWindowContents(inRect);
        }
    }

    public class MooGirlSettings : ModSettings
    {
        public bool enableStructuralCrashEvent = true;

        public bool enableFastMilking = false;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref enableStructuralCrashEvent, "enableStructuralCrashEvent", true);
            Scribe_Values.Look(ref enableFastMilking, "enableFastMilking", false);
        }
    }
}
