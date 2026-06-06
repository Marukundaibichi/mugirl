using HarmonyLib;
using UnityEngine;
using Verse;

namespace MooGirl
{
    [StaticConstructorOnStartup]
    public class MooGirlMod : Mod
    {
        public MooGirlMod(ModContentPack modContentPack) : base(modContentPack)
        {
            MooGirlBootstrap.Initialize();
            harmony = MooGirlBootstrap.Harmony;
            settings = GetSettings<MooGirlSettings>();
        }

        public static Harmony harmony;

        public static MooGirlSettings settings;

        public override string SettingsCategory()
        {
            return "MooGirl.Settings.Category".Translate().ToString();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            MooGirlSettingsWindow.Draw(inRect, settings);
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
