using HarmonyLib;
using System.Collections.Generic;
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
            settings = GetSettings<MechanoidWorkControlSettings>();
        }

        public static List<SlaveApparelDef> allArmorDefs = new List<SlaveApparelDef>();

        public static Harmony harmony;

        public static MechanoidWorkControlSettings settings;

        // 构造函数
        //public MooGirlMod(ModContentPack content) : base(content)
        //{
        //    settings = GetSettings<MechanoidWorkControlSettings>();

        //}

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
    // 设置类
    public class MechanoidWorkControlSettings : ModSettings
    {

        public override void ExposeData()
        {
            base.ExposeData();

            // 保存和加载牛牛坠机事件是否启用 的值
            Scribe_Values.Look(ref enableStructuralCrashEvent, "enableStructuralCrashEvent", true);
            Scribe_Values.Look(ref enableFastMilking, "enableFastMilking", false);
        }

        public void DoWindowContents(Rect rect)
        {
            // 全局设置
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(rect);
            listingStandard.End();
        }
        // 控制牛牛坠机事件事件是否启用的开关
        public bool enableStructuralCrashEvent = true;

        public bool enableFastMilking = false;
    }
}
